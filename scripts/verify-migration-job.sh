#!/usr/bin/env bash
set -euo pipefail

# Проверяет release/init-задание миграций — issue #21 пункт 4, гейт docs/ROADMAP.md §7 пункт 4.
#
# Проверяется три утверждения, каждое из которых иначе подтверждено только чтением кода:
#
#   1. `--migrate` применяет baseline на чистой базе, и в истории оказывается ровно то, что
#      лежит в репозитории — ни больше, ни меньше;
#   2. повторный `--migrate` на уже мигрированной базе не падает и не меняет схему. Это не
#      теоретический случай: release job перезапускают после сетевого сбоя, и «применилось
#      второй раз» — это порча схемы в момент выпуска;
#   3. вне Development автоприменение миграций на старте отвергается. По коду это так
#      (RuntimeConfiguration.LoadDatabase), но гейт требует поведения процесса, а не строки
#      в исходнике.
#
# Отпечаток схемы снимается pg_dump'ом с отфильтрованными строками \restrict/\unrestrict:
# PostgreSQL 18 вставляет в них случайный nonce, поэтому побайтовое сравнение дампов
# нестабильно само по себе и дало бы мигающую проверку вместо содержательной.
#
# По умолчанию задание запускается production-образом. MIGRATE_CMD подменяет команду
# целиком — так же скрипт запускается против образа контура или против локальной сборки.

IMAGE="${IMAGE:-testapp-api:ci}"
POSTGRES_IMAGE="${POSTGRES_IMAGE:-postgres:18@sha256:06cad38a5d9f5d24b4d83d86def30795d5e4b757fedbf5281172b576dedcd941}"
DOCKER_NETWORK="${DOCKER_NETWORK:-host}"
PGHOST="${PGHOST:-127.0.0.1}"
PGPORT="${PGPORT:-5432}"
PGUSER="${PGUSER:-testapp}"
PGDATABASE="${PGDATABASE:-testapp}"
PGPASSWORD="${PGPASSWORD:-testapp}"
CONNECTION_STRING="${CONNECTION_STRING:-Host=${PGHOST};Port=${PGPORT};Database=${PGDATABASE};Username=${PGUSER};Password=${PGPASSWORD};}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

fail() { echo "FAIL: $*" >&2; exit 1; }

run_migrate() {
  local apply_on_startup="$1"
  if [ -n "${MIGRATE_CMD:-}" ]; then
    local command
    read -ra command <<< "$MIGRATE_CMD"
    env ASPNETCORE_ENVIRONMENT=Production \
        "ConnectionStrings__Database=$CONNECTION_STRING" \
        "Database__ApplyMigrationsOnStartup=$apply_on_startup" \
        "${command[@]}"
  else
    docker run --rm --network "$DOCKER_NETWORK" \
      -e ASPNETCORE_ENVIRONMENT=Production \
      -e "ConnectionStrings__Database=$CONNECTION_STRING" \
      -e "Database__ApplyMigrationsOnStartup=$apply_on_startup" \
      "$IMAGE" --migrate
  fi
}

psql_value() {
  docker run --rm --network "$DOCKER_NETWORK" -e "PGPASSWORD=$PGPASSWORD" "$POSTGRES_IMAGE" \
    psql --host="$PGHOST" --port="$PGPORT" --username="$PGUSER" --dbname="$PGDATABASE" \
    --tuples-only --no-align --set=ON_ERROR_STOP=1 --command="$1"
}

schema_fingerprint() {
  docker run --rm --network "$DOCKER_NETWORK" -e "PGPASSWORD=$PGPASSWORD" "$POSTGRES_IMAGE" \
    pg_dump --host="$PGHOST" --port="$PGPORT" --username="$PGUSER" --dbname="$PGDATABASE" --schema-only \
    | grep -vE '^(--|\\restrict |\\unrestrict )' \
    | sha256sum | cut -d' ' -f1
}

echo "== 1. Применение миграций на чистой базе =="
run_migrate false

expected="$(find "$REPO_ROOT/src/TestApp.Infrastructure/Persistence/Migrations" -maxdepth 1 -name '*.cs' \
  ! -name '*.Designer.cs' ! -name 'AppDbContextModelSnapshot.cs' | wc -l | tr -d ' ')"
recorded="$(psql_value 'select count(*) from "__EFMigrationsHistory";' | tr -d ' ')"
[ "$expected" = "$recorded" ] \
  || fail "в истории $recorded миграций, а в репозитории $expected — схема и код разошлись"
echo "Применено и записано миграций: $recorded."

before="$(schema_fingerprint)"

echo "== 2. Повторный прогон на уже мигрированной базе =="
run_migrate false
after="$(schema_fingerprint)"
[ "$before" = "$after" ] \
  || fail "повторный --migrate изменил схему ($before -> $after) — задание не идемпотентно"
echo "Схема не изменилась: $after."

echo "== 3. Автоприменение миграций на старте вне Development =="
# Ожидается отказ. Успешный старт здесь означал бы, что обычный перезапуск API способен
# менять схему production-базы — то есть выпуск перестаёт быть управляемым событием.
if output="$(run_migrate true 2>&1)"; then
  echo "$output" >&2
  fail "процесс стартовал с Database:ApplyMigrationsOnStartup=true в Production"
fi
grep -q -- "--migrate" <<< "$output" \
  || fail "отказ произошёл, но сообщение не называет штатный путь (--migrate): $output"
echo "Отказано, сообщение указывает на задание --migrate."

echo "Проверка задания миграций пройдена."
