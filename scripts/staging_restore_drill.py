#!/usr/bin/env python3
"""Fail-closed staging application restore drill for issue #18.

The live staging database is read only. Recovery happens in disposable Docker
volumes, and successful evidence is published only after the recovery project
has been removed with its volumes.
"""

from __future__ import annotations

import argparse
import base64
import dataclasses
import datetime as dt
import hashlib
import json
import math
import os
import pathlib
import re
import stat
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
from typing import Any, Mapping, Sequence


REPOSITORY_ROOT = pathlib.Path(__file__).resolve().parents[1]
RECOVERY_COMPOSE = REPOSITORY_ROOT / "deploy/staging/compose.restore-drill.yaml"
BACKUP_SCRIPT = REPOSITORY_ROOT / "scripts/postgresql-backup.sh"
RESTORE_SCRIPT = REPOSITORY_ROOT / "scripts/postgresql-restore-verify.sh"
POSTGRES_IMAGE = (
    "postgres:18@sha256:"
    "06cad38a5d9f5d24b4d83d86def30795d5e4b757fedbf5281172b576dedcd941"
)
DATABASE_NAME = re.compile(r"^[A-Za-z0-9_]+$")
SAFE_NAME = re.compile(r"^[A-Za-z0-9_.-]+$")
PROJECT_NAME = re.compile(r"^testapp-rto-[a-z0-9][a-z0-9_-]*$")
SEED_TAG = re.compile(r"^[A-Za-z0-9_.-]+$")
HOST_NAME = re.compile(r"^[A-Za-z0-9.-]+$")
IMMUTABLE_IMAGE = re.compile(r"^[^\s@]+@sha256:([0-9a-f]{64})$")
PUBLISH_OPERATION = re.compile(r"^tests\.publish:([0-9a-f]{32})$")
ACTOR_ID = re.compile(r"^[A-Za-z0-9._:-]+$")


class DrillError(RuntimeError):
    pass


def required(environment: Mapping[str, str], name: str) -> str:
    value = environment.get(name, "").strip()
    if not value:
        raise DrillError(f"Нужна переменная окружения {name}.")
    return value


def positive_integer(environment: Mapping[str, str], name: str, default: int | None = None) -> int:
    raw = environment.get(name, "").strip()
    if not raw and default is not None:
        return default
    if not raw or not raw.isdigit() or int(raw) < 1:
        raise DrillError(f"{name} должна быть положительным целым числом.")
    return int(raw)


