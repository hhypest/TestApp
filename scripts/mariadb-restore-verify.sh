#!/usr/bin/env bash
set -euo pipefail

backup="${1:?Usage: mariadb-restore-verify.sh <backup.sql.gz> [target_database]}"
MARIADB_IMAGE="${MARIADB_IMAGE:-mariadb:12.3}"
MARIADB_HOST="${MARIADB_HOST:-127.0.0.1}"
MARIADB_PORT="${MARIADB_PORT:-3306}"
MARIADB_SOURCE_DATABASE="${MARIADB_SOURCE_DATABASE:-testapp}"
MARIADB_ADMIN_USER="${MARIADB_ADMIN_USER:-root}"
MARIADB_ADMIN_PASSWORD="${MARIADB_ADMIN_PASSWORD:?MARIADB_ADMIN_PASSWORD is required}"
target="${2:-${MARIADB_SOURCE_DATABASE}_restore_verify}"
VERIFY_QUERY="${VERIFY_QUERY:-}"
VERIFY_EXPECTED="${VERIFY_EXPECTED:-}"

for name in "$MARIADB_SOURCE_DATABASE" "$target"; do
  if [[ ! "$name" =~ ^[A-Za-z0-9_]+$ ]]; then
    echo "Database name '$name' contains unsupported characters." >&2
    exit 2
  fi
done

if [[ "$target" == "$MARIADB_SOURCE_DATABASE" ]]; then
  echo "Refusing to restore verification backup over source database '$MARIADB_SOURCE_DATABASE'." >&2
  exit 2
fi
if [[ ! -f "$backup" ]]; then
  echo "Backup file does not exist: $backup" >&2
  exit 2
fi
if [[ -f "${backup}.sha256" ]]; then
  sha256sum --check "${backup}.sha256"
fi
gzip -t "$backup"

mysql_exec() {
  local database="$1"
  local sql="$2"
  local args=(
    --rm --network host
    -e "MARIADB_PWD=$MARIADB_ADMIN_PASSWORD"
    "$MARIADB_IMAGE"
    mariadb
    --host="$MARIADB_HOST"
    --port="$MARIADB_PORT"
    --user="$MARIADB_ADMIN_USER"
    --batch --skip-column-names
  )
  if [[ -n "$database" ]]; then
    args+=(--database="$database")
  fi
  docker run "${args[@]}" --execute="$sql"
}

cleanup() {
  mysql_exec "" "DROP DATABASE IF EXISTS \`$target\`;" >/dev/null || true
}
trap cleanup EXIT

mysql_exec "" "DROP DATABASE IF EXISTS \`$target\`; CREATE DATABASE \`$target\` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;" >/dev/null

echo "Restoring '$backup' into disposable database '$target'..."
gunzip -c "$backup" | docker run --rm -i --network host \
  -e MARIADB_PWD="$MARIADB_ADMIN_PASSWORD" \
  "$MARIADB_IMAGE" \
  mariadb \
    --host="$MARIADB_HOST" \
    --port="$MARIADB_PORT" \
    --user="$MARIADB_ADMIN_USER" \
    --database="$target"

source_tables="$(mysql_exec "$MARIADB_SOURCE_DATABASE" "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = '$MARIADB_SOURCE_DATABASE' AND table_type = 'BASE TABLE';")"
target_tables="$(mysql_exec "$target" "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = '$target' AND table_type = 'BASE TABLE';")"
if [[ "$source_tables" != "$target_tables" ]]; then
  echo "Restore verification failed: source tables=$source_tables target tables=$target_tables" >&2
  exit 1
fi

source_migrations="$(mysql_exec "$MARIADB_SOURCE_DATABASE" 'SELECT COUNT(*) FROM `__EFMigrationsHistory`;')"
target_migrations="$(mysql_exec "$target" 'SELECT COUNT(*) FROM `__EFMigrationsHistory`;')"
if [[ "$source_migrations" != "$target_migrations" || "$target_migrations" == "0" ]]; then
  echo "Restore verification failed: source migrations=$source_migrations target migrations=$target_migrations" >&2
  exit 1
fi

if [[ -n "$VERIFY_QUERY" ]]; then
  actual="$(mysql_exec "$target" "$VERIFY_QUERY")"
  if [[ "$actual" != "$VERIFY_EXPECTED" ]]; then
    echo "Restore verification query failed: expected '$VERIFY_EXPECTED', got '$actual'." >&2
    exit 1
  fi
fi

echo "Restore verification succeeded: tables=$target_tables migrations=$target_migrations database=$target"
