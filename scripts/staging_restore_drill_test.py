#!/usr/bin/env python3
"""Contract tests for the fail-closed staging restore drill."""

from __future__ import annotations

import base64
import datetime as dt
import hashlib
import json
import os
import pathlib
import stat
import tempfile
import unittest
import urllib.parse
from typing import Mapping, Sequence

import staging_restore_drill as drill


IMAGE_HASH = "a" * 64
PUBLISH_TEST_ID = "b" * 32
REQUEST_ID = "11111111-2222-4333-8444-555555555555"
REVISION_ID = "66666666-7777-4888-8999-aaaaaaaaaaaa"


def environment() -> dict[str, str]:
    return {
        "TESTAPP_PUBLIC_HOST": "staging.example.test",
        "TESTAPP_IMAGE": f"ghcr.io/example/testapp@sha256:{IMAGE_HASH}",
        "TESTAPP_IMAGE_DIGEST": f"sha256:{IMAGE_HASH}",
        "POSTGRES_DB": "testapp",
        "POSTGRES_USER": "testapp",
        "POSTGRES_PASSWORD": "postgres-secret-value",
        "RABBITMQ_USER": "testapp",
        "RABBITMQ_PASSWORD": "rabbit-secret-value",
        "TESTAPP_KEYCLOAK_CLIENT_SECRET": "client-secret-value",
        "ADMIN_USERNAME": "staging-admin",
        "ADMIN_PASSWORD": "admin-secret-value",
        "AUTHOR_USERNAME": "staging-author",
        "AUTHOR_PASSWORD": "author-secret-value",
        "RTO_SEED_TAG": "rto-seed-v1",
        "RTO_MIN_DATABASE_BYTES": "1000000",
        "RTO_MIN_TEST_COUNT": "100",
        "RTO_MIN_IDEMPOTENCY_RECORDS": "50",
        "CONFIRM_STAGING_RESTORE_DRILL": "isolated-recovery",
        "RTO_PROJECT_NAME": "testapp-rto-contract-test",
    }


class FakeClock:
    def __init__(self) -> None:
        self.seconds = 0.0
        self.epoch = 1_787_270_400.0
        self.origin = dt.datetime(2026, 8, 21, tzinfo=dt.timezone.utc)

    def advance(self, seconds: float) -> None:
        self.seconds += seconds

    def monotonic(self) -> float:
        return self.seconds

    def timestamp(self) -> float:
        return self.epoch + self.seconds

    def sleep(self, seconds: float) -> None:
        self.advance(seconds)

    def utc_now(self) -> dt.datetime:
        return self.origin + dt.timedelta(seconds=self.seconds)