@dataclasses.dataclass(frozen=True)
class DrillConfig:
    backup_path: pathlib.Path
    evidence_path: pathlib.Path
    compose_file: pathlib.Path
    staging_network: str
    project_name: str
    api_port: int
    timeout_seconds: int
    rto_target_seconds: int
    rpo_target_seconds: int
    min_database_bytes: int
    min_test_count: int
    min_idempotency_records: int
    seed_tag: str
    public_host: str
    image: str
    image_digest: str
    postgres_database: str
    postgres_user: str
    postgres_password: str
    rabbitmq_user: str
    rabbitmq_password: str
    client_id: str
    client_secret: str
    keycloak_realm: str
    admin_username: str
    admin_password: str
    author_username: str
    author_password: str
    confirmation: str

    @property
    def recovery_url(self) -> str:
        return f"http://127.0.0.1:{self.api_port}"

    @property
    def keycloak_url(self) -> str:
        return f"https://{self.public_host}/auth"

    @property
    def recovery_network(self) -> str:
        return f"{self.project_name}_default"

    @classmethod
    def from_environment(
        cls,
        backup_path: pathlib.Path,
        evidence_path: pathlib.Path,
        environment: Mapping[str, str],
    ) -> "DrillConfig":
        public_host = required(environment, "TESTAPP_PUBLIC_HOST")
        image = required(environment, "TESTAPP_IMAGE")
        image_digest = required(environment, "TESTAPP_IMAGE_DIGEST")
        postgres_database = required(environment, "POSTGRES_DB")
        staging_network = environment.get("STAGING_NETWORK", "testapp-staging_default").strip()
        project_name = environment.get("RTO_PROJECT_NAME", f"testapp-rto-{os.getpid()}").strip()
        seed_tag = required(environment, "RTO_SEED_TAG")
        client_id = environment.get("KEYCLOAK_CLIENT_ID", "testapp-api").strip() or "testapp-api"
        keycloak_realm = (
            environment.get("KEYCLOAK_REALM", "testapp-staging").strip()
            or "testapp-staging"
        )
        match = IMMUTABLE_IMAGE.fullmatch(image)

        if not match:
            raise DrillError("TESTAPP_IMAGE должен ссылаться на immutable @sha256 digest.")
        if image_digest != f"sha256:{match.group(1)}":
            raise DrillError("TESTAPP_IMAGE_DIGEST не совпадает с digest в TESTAPP_IMAGE.")
        if not HOST_NAME.fullmatch(public_host):
            raise DrillError("TESTAPP_PUBLIC_HOST содержит неподдерживаемые символы.")
        if not DATABASE_NAME.fullmatch(postgres_database):
            raise DrillError("POSTGRES_DB содержит неподдерживаемые символы.")
        if not SAFE_NAME.fullmatch(staging_network):
            raise DrillError("STAGING_NETWORK содержит неподдерживаемые символы.")
        if not PROJECT_NAME.fullmatch(project_name):
            raise DrillError(
                "RTO_PROJECT_NAME должен начинаться с testapp-rto- и содержать "
                "только lowercase ASCII, цифры, '-' или '_'."
            )
        if not SEED_TAG.fullmatch(seed_tag):
            raise DrillError("RTO_SEED_TAG содержит неподдерживаемые символы.")
        if not SAFE_NAME.fullmatch(client_id) or not SAFE_NAME.fullmatch(keycloak_realm):
            raise DrillError("KEYCLOAK_CLIENT_ID/KEYCLOAK_REALM содержат неподдерживаемые символы.")

        resolved_backup = backup_path.expanduser().resolve()
        resolved_evidence = evidence_path.expanduser().resolve()
        forbidden = {
            resolved_backup,
            pathlib.Path(f"{resolved_backup}.sha256").resolve(),
            pathlib.Path(f"{resolved_backup}.tmp").resolve(),
        }
        if resolved_evidence in forbidden:
            raise DrillError("Evidence path должен отличаться от backup, checksum и backup temp.")

        api_port = positive_integer(environment, "RTO_API_PORT", 18080)
        if api_port > 65535:
            raise DrillError("RTO_API_PORT должен быть в диапазоне 1..65535.")

        rto_target_seconds = positive_integer(environment, "RTO_TARGET_SECONDS", 14_400)
        timeout_seconds = positive_integer(
            environment,
            "RTO_TIMEOUT_SECONDS",
            rto_target_seconds,
        )
        if timeout_seconds < rto_target_seconds:
            raise DrillError("RTO_TIMEOUT_SECONDS не может быть меньше RTO_TARGET_SECONDS.")

        return cls(
            backup_path=resolved_backup,
            evidence_path=resolved_evidence,
            compose_file=RECOVERY_COMPOSE,
            staging_network=staging_network,
            project_name=project_name,
            api_port=api_port,
            timeout_seconds=timeout_seconds,
            rto_target_seconds=rto_target_seconds,
            rpo_target_seconds=positive_integer(environment, "RPO_TARGET_SECONDS", 86_400),
            min_database_bytes=positive_integer(environment, "RTO_MIN_DATABASE_BYTES"),
            min_test_count=positive_integer(environment, "RTO_MIN_TEST_COUNT"),
            min_idempotency_records=positive_integer(environment, "RTO_MIN_IDEMPOTENCY_RECORDS"),
            seed_tag=seed_tag,
            public_host=public_host,
            image=image,
            image_digest=image_digest,
            postgres_database=postgres_database,
            postgres_user=required(environment, "POSTGRES_USER"),
            postgres_password=required(environment, "POSTGRES_PASSWORD"),
            rabbitmq_user=required(environment, "RABBITMQ_USER"),
            rabbitmq_password=required(environment, "RABBITMQ_PASSWORD"),
            client_id=client_id,
            client_secret=required(environment, "TESTAPP_KEYCLOAK_CLIENT_SECRET"),
            keycloak_realm=keycloak_realm,
            admin_username=required(environment, "ADMIN_USERNAME"),
            admin_password=required(environment, "ADMIN_PASSWORD"),
            author_username=required(environment, "AUTHOR_USERNAME"),
            author_password=required(environment, "AUTHOR_PASSWORD"),
            confirmation=environment.get("CONFIRM_STAGING_RESTORE_DRILL", "").strip(),
        )


class CommandRunner:
    def __init__(self, cwd: pathlib.Path = REPOSITORY_ROOT) -> None:
        self.cwd = cwd

    def run(
        self,
        arguments: Sequence[str],
        *,
        environment: Mapping[str, str] | None = None,
        input_bytes: bytes | None = None,
        check: bool = True,
        announce_output: bool = False,
    ) -> str:
        completed = subprocess.run(
            list(arguments),
            cwd=self.cwd,
            env=dict(environment) if environment is not None else None,
            input=input_bytes,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )
        stdout = completed.stdout.decode("utf-8", errors="replace")
        stderr = completed.stderr.decode("utf-8", errors="replace")
        if announce_output and stdout:
            print(stdout, end="")
        if completed.returncode != 0 and check:
            detail = stderr.strip() or stdout.strip() or "без диагностического вывода"
            raise DrillError(
                f"Команда {arguments[0]!r} завершилась с кодом {completed.returncode}: {detail}"
            )
        return stdout


