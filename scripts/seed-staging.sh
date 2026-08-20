#!/usr/bin/env bash
set -euo pipefail

# Засев контура данными эксплуатационного объёма — issue #25. Одна команда, три шага:
#
#   1. завести учётные записи студентов в Keycloak (их нет в realm контура намеренно);
#   2. фаза authoring: автор создаёт и публикует тесты, админ раздаёт назначения;
#   3. фаза attempts: студенты проходят назначенное.
#
# Данные идут через публичный API, а не через INSERT: только так появляются связанные
# записи Outbox, audit и idempotency, целостность которых потом проверяет restore drill
# (#18). Прямой SQL здесь используется ровно в одном месте — при подсчёте итогового
# объёма, то есть на чтении.
#
# Скрипт не идемпотентен по построению и не притворяется таковым: повторный запуск
# отвергается по метке SEED_TAG, потому что удвоенный объём делает baseline #20
# невоспроизводимым. Нужен второй набор — задайте другой SEED_TAG.

usage() {
  cat <<'USAGE'
Usage: scripts/seed-staging.sh [--check]

Обязательные переменные окружения:
  BASE_URL                 адрес API                     (например https://testapp.example.org)
  KEYCLOAK_URL             адрес Keycloak                (например https://testapp.example.org/auth)
  KEYCLOAK_REALM           realm контура                 (testapp-staging)
  KEYCLOAK_CLIENT_SECRET   секрет confidential-клиента
  KEYCLOAK_ADMIN_USERNAME  администратор Keycloak (realm master)
  KEYCLOAK_ADMIN_PASSWORD  его пароль
  AUTHOR_USERNAME/AUTHOR_PASSWORD   учётная запись автора
  ADMIN_USERNAME/ADMIN_PASSWORD     учётная запись администратора тестов
  SEED_STUDENT_PASSWORD    пароль, назначаемый всем создаваемым студентам

Необязательные (профиль объёма, умолчания — docs/PERFORMANCE.md):
  SEED_TAG SEED_TESTS SEED_QUESTIONS_PER_TEST SEED_OPTIONS_PER_QUESTION
  SEED_BULK_TARGETS_PER_TEST SEED_STUDENTS SEED_ATTEMPTS_PER_STUDENT
  SEED_SUBMITTED_SHARE SEED_VUS
  COMPOSE_FILE             файл compose для подсчёта объёма (по умолчанию compose.staging.yaml)
  SKIP_VOLUME_REPORT=1     не считать итоговый объём

Режимы:
  --check                  проверить обязательные переменные и завершиться без сети
USAGE
}

CHECK_ONLY=0
case "${1:-}" in
  -h|--help) usage; exit 0 ;;
  --check) CHECK_ONLY=1 ;;
  "") ;;
  *) echo "FAIL: неизвестный аргумент '$1'. scripts/seed-staging.sh --help" >&2; exit 2 ;;
esac

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
K6_IMAGE="${K6_IMAGE:-grafana/k6:1.4.0@sha256:6a3ee54ac0e9ff5527923f6295257453dd88012f32f40dadf0eb1b638cbb21c7}"
COMPOSE_FILE="${COMPOSE_FILE:-compose.staging.yaml}"

SEED_TAG="${SEED_TAG:-seed-v1}"
SEED_STUDENTS="${SEED_STUDENTS:-500}"

# Имена в .env относятся к контейнерам staging, а runbook исторически использовал
# короткие имена для k6. Принимаем оба варианта, чтобы источник секрета оставался один
# и оператору не приходилось копировать значения вручную.
KEYCLOAK_CLIENT_ID="${KEYCLOAK_CLIENT_ID:-testapp-api}"
KEYCLOAK_CLIENT_SECRET="${KEYCLOAK_CLIENT_SECRET:-${TESTAPP_KEYCLOAK_CLIENT_SECRET:-}}"
KEYCLOAK_ADMIN_USERNAME="${KEYCLOAK_ADMIN_USERNAME:-${KEYCLOAK_ADMIN_USER:-}}"
export KEYCLOAK_CLIENT_ID KEYCLOAK_CLIENT_SECRET KEYCLOAK_ADMIN_USERNAME

require() {
  local name="$1"
  if [ -z "${!name:-}" ]; then
    echo "FAIL: переменная $name не задана. scripts/seed-staging.sh --help" >&2
    exit 1
  fi
}

for variable in BASE_URL KEYCLOAK_URL KEYCLOAK_REALM KEYCLOAK_CLIENT_SECRET \
                KEYCLOAK_ADMIN_USERNAME KEYCLOAK_ADMIN_PASSWORD \
                AUTHOR_USERNAME AUTHOR_PASSWORD ADMIN_USERNAME ADMIN_PASSWORD SEED_STUDENT_PASSWORD; do
  require "$variable"
done

if [ "$CHECK_ONLY" = "1" ]; then
  echo "OK: обязательные переменные staging seed заданы; сетевые запросы не выполнялись."
  exit 0
fi

echo "== 1. Учётные записи студентов ($SEED_STUDENTS шт., префикс ${SEED_TAG}-student-) =="

admin_token() {
  curl --fail --silent --show-error \
    --data "client_id=admin-cli" \
    --data "grant_type=password" \
    --data-urlencode "username=${KEYCLOAK_ADMIN_USERNAME}" \
    --data-urlencode "password=${KEYCLOAK_ADMIN_PASSWORD}" \
    "${KEYCLOAK_URL}/realms/master/protocol/openid-connect/token" \
    | python3 -c 'import json,sys;print(json.load(sys.stdin)["access_token"])'
}

TOKEN="$(admin_token)"

