#!/usr/bin/env bash
set -euo pipefail
umask 077

POSTGRES_IMAGE="${POSTGRES_IMAGE:-postgres:18@sha256:06cad38a5d9f5d24b4d83d86def30795d5e4b757fedbf5281172b576dedcd941}"
# Сеть контейнера pg_dump/pg_restore. По умолчанию host — так работает CI, где PostgreSQL
# опубликован на 127.0.0.1. На staging-контуре порт наружу не публикуется намеренно, поэтому
# там передаётся сеть compose:
#   POSTGRES_DOCKER_NETWORK=testapp-staging_default POSTGRES_HOST=postgres
POSTGRES_DOCKER_NETWORK="${POSTGRES_DOCKER_NETWORK:-host}"
POSTGRES_HOST="${POSTGRES_HOST:-127.0.0.1}"
POSTGRES_PORT="${POSTGRES_PORT:-5432}"
POSTGRES_DATABASE="${POSTGRES_DATABASE:-testapp}"
POSTGRES_USER="${POSTGRES_USER:-testapp}"
POSTGRES_PASSWORD="${POSTGRES_PASSWORD:?POSTGRES_PASSWORD is required}"
BACKUP_DIR="${BACKUP_DIR:-backups}"

if [[ ! "$POSTGRES_DATABASE" =~ ^[A-Za-z0-9_]+$ ]]; then
  echo "POSTGRES_DATABASE contains unsupported characters." >&2
  exit 2
fi

output="${1:-$BACKUP_DIR/${POSTGRES_DATABASE}-$(date -u +%Y%m%dT%H%M%SZ).dump}"
mkdir -p -- "$(dirname -- "$output")"
tmp="${output}.tmp"
checksum="${output}.sha256"
checksum_tmp="${checksum}.tmp"
if [[ -e "$tmp" || -L "$tmp" || -e "$checksum_tmp" || -L "$checksum_tmp" ]]; then
  echo "Refusing to reuse an existing backup temporary path." >&2
  exit 2
fi
trap 'rm -f -- "$tmp" "$checksum_tmp"' EXIT

echo "Creating logical backup for database '$POSTGRES_DATABASE'..."
docker run --rm --network "$POSTGRES_DOCKER_NETWORK" \
  -e PGPASSWORD="$POSTGRES_PASSWORD" \
  "$POSTGRES_IMAGE" \
  pg_dump \
    --host="$POSTGRES_HOST" \
    --port="$POSTGRES_PORT" \
    --username="$POSTGRES_USER" \
    --dbname="$POSTGRES_DATABASE" \
    --format=custom \
    --compress=9 \
    --no-owner \
    --no-privileges \
    --serializable-deferrable \
  > "$tmp"

docker run --rm -i "$POSTGRES_IMAGE" pg_restore --list < "$tmp" >/dev/null
mv "$tmp" "$output"

sha256sum "$output" > "$checksum_tmp"
mv "$checksum_tmp" "$checksum"
trap - EXIT
echo "Backup created: $output"
echo "Checksum: $checksum"
