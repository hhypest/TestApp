#!/bin/sh
# Создаёт отдельную базу для Keycloak в том же инстансе PostgreSQL (issue #16).
#
# Образ postgres выполняет этот скрипт один раз, при инициализации пустого тома.
# Без него Keycloak стартует с KC_DB_URL на несуществующую базу и падает.
#
# Отдельная база, а не отдельный инстанс: контур должен помещаться на одну VM, а Keycloak
# и приложение всё равно делят судьбу этой машины. Для restore drill (#18) это означает,
# что дамп приложения снимается по имени базы приложения и Keycloak не задевает.
set -eu

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<SQL
    SELECT 'CREATE DATABASE ${KEYCLOAK_DB}'
    WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '${KEYCLOAK_DB}')\gexec
SQL