# Группа students обязана существовать: групповое назначение раздаётся именно на неё, и
# студент вне группы просто не увидит ни одного теста.
groups="$(curl --fail --silent --show-error -H "Authorization: Bearer $TOKEN" \
  "${KEYCLOAK_URL}/admin/realms/${KEYCLOAK_REALM}/groups?search=students")"
if ! echo "$groups" | grep -q '"name":"students"'; then
  echo "FAIL: в realm ${KEYCLOAK_REALM} нет группы students — realm импортирован не тем файлом." >&2
  exit 1
fi

created=0
existing=0

post_student() {
  local username="$1"
  curl --silent --output /dev/null --write-out '%{http_code}' \
    -X POST -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
    --data "$(python3 - "$username" "$SEED_STUDENT_PASSWORD" <<'USERJSON'
import json, sys
# firstName/lastName/email обязательны: декларативный user profile Keycloak считает учётную
# запись без них незаполненной и отклоняет password grant с "Account is not fully set up",
# хотя requiredActions при этом пуст, а пользователь включён. Найдено смоук-прогоном.
print(json.dumps({
    "username": sys.argv[1],
    "enabled": True,
    "emailVerified": True,
    "firstName": "Seed",
    "lastName": sys.argv[1],
    "email": sys.argv[1] + "@seed.invalid",
    "requiredActions": [],
    "groups": ["/students"],
    "credentials": [{"type": "password", "value": sys.argv[2], "temporary": False}],
}))
USERJSON
)" \
    "${KEYCLOAK_URL}/admin/realms/${KEYCLOAK_REALM}/users"
}

for index in $(seq 0 $((SEED_STUDENTS - 1))); do
  username="$(printf '%s-student-%05d' "$SEED_TAG" "$index")"
  status="$(post_student "$username")"

  # Токен администратора живёт минуты, а создание сотен пользователей — дольше. 401 здесь
  # ожидаем и означает истёкший токен, а не отказ в доступе: обновляем и повторяем ровно раз.
  if [ "$status" = "401" ]; then
    TOKEN="$(admin_token)"
    status="$(post_student "$username")"
  fi

  case "$status" in
    201) created=$((created + 1)) ;;
    409) existing=$((existing + 1)) ;;
    *) echo "FAIL: создание пользователя $username вернуло HTTP $status" >&2; exit 1 ;;
  esac

  if [ $(( (index + 1) % 100 )) -eq 0 ]; then
    echo "  ...$((index + 1))/${SEED_STUDENTS}"
  fi
done
echo "Создано: $created, уже существовало: $existing."

run_k6() {
  local phase="$1"
  docker run --rm --network host \
    -v "$REPO_ROOT/performance/k6:/scripts:ro" \
    -e BASE_URL -e KEYCLOAK_URL -e KEYCLOAK_REALM -e KEYCLOAK_CLIENT_ID -e KEYCLOAK_CLIENT_SECRET \
    -e AUTHOR_USERNAME -e AUTHOR_PASSWORD -e ADMIN_USERNAME -e ADMIN_PASSWORD \
    -e SEED_TAG -e SEED_VUS -e SEED_TESTS -e SEED_QUESTIONS_PER_TEST -e SEED_OPTIONS_PER_QUESTION \
    -e SEED_BULK_TARGETS_PER_TEST -e SEED_STUDENTS -e SEED_ATTEMPTS_PER_STUDENT \
    -e SEED_SUBMITTED_SHARE -e SEED_STUDENT_PASSWORD -e SEED_ALLOW_APPEND -e SEED_MAX_DURATION \
    -e "PHASE=$phase" \
    "$K6_IMAGE" run /scripts/seed-data.js
}

echo "== 2. Фаза authoring =="
run_k6 authoring

echo "== 3. Фаза attempts =="
run_k6 attempts

if [ "${SKIP_VOLUME_REPORT:-0}" = "1" ]; then
  echo "Засев завершён. Отчёт об объёме пропущен (SKIP_VOLUME_REPORT=1)."
  exit 0
fi

echo "== 4. Фактический объём =="
# Без этих цифр показатели ёмкости невоспроизводимы: неизвестно, на чём они получены.
# Таблица печатается в формате docs/PERFORMANCE.md, чтобы её можно было перенести как есть.
if [ -z "${POSTGRES_USER:-}" ] || [ -z "${POSTGRES_DB:-}" ]; then
  echo "(подсчёт строк пропущен: не заданы POSTGRES_USER/POSTGRES_DB)"
else
  # n_live_tup из pg_stat_user_tables является статистической оценкой и может отставать
  # сразу после массового засева. Для воспроизводимого baseline печатаем точные COUNT(*).
  if ! docker compose -f "$REPO_ROOT/$COMPOSE_FILE" exec -T postgres \
    psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At <<'SQL'
select format(
  'select %L || to_char(count(*), ''FM999G999G999'') || '' |'' from %I.%I;',
  '| ' || relname || ' | ',
  schemaname,
  relname
)
from pg_stat_user_tables
order by relname;
\gexec
SQL
  then
    echo "(подсчёт строк пропущен: postgres недоступен из $COMPOSE_FILE)"
  fi

  docker compose -f "$REPO_ROOT/$COMPOSE_FILE" exec -T postgres \
    psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At -c \
    "select 'Размер базы: ' || pg_size_pretty(pg_database_size(current_database()));" || true
fi

echo
echo "Проверьте состояние Outbox до снятия дампа:"
echo "  GET ${BASE_URL}/api/v1/operations/outbox  (роль operations:read)"
echo "Растущий backlog означает, что засев сам создал аварию — #19 примет её за drill."
