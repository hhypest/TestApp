#!/usr/bin/env bash
set -euo pipefail
SECONDS=0

backup="${1:?Usage: postgresql-restore-verify.sh <backup.dump> [target_database]}"
POSTGRES_IMAGE="${POSTGRES_IMAGE:-postgres:18@sha256:06cad38a5d9f5d24b4d83d86def30795d5e4b757fedbf5281172b576dedcd941}"
# Сеть контейнера pg_dump/pg_restore. По умолчанию host — так работает CI, где PostgreSQL
# опубликован на 127.0.0.1. На staging-контуре порт наружу не публикуется намеренно, поэтому
# там передаётся сеть compose:
#   POSTGRES_DOCKER_NETWORK=testapp-staging_default POSTGRES_HOST=postgres
POSTGRES_DOCKER_NETWORK="${POSTGRES_DOCKER_NETWORK:-host}"
POSTGRES_HOST="${POSTGRES_HOST:-127.0.0.1}"
POSTGRES_PORT="${POSTGRES_PORT:-5432}"
POSTGRES_SOURCE_DATABASE="${POSTGRES_SOURCE_DATABASE:-testapp}"
POSTGRES_ADMIN_DATABASE="${POSTGRES_ADMIN_DATABASE:-postgres}"
POSTGRES_ADMIN_USER="${POSTGRES_ADMIN_USER:-testapp}"
POSTGRES_ADMIN_PASSWORD="${POSTGRES_ADMIN_PASSWORD:?POSTGRES_ADMIN_PASSWORD is required}"
target="${2:-${POSTGRES_SOURCE_DATABASE}_restore_verify}"
VERIFY_QUERY="${VERIFY_QUERY:-}"
VERIFY_EXPECTED="${VERIFY_EXPECTED:-}"
RESTORE_EVIDENCE_PATH="${RESTORE_EVIDENCE_PATH:-}"
evidence_tmp=""

for name in "$POSTGRES_SOURCE_DATABASE" "$POSTGRES_ADMIN_DATABASE" "$target"; do
  if [[ ! "$name" =~ ^[A-Za-z0-9_]+$ ]]; then
    echo "Database name '$name' contains unsupported characters." >&2
    exit 2
  fi
done

if [[ "$target" == "$POSTGRES_SOURCE_DATABASE" || "$target" == "$POSTGRES_ADMIN_DATABASE" ]]; then
  echo "Refusing to restore verification backup over database '$target'." >&2
  exit 2
fi
if [[ ! -f "$backup" ]]; then
  echo "Backup file does not exist: $backup" >&2
  exit 2
fi
if [[ -n "$RESTORE_EVIDENCE_PATH" ]]; then
  backup_path="$(readlink -m -- "$backup")"
  checksum_path="$(readlink -m -- "${backup}.sha256")"
  evidence_path="$(readlink -m -- "$RESTORE_EVIDENCE_PATH")"
  if [[ "$evidence_path" == "$backup_path" || "$evidence_path" == "$checksum_path" ]]; then
    echo "Restore evidence path must differ from backup and checksum paths." >&2
    exit 2
  fi
fi
if [[ -f "${backup}.sha256" ]]; then
  sha256sum --check "${backup}.sha256"
fi
docker run --rm -i "$POSTGRES_IMAGE" pg_restore --list < "$backup" >/dev/null

backup_sha256="$(sha256sum "$backup" | awk '{print $1}')"
backup_bytes="$(wc -c < "$backup" | tr -d '[:space:]')"
started_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
recovery_started_seconds="$SECONDS"

postgres_exec() {
  local database="$1"
  local sql="$2"
  docker run --rm --network "$POSTGRES_DOCKER_NETWORK" \
    -e PGPASSWORD="$POSTGRES_ADMIN_PASSWORD" \
    "$POSTGRES_IMAGE" \
    psql \
      --host="$POSTGRES_HOST" \
      --port="$POSTGRES_PORT" \
      --username="$POSTGRES_ADMIN_USER" \
      --dbname="$database" \
      --no-align \
      --tuples-only \
      --quiet \
      --set=ON_ERROR_STOP=1 \
      --command="$sql"
}

drop_target() {
  postgres_exec "$POSTGRES_ADMIN_DATABASE" \
    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$target' AND pid <> pg_backend_pid();" \
    >/dev/null
  postgres_exec "$POSTGRES_ADMIN_DATABASE" "DROP DATABASE IF EXISTS \"$target\";" >/dev/null
}

cleanup() {
  if [[ -n "$evidence_tmp" && -f "$evidence_tmp" ]]; then
    rm -f -- "$evidence_tmp"
  fi
  drop_target >/dev/null 2>&1 || true
}
trap cleanup EXIT

