#!/usr/bin/env bash
set -euo pipefail

backup="${1:?Usage: postgresql-restore-verify.sh <backup.dump> [target_database]}"
POSTGRES_IMAGE="${POSTGRES_IMAGE:-postgres:18}"
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
if [[ -f "${backup}.sha256" ]]; then
  sha256sum --check "${backup}.sha256"
fi
docker run --rm -i "$POSTGRES_IMAGE" pg_restore --list < "$backup" >/dev/null

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

cleanup() {
  postgres_exec "$POSTGRES_ADMIN_DATABASE" \
    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$target' AND pid <> pg_backend_pid();" \
    >/dev/null || true
  postgres_exec "$POSTGRES_ADMIN_DATABASE" "DROP DATABASE IF EXISTS \"$target\";" >/dev/null || true
}
trap cleanup EXIT

cleanup
postgres_exec "$POSTGRES_ADMIN_DATABASE" "CREATE DATABASE \"$target\" TEMPLATE template0;" >/dev/null

echo "Restoring '$backup' into disposable database '$target'..."
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

echo "Restore verification succeeded: tables=$target_tables migrations=$target_migrations database=$target"