class FakeRunner:
    def __init__(
        self,
        clock: FakeClock,
        evidence_path: pathlib.Path,
        *,
        source_metrics: str = "13|4|50000000|200|60|1000|0|0|0\n",
        fail_cleanup: bool = False,
        fail_recovery_up: bool = False,
    ) -> None:
        self.clock = clock
        self.evidence_path = evidence_path
        self.source_metrics = source_metrics
        self.fail_cleanup = fail_cleanup
        self.fail_recovery_up = fail_recovery_up
        self.events: list[str] = []
        self.evidence_existed_at_cleanup: list[bool] = []
        self.recovery_metric_queries = 0
        self.cleanup_calls = 0

    def run(
        self,
        arguments: Sequence[str],
        *,
        environment: Mapping[str, str] | None = None,
        input_bytes: bytes | None = None,
        check: bool = True,
        announce_output: bool = False,
    ) -> str:
        del input_bytes, announce_output
        command = list(arguments)

        if command[:3] == ["docker", "network", "inspect"]:
            self.events.append("network-inspect")
            return "[]\n"

        if command[:3] in (
            ["docker", "container", "ls"],
            ["docker", "volume", "ls"],
            ["docker", "network", "ls"],
        ):
            self.events.append("recovery-removal-check")
            return ""

        if command[:2] == ["docker", "run"]:
            self.events.append("source-metrics")
            return self.source_metrics

        if command[0] == "bash" and pathlib.Path(command[1]) == drill.BACKUP_SCRIPT:
            self.events.append("backup")
            backup_path = pathlib.Path(command[2])
            backup = b"representative-staging-database-backup"
            backup_path.write_bytes(backup)
            os.chmod(backup_path, 0o600)
            digest = hashlib.sha256(backup).hexdigest()
            checksum_path = pathlib.Path(f"{backup_path}.sha256")
            checksum_path.write_text(f"{digest}  {backup_path}\n", encoding="utf-8")
            os.chmod(checksum_path, 0o600)
            self.clock.advance(5)
            return "Backup created\n"

        if command[0] == "bash" and pathlib.Path(command[1]) == drill.RESTORE_SCRIPT:
            self.events.append("restore")
            assert environment is not None
            backup_path = pathlib.Path(command[2])
            digest = hashlib.sha256(backup_path.read_bytes()).hexdigest()
            database_evidence = {
                "schemaVersion": 2,
                "status": "passed",
                "scope": "database-restore-promoted",
                "sourceDatabase": environment["POSTGRES_SOURCE_DATABASE"],
                "temporaryDatabase": command[3],
                "restoredDatabase": environment["POSTGRES_SOURCE_DATABASE"],
                "targetRetained": True,
                "backupSha256": digest,
                "backupBytes": backup_path.stat().st_size,
                "databaseRestoreSeconds": 5,
                "databaseVerificationSeconds": 2,
                "databaseRecoverySeconds": 7,
                "tableCount": int(environment["RESTORE_EXPECTED_TABLE_COUNT"]),
                "migrationCount": int(environment["RESTORE_EXPECTED_MIGRATION_COUNT"]),
                "customVerificationExecuted": True,
            }
            database_evidence_path = pathlib.Path(environment["RESTORE_EVIDENCE_PATH"])
            database_evidence_path.write_text(
                json.dumps(database_evidence),
                encoding="utf-8",
            )
            os.chmod(database_evidence_path, 0o600)
            self.clock.advance(7)
            return "Restore verification succeeded\n"

        if command[:2] == ["docker", "compose"]:
            if "down" in command:
                self.events.append("cleanup")
                self.cleanup_calls += 1
                self.evidence_existed_at_cleanup.append(self.evidence_path.exists())
                self.clock.advance(1)
                if self.fail_cleanup and self.cleanup_calls > 1 and check:
                    raise drill.DrillError("synthetic cleanup failure")
                return ""
            if "up" in command and "recovery-postgres" in command:
                self.events.append("recovery-infrastructure-up")
                self.clock.advance(3)
                if self.fail_recovery_up:
                    raise drill.DrillError("synthetic partial compose failure")
                return ""
            if "up" in command and "recovery-api" in command:
                self.events.append("recovery-api-up")
                self.clock.advance(2)
                return ""
            if "exec" in command:
                sql = next(value[10:] for value in command if value.startswith("--command="))
                if "json_build_object" in sql:
                    self.events.append("idempotency-record-query")
                    return json.dumps(
                        {
                            "operation": f"tests.publish:{PUBLISH_TEST_ID}",
                            "requestId": REQUEST_ID,
                            # Persistence использует JsonSerializerOptions.Default, HTTP —
                            # web defaults. Реальный контракт различается только casing.
                            "resultJson": json.dumps({"Value": REVISION_ID}),
                        }
                    )
                if "concat_ws" in sql:
                    self.events.append("recovery-metrics")
                    self.recovery_metric_queries += 1
                    if self.recovery_metric_queries == 1:
                        return "1000|60|0|0|0\n"
                    return "1003|60|0|0|0\n"

        raise AssertionError(f"Unexpected command: {command!r}")


