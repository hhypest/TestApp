#!/usr/bin/env bash
set -euo pipefail

readonly REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly PLACEHOLDER_DIGEST="sha256:0000000000000000000000000000000000000000000000000000000000000000"

# .env.staging.example намеренно содержит пустые секреты. Для разбора compose подставляем
# заведомо фиктивные значения через process environment: они имеют приоритет над env-file,
# не записываются на диск и никогда не используются для запуска контейнеров.
env \
  TESTAPP_PUBLIC_HOST=staging.example.invalid \
  TESTAPP_IMAGE="ghcr.io/example/testapp@${PLACEHOLDER_DIGEST}" \
  TESTAPP_IMAGE_DIGEST="${PLACEHOLDER_DIGEST}" \
  TESTAPP_INTERNAL_CIDR=172.31.0.0/16 \
  POSTGRES_PASSWORD=compose-validation-only \
  RABBITMQ_PASSWORD=compose-validation-only \
  KEYCLOAK_ADMIN_USER=compose-validator \
  KEYCLOAK_ADMIN_PASSWORD=compose-validation-only \
  TESTAPP_KEYCLOAK_CLIENT_SECRET=compose-validation-only \
  GRAFANA_ADMIN_USER=compose-validator \
  GRAFANA_ADMIN_PASSWORD=compose-validation-only \
  docker compose \
    -f "$REPO_ROOT/compose.staging.yaml" \
    --env-file "$REPO_ROOT/.env.staging.example" \
    config --quiet