class HttpClient:
    def request(
        self,
        method: str,
        url: str,
        *,
        headers: Mapping[str, str] | None = None,
        data: bytes | None = None,
        timeout: int = 30,
    ) -> tuple[int, bytes]:
        request = urllib.request.Request(
            url,
            data=data,
            headers=dict(headers or {}),
            method=method,
        )
        try:
            with urllib.request.urlopen(request, timeout=timeout) as response:
                return response.status, response.read()
        except urllib.error.HTTPError as error:
            return error.code, error.read()
        except urllib.error.URLError as error:
            raise DrillError(f"{url}: {error.reason}") from error
        except OSError as error:
            raise DrillError(f"{url}: {error}") from error


class SystemClock:
    @staticmethod
    def monotonic() -> float:
        return time.monotonic()

    @staticmethod
    def timestamp() -> float:
        return time.time()

    @staticmethod
    def sleep(seconds: float) -> None:
        time.sleep(seconds)

    @staticmethod
    def utc_now() -> dt.datetime:
        return dt.datetime.now(dt.timezone.utc)


def iso8601(value: dt.datetime) -> str:
    return value.astimezone(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def elapsed_seconds(clock: SystemClock, started: float) -> int:
    return max(0, math.ceil(clock.monotonic() - started))


def compose_arguments(config: DrillConfig, *arguments: str) -> list[str]:
    return [
        "docker",
        "compose",
        "--project-name",
        config.project_name,
        "--file",
        str(config.compose_file),
        *arguments,
    ]


SOURCE_METRICS_SQL = """
SELECT concat_ws('|',
  (SELECT COUNT(*) FROM information_schema.tables
   WHERE table_schema = 'public' AND table_type = 'BASE TABLE'),
  (SELECT COUNT(*) FROM "__EFMigrationsHistory"),
  pg_database_size(current_database()),
  (SELECT COUNT(*) FROM tests),
  (SELECT COUNT(*) FROM idempotency_records),
  (SELECT COUNT(*) FROM audit_entries),
  (SELECT COUNT(*) FROM outbox_messages
   WHERE "ProcessedAt" IS NULL AND "DeadLetteredAt" IS NULL AND "DiscardedAt" IS NULL),
  (SELECT COUNT(*) FROM outbox_messages
   WHERE "DeadLetteredAt" IS NOT NULL AND "DiscardedAt" IS NULL),
  (SELECT COUNT(*) FROM pg_locks
   WHERE locktype = 'advisory' AND database = (SELECT oid FROM pg_database WHERE datname = current_database()))
);
""".strip()


RECOVERY_METRICS_SQL = """
SELECT concat_ws('|',
  (SELECT COUNT(*) FROM audit_entries),
  (SELECT COUNT(*) FROM idempotency_records),
  (SELECT COUNT(*) FROM outbox_messages
   WHERE "ProcessedAt" IS NULL AND "DeadLetteredAt" IS NULL AND "DiscardedAt" IS NULL),
  (SELECT COUNT(*) FROM outbox_messages
   WHERE "DeadLetteredAt" IS NOT NULL AND "DiscardedAt" IS NULL),
  (SELECT COUNT(*) FROM pg_locks
   WHERE locktype = 'advisory' AND database = (SELECT oid FROM pg_database WHERE datname = current_database()))
);
""".strip()


def parse_numeric_row(raw: str, expected: int, label: str) -> list[int]:
    parts = raw.strip().split("|")
    if len(parts) != expected or any(not part.isdigit() for part in parts):
        raise DrillError(f"{label} вернул некорректные метрики: {raw.strip()!r}")
    return [int(part) for part in parts]


def source_scalar(config: DrillConfig, runner: CommandRunner, sql: str) -> str:
    environment = dict(os.environ)
    environment["PGPASSWORD"] = config.postgres_password
    return runner.run(
        [
            "docker",
            "run",
            "--rm",
            "--network",
            config.staging_network,
            "--env",
            "PGPASSWORD",
            POSTGRES_IMAGE,
            "psql",
            "--host=postgres",
            "--port=5432",
            f"--username={config.postgres_user}",
            f"--dbname={config.postgres_database}",
            "--no-align",
            "--tuples-only",
            "--quiet",
            "--set=ON_ERROR_STOP=1",
            f"--command={sql}",
        ],
        environment=environment,
    )


def recovery_scalar(config: DrillConfig, runner: CommandRunner, sql: str) -> str:
    return runner.run(
        compose_arguments(
            config,
            "exec",
            "--no-TTY",
            "recovery-postgres",
            "psql",
            f"--username={config.postgres_user}",
            f"--dbname={config.postgres_database}",
            "--no-align",
            "--tuples-only",
            "--quiet",
            "--set=ON_ERROR_STOP=1",
            f"--command={sql}",
        )
    )


def json_document(body: bytes, label: str) -> dict[str, Any]:
    try:
        value = json.loads(body)
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise DrillError(f"{label} вернул невалидный JSON.") from error
    if not isinstance(value, dict):
        raise DrillError(f"{label} должен вернуть JSON object.")
    return value


def request_token(
    config: DrillConfig,
    http: HttpClient,
    username: str,
    password: str,
) -> str:
    payload = urllib.parse.urlencode(
        {
            "client_id": config.client_id,
            "client_secret": config.client_secret,
            "grant_type": "password",
            "username": username,
            "password": password,
        }
    ).encode()
    status, body = http.request(
        "POST",
        f"{config.keycloak_url}/realms/{config.keycloak_realm}/protocol/openid-connect/token",
        headers={"Content-Type": "application/x-www-form-urlencoded"},
        data=payload,
    )
    if status != 200:
        raise DrillError(f"Keycloak token endpoint вернул HTTP {status}.")
    token = json_document(body, "Keycloak token endpoint").get("access_token")
    if not isinstance(token, str) or not token:
        raise DrillError("Keycloak token endpoint не вернул access_token.")
    return token


def jwt_subject(token: str) -> str:
    parts = token.split(".")
    if len(parts) != 3:
        raise DrillError("Access token не имеет JWT-формат.")
    try:
        payload = json.loads(base64.urlsafe_b64decode(parts[1] + "=" * (-len(parts[1]) % 4)))
    except (ValueError, UnicodeDecodeError, json.JSONDecodeError) as error:
        raise DrillError("Не удалось прочитать JWT payload.") from error
    subject = payload.get("sub") if isinstance(payload, dict) else None
    if not isinstance(subject, str) or not ACTOR_ID.fullmatch(subject):
        raise DrillError("JWT subject отсутствует или содержит неподдерживаемые символы.")
    return subject


def authenticated_json(
    http: HttpClient,
    method: str,
    url: str,
    token: str,
    *,
    headers: Mapping[str, str] | None = None,
    data: bytes | None = None,
    expected_status: int = 200,
    label: str,
) -> dict[str, Any]:
    all_headers = {"Authorization": f"Bearer {token}", **dict(headers or {})}
    status, body = http.request(method, url, headers=all_headers, data=data)
    if status != expected_status:
        raise DrillError(f"{label} вернул HTTP {status}, ожидался {expected_status}.")
    return json_document(body, label)


def published_revision_value(document: Mapping[str, Any], label: str) -> str:
    # IdempotencyStore сериализует strong ID через JsonSerializerOptions.Default (`Value`),
    # HTTP pipeline — web defaults (`value`). Сравниваем контрактное значение, а не casing.
    matches = [value for key, value in document.items() if key.casefold() == "value"]
    if len(document) != 1 or len(matches) != 1 or not isinstance(matches[0], str):
        raise DrillError(f"{label} не имеет контракт PublishedTestRevisionId.")
    value = matches[0]
    if not re.fullmatch(
        r"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-"
        r"[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        value,
    ):
        raise DrillError(f"{label} содержит некорректный revision ID.")
    return value.casefold()


def validate_build_information(
    document: Mapping[str, Any],
    config: DrillConfig,
    label: str,
) -> str:
    version = document.get("version")
    digest = document.get("imageDigest")
    if not isinstance(version, str) or not version.strip():
        raise DrillError(f"{label} не вернул version.")
    if digest != config.image_digest:
        raise DrillError(
            f"{label} запущен не из ожидаемого image digest: {digest!r} != "
            f"{config.image_digest!r}."
        )
    return version.strip()


def wait_for_readiness(
    config: DrillConfig,
    http: HttpClient,
    clock: SystemClock,
    recovery_started: float,
) -> int:
    deadline = recovery_started + config.timeout_seconds
    last_error = ""
    while clock.monotonic() < deadline:
        try:
            status, _ = http.request("GET", f"{config.recovery_url}/health/ready", timeout=5)
            if status == 200:
                return elapsed_seconds(clock, recovery_started)
            last_error = f"HTTP {status}"
        except DrillError as error:
            last_error = str(error)
        clock.sleep(1)
    raise DrillError(
        f"Recovery API не достиг /health/ready за {config.timeout_seconds} с"
        + (f": {last_error}" if last_error else ".")
    )


def validate_source_metrics(config: DrillConfig, values: Sequence[int]) -> dict[str, int]:
    (
        table_count,
        migration_count,
        database_bytes,
        test_count,
        idempotency_count,
        audit_count,
        outbox_pending,
        outbox_dead_letters,
        advisory_locks,
    ) = values
    if table_count < 1 or migration_count < 1:
        raise DrillError("Staging source не содержит application schema/migrations.")
    if database_bytes < config.min_database_bytes:
        raise DrillError(
            f"Staging database слишком мала: {database_bytes} < {config.min_database_bytes} байт."
        )
    if test_count < config.min_test_count:
        raise DrillError(f"Недостаточно тестов: {test_count} < {config.min_test_count}.")
    if idempotency_count < config.min_idempotency_records:
        raise DrillError(
            "Недостаточно idempotency records: "
            f"{idempotency_count} < {config.min_idempotency_records}."
        )
    if outbox_pending or outbox_dead_letters or advisory_locks:
        raise DrillError(
            "Source staging не готов к backup: "
            f"outbox pending={outbox_pending}, dead letters={outbox_dead_letters}, "
            f"advisory locks={advisory_locks}."
        )
    return {
        "tableCount": table_count,
        "migrationCount": migration_count,
        "databaseBytes": database_bytes,
        "testCount": test_count,
        "idempotencyRecordCount": idempotency_count,
        "auditEntryCount": audit_count,
        "outboxPendingCount": outbox_pending,
        "outboxDeadLetterCount": outbox_dead_letters,
        "advisoryLockCount": advisory_locks,
    }


def verify_backup(path: pathlib.Path) -> tuple[str, int]:
    checksum_path = pathlib.Path(f"{path}.sha256")
    if not path.is_file() or not checksum_path.is_file():
        raise DrillError("Backup script не создал dump и checksum sidecar.")
    digest_builder = hashlib.sha256()
    with path.open("rb") as stream:
        while chunk := stream.read(1024 * 1024):
            digest_builder.update(chunk)
    digest = digest_builder.hexdigest()
    checksum_parts = checksum_path.read_text(encoding="utf-8").split(maxsplit=1)
    expected = checksum_parts[0] if checksum_parts else ""
    if not re.fullmatch(r"[0-9a-f]{64}", expected):
        raise DrillError("Checksum sidecar не содержит валидный SHA-256.")
    if digest != expected:
        raise DrillError("SHA-256 backup не совпадает с checksum sidecar.")
    for private_path in (path, checksum_path):
        if stat.S_IMODE(private_path.stat().st_mode) & 0o077:
            raise DrillError(
                f"{private_path.name} должен быть недоступен group/other "
                "(ожидается mode 0600)."
            )
    return digest, path.stat().st_size


def validate_database_evidence(
    document: Mapping[str, Any],
    config: DrillConfig,
    source: Mapping[str, int],
    backup_sha256: str,
    backup_bytes: int,
) -> None:
    expected = {
        "schemaVersion": 2,
        "status": "passed",
        "scope": "database-restore-promoted",
        "sourceDatabase": config.postgres_database,
        "temporaryDatabase": f"{config.postgres_database}_restore_candidate",
        "restoredDatabase": config.postgres_database,
        "targetRetained": True,
        "backupSha256": backup_sha256,
        "backupBytes": backup_bytes,
        "tableCount": source["tableCount"],
        "migrationCount": source["migrationCount"],
        "customVerificationExecuted": True,
    }
    actual = {key: document.get(key) for key in expected}
    if actual != expected:
        raise DrillError(f"Database restore evidence нарушает контракт: {actual!r}")
    for field in ("backupBytes", "tableCount", "migrationCount"):
        if isinstance(document.get(field), bool) or not isinstance(document.get(field), int):
            raise DrillError(f"Database restore evidence field {field} должен быть integer.")
    timings = [
        document.get("databaseRestoreSeconds"),
        document.get("databaseVerificationSeconds"),
        document.get("databaseRecoverySeconds"),
    ]
    if any(isinstance(value, bool) or not isinstance(value, int) or value < 0 for value in timings):
        raise DrillError("Database restore evidence содержит некорректные timing fields.")
    if timings[2] < timings[0] + timings[1]:
        raise DrillError("Database recovery timing меньше суммы restore и verification.")


def cleanup_recovery(config: DrillConfig, runner: CommandRunner, *, check: bool) -> None:
    runner.run(
        compose_arguments(config, "down", "--volumes", "--remove-orphans", "--timeout", "30"),
        check=check,
    )


def assert_recovery_removed(config: DrillConfig, runner: CommandRunner) -> None:
    filter_value = f"label=com.docker.compose.project={config.project_name}"
    commands = {
        "containers": ["docker", "container", "ls", "--all", "--quiet", "--filter", filter_value],
        "volumes": ["docker", "volume", "ls", "--quiet", "--filter", filter_value],
        "networks": ["docker", "network", "ls", "--quiet", "--filter", filter_value],
    }
    remaining = [label for label, arguments in commands.items() if runner.run(arguments).strip()]
    if remaining:
        raise DrillError(
            "Recovery project не удалён полностью; остались: " + ", ".join(remaining) + "."
        )


def run_drill(
    config: DrillConfig,
    runner: CommandRunner,
    http: HttpClient,
    clock: SystemClock,
    private_directory: pathlib.Path,
) -> dict[str, Any]:
    if config.confirmation != "isolated-recovery":
        raise DrillError(
            "Для запуска задайте CONFIRM_STAGING_RESTORE_DRILL=isolated-recovery. "
            "Рабочая БД останется read-only; будут созданы disposable volumes."
        )

    runner.run(["docker", "network", "inspect", config.staging_network])
    live_status, _ = http.request("GET", f"https://{config.public_host}/health/ready")
    if live_status != 200:
        raise DrillError(f"Live staging /health/ready вернул HTTP {live_status}.")

    live_admin_token = request_token(
        config,
        http,
        config.admin_username,
        config.admin_password,
    )
    live_build = authenticated_json(
        http,
        "GET",
        f"https://{config.public_host}/api/v1/operations/version",
        live_admin_token,
        label="Live staging build information",
    )
    live_version = validate_build_information(
        live_build,
        config,
        "Live staging build information",
    )

    source_values = parse_numeric_row(
        source_scalar(config, runner, SOURCE_METRICS_SQL),
        9,
        "Source staging query",
    )
    source = validate_source_metrics(config, source_values)

    # Project name ограничен отдельным префиксом. Удаление остатков предыдущего drill до
    # backup гарантирует, что измерение начинается с действительно чистых volumes.
    cleanup_recovery(config, runner, check=True)
    assert_recovery_removed(config, runner)

    config.backup_path.parent.mkdir(parents=True, exist_ok=True)
    backup_started_at = clock.utc_now()
    backup_started_epoch = clock.timestamp()
    backup_environment = dict(os.environ)
    backup_environment.update(
        {
            "POSTGRES_DOCKER_NETWORK": config.staging_network,
            "POSTGRES_HOST": "postgres",
            "POSTGRES_DATABASE": config.postgres_database,
            "POSTGRES_USER": config.postgres_user,
            "POSTGRES_PASSWORD": config.postgres_password,
        }
    )
    runner.run(
        ["bash", str(BACKUP_SCRIPT), str(config.backup_path)],
        environment=backup_environment,
        announce_output=True,
    )
    backup_sha256, backup_bytes = verify_backup(config.backup_path)
    backup_completed_at = clock.utc_now()

    recovery_started_at = clock.utc_now()
    recovery_started_epoch = clock.timestamp()
    recovery_started = clock.monotonic()
    recovery_created = False
    cleaned = False
    database_evidence_path = private_directory / "database-restore.json"

    try:
        # Даже неуспешный `compose up` может успеть создать container/volume. С этого
        # момента finally всегда пытается удалить весь recovery project.
        recovery_created = True
        runner.run(
            compose_arguments(
                config,
                "up",
                "--detach",
                "--wait",
                "--wait-timeout",
                str(config.timeout_seconds),
                "recovery-postgres",
                "recovery-rabbitmq",
            ),
            announce_output=True,
        )
        clean_instance_ready_seconds = elapsed_seconds(clock, recovery_started)

        restore_environment = dict(os.environ)
        restore_environment.update(
            {
                "POSTGRES_DOCKER_NETWORK": config.recovery_network,
                "POSTGRES_HOST": "recovery-postgres",
                "POSTGRES_SOURCE_DATABASE": config.postgres_database,
                "POSTGRES_ADMIN_DATABASE": "postgres",
                "POSTGRES_ADMIN_USER": config.postgres_user,
                "POSTGRES_ADMIN_PASSWORD": config.postgres_password,
                "RESTORE_EXPECTED_TABLE_COUNT": str(source["tableCount"]),
                "RESTORE_EXPECTED_MIGRATION_COUNT": str(source["migrationCount"]),
                "RESTORE_PROMOTE_TARGET": "1",
                "RESTORE_EVIDENCE_PATH": str(database_evidence_path),
                "VERIFY_QUERY": (
                    "SELECT CASE WHEN COUNT(*) >= "
                    f"{config.min_idempotency_records} THEN 1 ELSE 0 END "
                    "FROM idempotency_records;"
                ),
                "VERIFY_EXPECTED": "1",
            }
        )
        temporary_database = f"{config.postgres_database}_restore_candidate"
        runner.run(
            [
                "bash",
                str(RESTORE_SCRIPT),
                str(config.backup_path),
                temporary_database,
            ],
            environment=restore_environment,
            announce_output=True,
        )
        database_evidence = json_document(
            database_evidence_path.read_bytes(),
            "Database restore evidence",
        )
        if stat.S_IMODE(database_evidence_path.stat().st_mode) & 0o077:
            raise DrillError("Database restore evidence должен иметь mode 0600.")
        validate_database_evidence(
            database_evidence,
            config,
            source,
            backup_sha256,
            backup_bytes,
        )

        runner.run(
            compose_arguments(config, "up", "--detach", "--no-deps", "recovery-api"),
            announce_output=True,
        )
        api_ready_seconds = wait_for_readiness(config, http, clock, recovery_started)

        recovery_baseline_values = parse_numeric_row(
            recovery_scalar(config, runner, RECOVERY_METRICS_SQL),
            5,
            "Recovered database baseline query",
        )
        (
            recovered_audit_before,
            recovered_idempotency_before,
            recovered_pending_before,
            recovered_dead_letters_before,
            recovered_locks_before,
        ) = recovery_baseline_values
        if recovered_idempotency_before < config.min_idempotency_records:
            raise DrillError(
                "Recovered database содержит недостаточно idempotency records: "
                f"{recovered_idempotency_before} < {config.min_idempotency_records}."
            )
        if recovered_pending_before or recovered_dead_letters_before or recovered_locks_before:
            raise DrillError(
                "Recovered baseline не чист: "
                f"pending={recovered_pending_before}, "
                f"dead letters={recovered_dead_letters_before}, "
                f"advisory locks={recovered_locks_before}."
            )

        admin_token = request_token(
            config,
            http,
            config.admin_username,
            config.admin_password,
        )
        author_token = request_token(
            config,
            http,
            config.author_username,
            config.author_password,
        )

        recovered_build = authenticated_json(
            http,
            "GET",
            f"{config.recovery_url}/api/v1/operations/version",
            admin_token,
            label="Recovered build information",
        )
        recovered_version = validate_build_information(
            recovered_build,
            config,
            "Recovered build information",
        )
        if recovered_version != live_version:
            raise DrillError(
                "Live и recovered API сообщили разные application versions: "
                f"{live_version!r} != {recovered_version!r}."
            )

        query = urllib.parse.urlencode(
            {"search": config.seed_tag, "page": 1, "pageSize": 1}
        )
        business = authenticated_json(
            http,
            "GET",
            f"{config.recovery_url}/api/v1/tests?{query}",
            admin_token,
            label="Recovered business query",
        )
        total_count = business.get("totalCount")
        if (
            isinstance(total_count, bool)
            or not isinstance(total_count, int)
            or total_count < config.min_test_count
        ):
            raise DrillError(
                "Recovered business query не подтвердил seed dataset: "
                f"totalCount={total_count!r}."
            )
        business_ready_seconds = elapsed_seconds(clock, recovery_started)

        outbox = authenticated_json(
            http,
            "GET",
            f"{config.recovery_url}/api/v1/operations/outbox?deadLetterLimit=1",
            admin_token,
            label="Recovered Outbox query",
        )
        for field in ("pendingCount", "retryScheduledCount", "deadLetterCount"):
            value = outbox.get(field)
            if isinstance(value, bool) or value != 0:
                raise DrillError(f"Recovered Outbox {field}={value!r}, ожидался integer 0.")

        author_subject = jwt_subject(author_token)
        replay_sql = f"""
SELECT json_build_object(
  'operation', "Operation",
  'requestId', "RequestId",
  'resultJson', "ResultJson"
)::text
FROM idempotency_records
WHERE "ActorId" = '{author_subject}' AND "Operation" LIKE 'tests.publish:%'
ORDER BY "CreatedAt", "Id"
LIMIT 1;
""".strip()
        replay_row = recovery_scalar(config, runner, replay_sql).strip()
        if not replay_row:
            raise DrillError("В backup нет publish idempotency record для staging author.")
        replay = json_document(replay_row.encode(), "Publish idempotency record")
        operation = replay.get("operation")
        request_id = str(replay.get("requestId", ""))
        stored_result = replay.get("resultJson")
        operation_match = PUBLISH_OPERATION.fullmatch(operation or "")
        if not operation_match or not re.fullmatch(
            r"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-"
            r"[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            request_id,
        ):
            raise DrillError("Publish idempotency record имеет некорректный operation/request ID.")
        replay_result = authenticated_json(
            http,
            "POST",
            f"{config.recovery_url}/api/v1/tests/{operation_match.group(1)}/publish",
            author_token,
            headers={
                "Idempotency-Key": request_id,
                "If-Match": '"1"',
                "Content-Type": "application/json",
            },
            data=b"{}",
            label="Idempotency replay",
        )
        try:
            expected_result = json.loads(stored_result)
        except (TypeError, json.JSONDecodeError) as error:
            raise DrillError("Stored idempotency result содержит невалидный JSON.") from error
        if not isinstance(expected_result, dict):
            raise DrillError("Stored idempotency result должен быть JSON object.")
        if published_revision_value(
            replay_result,
            "Idempotency replay",
        ) != published_revision_value(expected_result, "Stored idempotency result"):
            raise DrillError("Idempotency replay не вернул сохранённый business result.")

        recovery_values = parse_numeric_row(
            recovery_scalar(config, runner, RECOVERY_METRICS_SQL),
            5,
            "Recovered database query",
        )
        audit_count, idempotency_count, pending_count, dead_letter_count, advisory_locks = (
            recovery_values
        )
        if audit_count <= recovered_audit_before:
            raise DrillError("Business probes не создали audit entries в recovered database.")
        if idempotency_count != recovered_idempotency_before:
            raise DrillError("Idempotency replay изменил число idempotency records.")
        if pending_count or dead_letter_count or advisory_locks:
            raise DrillError(
                "Recovered state не чист: "
                f"pending={pending_count}, dead letters={dead_letter_count}, "
                f"advisory locks={advisory_locks}."
            )

        cleanup_recovery(config, runner, check=True)
        assert_recovery_removed(config, runner)
        cleaned = True
    finally:
        if recovery_created and not cleaned:
            cleanup_recovery(config, runner, check=False)

    completed_at = clock.utc_now()
    # pg_dump establishes its snapshot after process start. Start time is therefore a
    # conservative upper bound for the snapshot age at the beginning of recovery.
    observed_rpo_seconds = max(0, math.ceil(recovery_started_epoch - backup_started_epoch))
    total_rto_seconds = business_ready_seconds
    evidence: dict[str, Any] = {
        "schemaVersion": 1,
        "status": "passed",
        "scope": "staging-application-restore-verified",
        "startedAt": iso8601(recovery_started_at),
        "completedAt": iso8601(completed_at),
        "imageDigest": config.image_digest,
        "source": {**source, "apiVersion": live_version},
        "backup": {
            "startedAt": iso8601(backup_started_at),
            "completedAt": iso8601(backup_completed_at),
            "sha256": backup_sha256,
            "bytes": backup_bytes,
            "observedRpoSeconds": observed_rpo_seconds,
            "calculation": "recoveryStartedAt - backupStartedAt (conservative upper bound)",
            "targetRpoSeconds": config.rpo_target_seconds,
            "targetMet": observed_rpo_seconds <= config.rpo_target_seconds,
            "headroomSeconds": config.rpo_target_seconds - observed_rpo_seconds,
        },
        "recovery": {
            "cleanInstanceReadySeconds": clean_instance_ready_seconds,
            "databaseRestoreSeconds": database_evidence["databaseRestoreSeconds"],
            "databaseVerificationSeconds": database_evidence[
                "databaseVerificationSeconds"
            ],
            "databaseRecoverySeconds": database_evidence["databaseRecoverySeconds"],
            "apiReadySeconds": api_ready_seconds,
            "businessReadySeconds": business_ready_seconds,
            "apiVersion": recovered_version,
            "totalRtoSeconds": total_rto_seconds,
            "targetRtoSeconds": config.rto_target_seconds,
            "targetMet": total_rto_seconds <= config.rto_target_seconds,
            "headroomSeconds": config.rto_target_seconds - total_rto_seconds,
        },
        "verification": {
            "businessRoute": "GET /api/v1/tests",
            "businessMatchCount": total_count,
            "imageDigestVerified": True,
            "idempotencyReplayVerified": True,
            "idempotencyRecordCount": idempotency_count,
            "outboxPendingCount": pending_count,
            "outboxDeadLetterCount": dead_letter_count,
            "advisoryLockCount": advisory_locks,
            "auditWriteVerified": True,
            "auditEntryCountBefore": recovered_audit_before,
            "auditEntryCountAfter": audit_count,
            "cleanupVerified": cleaned,
        },
    }
    if not evidence["backup"]["targetMet"] or not evidence["recovery"]["targetMet"]:
        raise DrillError("RPO/RTO target превышен; successful evidence не публикуется.")
    return evidence


def ensure_no_secrets(document: Mapping[str, Any], config: DrillConfig) -> None:
    serialized = json.dumps(document, ensure_ascii=False, separators=(",", ":"))
    for value in (
        config.postgres_password,
        config.rabbitmq_password,
        config.client_secret,
        config.admin_password,
        config.author_password,
    ):
        if len(value) >= 4 and value in serialized:
            raise DrillError("Evidence содержит credential material.")
    lowered = serialized.lower()
    for marker in ("access_token", "refresh_token", "bearer ", "client_secret", "password"):
        if marker in lowered:
            raise DrillError(f"Evidence содержит запрещённый marker {marker!r}.")


def atomic_write_json(path: pathlib.Path, document: Mapping[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = pathlib.Path(f"{path}.tmp.{os.getpid()}")
    try:
        descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
            json.dump(document, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.chmod(temporary, 0o600)
        os.replace(temporary, path)
    finally:
        if temporary.exists():
            temporary.unlink()


def execute(
    config: DrillConfig,
    *,
    runner: CommandRunner | None = None,
    http: HttpClient | None = None,
    clock: SystemClock | None = None,
) -> dict[str, Any]:
    runner = runner or CommandRunner()
    http = http or HttpClient()
    clock = clock or SystemClock()
    with tempfile.TemporaryDirectory(prefix="testapp-staging-rto-") as directory:
        evidence = run_drill(config, runner, http, clock, pathlib.Path(directory))
    ensure_no_secrets(evidence, config)
    atomic_write_json(config.evidence_path, evidence)
    return evidence


def parser() -> argparse.ArgumentParser:
    command = argparse.ArgumentParser(
        description=(
            "Восстановить staging backup в чистый disposable instance, измерить "
            "RPO/RTO и опубликовать fail-closed JSON evidence."
        )
    )
    command.add_argument("--backup", required=True, type=pathlib.Path)
    command.add_argument("--evidence", required=True, type=pathlib.Path)
    command.add_argument(
        "--check",
        action="store_true",
        help="проверить конфигурацию без Docker/сети и завершиться",
    )
    return command


def main(arguments: Sequence[str] | None = None) -> int:
    options = parser().parse_args(arguments)
    try:
        config = DrillConfig.from_environment(
            options.backup,
            options.evidence,
            os.environ,
        )
        if options.check:
            print("OK: staging restore drill configuration is valid; Docker/network were not used.")
            return 0
        evidence = execute(config)
        print(
            "Staging restore drill succeeded: "
            f"RPO={evidence['backup']['observedRpoSeconds']}s "
            f"RTO={evidence['recovery']['totalRtoSeconds']}s "
            f"evidence={config.evidence_path}"
        )
        return 0
    except (DrillError, json.JSONDecodeError, OSError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