class FakeHttp:
    def __init__(
        self,
        config: drill.DrillConfig,
        clock: FakeClock,
        *,
        business_count: int = 150,
        reported_digest: str | None = None,
    ) -> None:
        self.config = config
        self.clock = clock
        self.business_count = business_count
        self.reported_digest = reported_digest or config.image_digest
        self.events: list[str] = []

    @staticmethod
    def token(subject: str) -> str:
        payload = base64.urlsafe_b64encode(
            json.dumps({"sub": subject}, separators=(",", ":")).encode()
        ).decode().rstrip("=")
        return f"header.{payload}.signature"

    def request(
        self,
        method: str,
        url: str,
        *,
        headers: Mapping[str, str] | None = None,
        data: bytes | None = None,
        timeout: int = 30,
    ) -> tuple[int, bytes]:
        del headers, timeout
        if method == "GET" and url == f"https://{self.config.public_host}/health/ready":
            self.events.append("live-readiness")
            return 200, b"{}"
        if method == "GET" and url == f"{self.config.recovery_url}/health/ready":
            self.events.append("recovery-readiness")
            self.clock.advance(4)
            return 200, b"{}"
        if method == "GET" and url.endswith("/api/v1/operations/version"):
            self.events.append("build-information")
            return 200, json.dumps(
                {"version": "1.0.0-rc.1", "imageDigest": self.reported_digest}
            ).encode()
        if method == "POST" and url.endswith("/protocol/openid-connect/token"):
            form = urllib.parse.parse_qs((data or b"").decode())
            username = form.get("username", [""])[0]
            subject = "author-subject" if username == self.config.author_username else "admin-subject"
            self.events.append(f"token:{username}")
            return 200, json.dumps({"access_token": self.token(subject)}).encode()
        if method == "GET" and "/api/v1/tests?" in url:
            self.events.append("business-query")
            self.clock.advance(2)
            return 200, json.dumps({"items": [], "totalCount": self.business_count}).encode()
        if method == "GET" and "/api/v1/operations/outbox?" in url:
            self.events.append("outbox-query")
            return 200, json.dumps(
                {"pendingCount": 0, "retryScheduledCount": 0, "deadLetterCount": 0}
            ).encode()
        if method == "POST" and url.endswith(f"/api/v1/tests/{PUBLISH_TEST_ID}/publish"):
            self.events.append("idempotency-replay")
            return 200, json.dumps({"value": REVISION_ID}).encode()
        raise AssertionError(f"Unexpected HTTP request: {method} {url}")


