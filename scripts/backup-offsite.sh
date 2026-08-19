#!/usr/bin/env bash
# Снимает дамп и увозит его с машины в зашифрованном виде (внутренний пилот, issue #16).
#
# Локальный том рядом с базой не переживёт отказ диска, поэтому копия обязана уехать.
# Скрипт намеренно не знает, куда именно: назначение задаётся командой в BACKUP_UPLOAD_CMD,
# что позволяет использовать rclone, aws s3, scp или что угодно ещё без правки скрипта.
#
# Пример (S3-совместимое хранилище через rclone):
#   BACKUP_UPLOAD_CMD='rclone copyto {} remote:testapp-backups/{}' \
#   BACKUP_AGE_RECIPIENT=age1... \
#   POSTGRES_DOCKER_NETWORK=testapp-staging_default POSTGRES_HOST=postgres \
#   POSTGRES_PASSWORD=... bash scripts/backup-offsite.sh
#
# Плейсхолдер {} в BACKUP_UPLOAD_CMD заменяется на путь к зашифрованному файлу.
set -euo pipefail

BACKUP_DIR="${BACKUP_DIR:-/var/backups/testapp}"
BACKUP_KEEP_LOCAL="${BACKUP_KEEP_LOCAL:-7}"
BACKUP_AGE_RECIPIENT="${BACKUP_AGE_RECIPIENT:?нужен получатель age для шифрования: age1...}"
BACKUP_UPLOAD_CMD="${BACKUP_UPLOAD_CMD:?нужна команда выгрузки, {} заменяется на путь к файлу}"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mkdir -p "$BACKUP_DIR"

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
dump="$BACKUP_DIR/testapp-$stamp.dump"
encrypted="$dump.age"

echo "[1/4] Снимаю дамп..."
BACKUP_DIR="$BACKUP_DIR" bash "$script_dir/postgresql-backup.sh" "$dump"

# Шифруем ДО выгрузки: незашифрованная копия не должна покидать машину даже на секунду.
echo "[2/4] Шифрую для $BACKUP_AGE_RECIPIENT..."
age --recipient "$BACKUP_AGE_RECIPIENT" --output "$encrypted" "$dump"
test -s "$encrypted"

echo "[3/4] Выгружаю..."
upload_cmd="${BACKUP_UPLOAD_CMD//\{\}/$encrypted}"
# shellcheck disable=SC2086
eval $upload_cmd

# Незашифрованный дамп удаляем сразу; зашифрованные держим BACKUP_KEEP_LOCAL штук,
# чтобы восстановление не зависело от доступности внешнего хранилища.
rm -f "$dump"

echo "[4/4] Чищу локальные копии старше $BACKUP_KEEP_LOCAL последних..."
ls -1t "$BACKUP_DIR"/testapp-*.dump.age 2>/dev/null | tail -n +$((BACKUP_KEEP_LOCAL + 1)) | while read -r old; do
  echo "  удаляю $old"
  rm -f "$old"
done

echo "Готово: $encrypted"
echo
echo "ВАЖНО: наличие файла в хранилище не означает, что из него можно восстановиться."
echo "Проверка восстановления — scripts/postgresql-restore-verify.sh, issue #18."
