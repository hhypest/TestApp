#!/usr/bin/env bash
set -euo pipefail

MARIADB_IMAGE="${MARIADB_IMAGE:-mariadb:12.3}"
MARIADB_HOST="${MARIADB_HOST:-127.0.0.1}"
MARIADB_PORT="${MARIADB_PORT:-3306}"
MARIADB_DATABASE="${MARIADB_DATABASE:-testapp}"
MARIADB_USER="${MARIADB_USER:-root}"
MARIADB_PASSWORD="${MARIADB_PASSWORD:?MARIADB_PASSWORD is required}"
BACKUP_DIR="${BACKUP_DIR:-backups}"

if [[ ! "$MARIADB_DATABASE" =~ ^[A-Za-z0-9_]+$ ]]; then
  echo "MARIADB_DATABASE contains unsupported characters." >&2
  exit 2
fi

mkdir -p "$BACKUP_DIR"
output="${1:-$BACKUP_DIR/${MARIADB_DATABASE}-$(date -u +%Y%m%dT%H%M%SZ).sql.gz}"
tmp="${output}.tmp"
trap 'rm -f "$tmp"' EXIT

echo "Creating logical backup for database '$MARIADB_DATABASE'..."
docker run --rm --network host \
  -e MARIADB_PWD="$MARIADB_PASSWORD" \
  "$MARIADB_IMAGE" \
  mariadb-dump \
    --host="$MARIADB_HOST" \
    --port="$MARIADB_PORT" \
    --user="$MARIADB_USER" \
    --single-transaction \
    --quick \
    --routines \
    --events \
    --triggers \
    --hex-blob \
    --default-character-set=utf8mb4 \
    --skip-comments \
    "$MARIADB_DATABASE" \
  | gzip -9 > "$tmp"

gzip -t "$tmp"
mv "$tmp" "$output"
trap - EXIT

sha256sum "$output" > "${output}.sha256"
echo "Backup created: $output"
echo "Checksum: ${output}.sha256"