class StagingRestoreDrillTests(unittest.TestCase):
    def config(self, directory: pathlib.Path, env: Mapping[str, str] | None = None) -> drill.DrillConfig:
        return drill.DrillConfig.from_environment(
            directory / "staging.dump",
            directory / "staging-rto.json",
            dict(env or environment()),
        )

    def test_success_publishes_private_evidence_only_after_cleanup(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = pathlib.Path(temporary)
            config = self.config(directory)
            clock = FakeClock()
            runner = FakeRunner(clock, config.evidence_path)
            http = FakeHttp(config, clock)

            evidence = drill.execute(config, runner=runner, http=http, clock=clock)

            persisted = json.loads(config.evidence_path.read_text(encoding="utf-8"))
            self.assertEqual(evidence, persisted)
            self.assertEqual(stat.S_IMODE(config.evidence_path.stat().st_mode), 0o600)
            self.assertEqual(evidence["scope"], "staging-application-restore-verified")
            self.assertEqual(evidence["backup"]["observedRpoSeconds"], 5)
            self.assertEqual(evidence["recovery"]["totalRtoSeconds"], 18)
            self.assertTrue(evidence["recovery"]["targetMet"])
            self.assertTrue(evidence["verification"]["cleanupVerified"])
            self.assertEqual(runner.evidence_existed_at_cleanup, [False, False])
            serialized = json.dumps(evidence)
            for secret_name in (
                "POSTGRES_PASSWORD",
                "RABBITMQ_PASSWORD",
                "TESTAPP_KEYCLOAK_CLIENT_SECRET",
                "ADMIN_PASSWORD",
                "AUTHOR_PASSWORD",
            ):
                self.assertNotIn(environment()[secret_name], serialized)

    def test_business_failure_cleans_up_and_preserves_previous_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = pathlib.Path(temporary)
            config = self.config(directory)
            config.evidence_path.write_text("previous-success\n", encoding="utf-8")
            clock = FakeClock()
            runner = FakeRunner(clock, config.evidence_path)

            with self.assertRaisesRegex(drill.DrillError, "seed dataset"):
                drill.execute(
                    config,
                    runner=runner,
                    http=FakeHttp(config, clock, business_count=99),
                    clock=clock,
                )

            self.assertEqual(
                config.evidence_path.read_text(encoding="utf-8"),
                "previous-success\n",
            )
            self.assertIn("cleanup", runner.events)

    def test_small_source_dataset_fails_before_backup_or_recovery(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = pathlib.Path(temporary)
            config = self.config(directory)
            clock = FakeClock()
            runner = FakeRunner(
                clock,
                config.evidence_path,
                source_metrics="13|4|50000000|99|60|1000|0|0|0\n",
            )

            with self.assertRaisesRegex(drill.DrillError, "Недостаточно тестов"):
                drill.execute(config, runner=runner, http=FakeHttp(config, clock), clock=clock)

            self.assertNotIn("backup", runner.events)
            self.assertNotIn("recovery-infrastructure-up", runner.events)
            self.assertFalse(config.evidence_path.exists())

    def test_live_digest_mismatch_fails_before_backup(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = pathlib.Path(temporary)
            config = self.config(directory)
            clock = FakeClock()
            runner = FakeRunner(clock, config.evidence_path)

            with self.assertRaisesRegex(drill.DrillError, "ожидаемого image digest"):
                drill.execute(
                    config,
                    runner=runner,
                    http=FakeHttp(config, clock, reported_digest=f"sha256:{'f' * 64}"),
                    clock=clock,
                )

            self.assertNotIn("backup", runner.events)
            self.assertFalse(config.evidence_path.exists())

    def test_cleanup_failure_is_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = pathlib.Path(temporary)
            config = self.config(directory)
            config.evidence_path.write_text("previous-success\n", encoding="utf-8")
            clock = FakeClock()
            runner = FakeRunner(clock, config.evidence_path, fail_cleanup=True)

            with self.assertRaisesRegex(drill.DrillError, "cleanup failure"):
                drill.execute(config, runner=runner, http=FakeHttp(config, clock), clock=clock)

            self.assertEqual(runner.events.count("cleanup"), 3)
            self.assertEqual(
                config.evidence_path.read_text(encoding="utf-8"),
                "previous-success\n",
            )

    def test_partial_compose_failure_still_cleans_resources(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = pathlib.Path(temporary)
            config = self.config(directory)
            clock = FakeClock()
            runner = FakeRunner(clock, config.evidence_path, fail_recovery_up=True)

            with self.assertRaisesRegex(drill.DrillError, "partial compose failure"):
                drill.execute(config, runner=runner, http=FakeHttp(config, clock), clock=clock)

            self.assertEqual(runner.events[-1], "cleanup")
            self.assertFalse(config.evidence_path.exists())

    def test_missed_rpo_target_preserves_previous_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = pathlib.Path(temporary)
            env = environment()
            env["RPO_TARGET_SECONDS"] = "4"
            config = self.config(directory, env)
            config.evidence_path.write_text("previous-success\n", encoding="utf-8")
            clock = FakeClock()
            runner = FakeRunner(clock, config.evidence_path)

            with self.assertRaisesRegex(drill.DrillError, "RPO/RTO target"):
                drill.execute(config, runner=runner, http=FakeHttp(config, clock), clock=clock)

            self.assertEqual(runner.events.count("cleanup"), 2)
            self.assertEqual(
                config.evidence_path.read_text(encoding="utf-8"),
                "previous-success\n",
            )

    def test_configuration_rejects_mutable_or_inconsistent_paths_and_images(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = pathlib.Path(temporary)
            cases: list[tuple[str, dict[str, str], pathlib.Path]] = []

            mutable = environment()
            mutable["TESTAPP_IMAGE"] = "ghcr.io/example/testapp:latest"
            cases.append(("immutable", mutable, directory / "mutable.json"))

            mismatch = environment()
            mismatch["TESTAPP_IMAGE_DIGEST"] = f"sha256:{'c' * 64}"
            cases.append(("не совпадает", mismatch, directory / "mismatch.json"))

            unsafe_project = environment()
            unsafe_project["RTO_PROJECT_NAME"] = "production"
            cases.append(("testapp-rto-", unsafe_project, directory / "project.json"))

            premature_timeout = environment()
            premature_timeout["RTO_TARGET_SECONDS"] = "20"
            premature_timeout["RTO_TIMEOUT_SECONDS"] = "10"
            cases.append(("не может быть меньше", premature_timeout, directory / "timeout.json"))

            cases.append(("должен отличаться", environment(), directory / "backup.dump"))

            for expected, env, evidence_path in cases:
                with self.subTest(expected=expected):
                    with self.assertRaisesRegex(drill.DrillError, expected):
                        drill.DrillConfig.from_environment(
                            directory / "backup.dump",
                            evidence_path,
                            env,
                        )


if __name__ == "__main__":
    unittest.main()