drop_target
postgres_exec "$POSTGRES_ADMIN_DATABASE" "CREATE DATABASE \"$target\" TEMPLATE template0;" >/dev/null

echo "Restoring '$backup' into disposable database '$target'..."
restore_started_seconds="$SECONDS"
docker run --rm -i --network "$POSTGRES_DOCKER_NETWORK" \
  -e PGPASSWORD="$POSTGRES_ADMIN_PASSWORD" \
  "$POSTGRES_IMAGE" \
  pg_restore \
    --host="$POSTGRES_HOST" \
    --port="$POSTGRES_PORT" \
    --username="$POSTGRES_ADMIN_USER" \
    --dbname="$target" \
    --no-owner \
    --no-privileges \
    --exit-on-error \
  < "$backup"
restore_finished_seconds="$SECONDS"

table_count_query="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE';"
source_tables="$(postgres_exec "$POSTGRES_SOURCE_DATABASE" "$table_count_query")"
target_tables="$(postgres_exec "$target" "$table_count_query")"
if [[ "$source_tables" != "$target_tables" ]]; then
  echo "Restore verification failed: source tables=$source_tables target tables=$target_tables" >&2
  exit 1
fi

source_migrations="$(postgres_exec "$POSTGRES_SOURCE_DATABASE" 'SELECT COUNT(*) FROM "__EFMigrationsHistory";')"
target_migrations="$(postgres_exec "$target" 'SELECT COUNT(*) FROM "__EFMigrationsHistory";')"
if [[ "$source_migrations" != "$target_migrations" || "$target_migrations" == "0" ]]; then
  echo "Restore verification failed: source migrations=$source_migrations target migrations=$target_migrations" >&2
  exit 1
fi

if [[ -n "$VERIFY_QUERY" ]]; then
  actual="$(postgres_exec "$target" "$VERIFY_QUERY")"
  if [[ "$actual" != "$VERIFY_EXPECTED" ]]; then
    echo "Restore verification query failed: expected '$VERIFY_EXPECTED', got '$actual'." >&2
    exit 1
  fi
fi

verification_finished_seconds="$SECONDS"
completed_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
database_restore_seconds="$((restore_finished_seconds - restore_started_seconds))"
database_verification_seconds="$((verification_finished_seconds - restore_finished_seconds))"
database_recovery_seconds="$((verification_finished_seconds - recovery_started_seconds))"
custom_verification_executed=false
if [[ -n "$VERIFY_QUERY" ]]; then
  custom_verification_executed=true
fi

for value in "$backup_bytes" "$target_tables" "$target_migrations" \
             "$database_restore_seconds" "$database_verification_seconds" "$database_recovery_seconds"; do
  if [[ ! "$value" =~ ^[0-9]+$ ]]; then
    echo "Restore verification produced a non-numeric evidence value." >&2
    exit 1
  fi
done

# Успешное evidence нельзя публиковать, пока одноразовая база не удалена. Иначе зелёный
# drill оставляет рабочую копию production-shaped данных и одновременно сообщает, что
# процедура завершена. EXIT trap остаётся страховкой для любого более раннего отказа.
drop_target

if [[ -n "$RESTORE_EVIDENCE_PATH" ]]; then
  mkdir -p -- "$(dirname -- "$RESTORE_EVIDENCE_PATH")"
  evidence_tmp="${RESTORE_EVIDENCE_PATH}.tmp.$$"
  (
    umask 077
    printf '%s\n' \
      '{' \
      '  "schemaVersion": 1,' \
      '  "status": "passed",' \
      '  "scope": "database-restore-verified",' \
      "  \"startedAt\": \"$started_at\"," \
      "  \"completedAt\": \"$completed_at\"," \
      "  \"sourceDatabase\": \"$POSTGRES_SOURCE_DATABASE\"," \
      "  \"targetDatabase\": \"$target\"," \
      "  \"backupSha256\": \"$backup_sha256\"," \
      "  \"backupBytes\": $backup_bytes," \
      "  \"databaseRestoreSeconds\": $database_restore_seconds," \
      "  \"databaseVerificationSeconds\": $database_verification_seconds," \
      "  \"databaseRecoverySeconds\": $database_recovery_seconds," \
      "  \"tableCount\": $target_tables," \
      "  \"migrationCount\": $target_migrations," \
      "  \"customVerificationExecuted\": $custom_verification_executed" \
      '}' \
      > "$evidence_tmp"
  )
  chmod 0600 "$evidence_tmp"
  mv -- "$evidence_tmp" "$RESTORE_EVIDENCE_PATH"
  evidence_tmp=""
  echo "Restore evidence: $RESTORE_EVIDENCE_PATH"
fi

trap - EXIT
echo "Restore verification succeeded: tables=$target_tables migrations=$target_migrations database=$target"
