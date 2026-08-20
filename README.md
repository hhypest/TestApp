# TestApp

Backend-система создания, публикации, назначения и прохождения тестов. Проект развивается как **modular monolith** на .NET 10 с DDD, Clean Architecture и CQRS.

## Текущий технологический baseline

| Компонент | Текущее состояние |
|---|---|
| Runtime | .NET 10 |
| Persistence | PostgreSQL 18 |
| ORM | EF Core 10.0.11 + провайдер Npgsql EF 10.0.3 |
| Идентификация | Keycloak / JWT Bearer |
| Обмен сообщениями | Транзакционный Outbox + RabbitMQ 4.3.x |
| Observability | OpenTelemetry 1.17.0 + Prometheus/Grafana (local/CI, ADR-027) |
| API | Minimal API, канонический `/api/v1` |
| Развёртывание | Docker/Compose + режим только миграций |
| CI | GitHub Actions + интеграционные тесты на реальных PostgreSQL/RabbitMQ |
| Тесты | 341 (Core/Domain/Application/Integration); conservative exact-head baseline 90.58%, CI floor 90.00% |

## Документация

Полная документация находится в [`docs/`](docs/README.md).

Ключевые документы:

- [Текущее состояние и ограничения](docs/CURRENT_STATE.md)
- [Архитектура](docs/ARCHITECTURE.md)
- [Доменная модель](docs/DOMAIN_MODEL.md)
- [HTTP API](docs/API.md)
- [Руководство пользователя по ролям](docs/USER_GUIDE.md)
- [Persistence / PostgreSQL](docs/PERSISTENCE.md)
- [Events / Outbox / RabbitMQ](docs/EVENTS_AND_OUTBOX.md)
- [Безопасность](docs/SECURITY.md)
- [Эксплуатация и deployment](docs/OPERATIONS.md)
- [Backup / restore](docs/BACKUP_RESTORE.md)
- [SLO и alerts](docs/SLO_ALERTS.md)
- [Performance / capacity](docs/PERFORMANCE.md)
- [Тестирование](docs/TESTING.md)
- [Тесты API в Postman](tests/TestApp.Postman/README.md)
- [Архитектурные решения](docs/DECISIONS.md)
- [Дорожная карта](docs/ROADMAP.md)
- [Детальный план фич](docs/FEATURE_PLAN.md)

Документация различает **Реализовано**, **Проверяется**, **Запланировано** и **Требуется решение**, чтобы наличие автоматизации не путалось с пройденным выходным гейтом, а план развития — с уже существующим функционалом.

## Архитектура

```text
TestApp.Api -> TestApp.Application -> TestApp.Domain -> TestApp.Core
     |                 |
     +-------> TestApp.Infrastructure

TestApp.Application -> TestApp.Messaging
TestApp.Infrastructure -> TestApp.Application + TestApp.Domain
```

Основные aggregates:

```text
Test
  -> PublishedTestRevision
       -> TestAssignment
            -> TestAttempt
```

Ключевое правило: assignments/attempts используют **immutable PublishedTestRevision**, поэтому последующее редактирование working Test не меняет исторические результаты.

## Реализованный core flow

```text
author
  -> create/edit Test
  -> publish immutable revision

admin
  -> assign revision to user/group

student
  -> start attempt
  -> answer
  -> submit / timeout
  -> own result

author/admin
  -> reviewer result/report
```

Поддерживаются:

- `SingleChoice`;
- `MultipleChoice`;
- порог прохождения в процентах;
- необязательное ограничение по времени;
- окна доступности;
- лимиты попыток;
- массовые назначения;
- фоновый воркер автоматического таймаута;
- оценивание по точному совпадению набора;
- разбор правильности для рецензента;
- безопасный для студента DTO результата.

## Identity и authorization

Keycloak — source of truth для пользователей/групп/системных ролей.

Канонические claims:

```text
sub
roles
groups
```

Roles:

```text
test-author
test-admin
```

`Test.OwnerId` содержит Keycloak `sub` создавшего автора. `test-author` видит и изменяет только собственные tests и результаты; `test-admin` имеет явно глобальный scope. Workspace/tenant boundary и multi-realm identity `(issuer, subject)` пока не реализованы и нужны только при соответствующем deployment/business requirement.

## API

Канонический базовый путь:

```text
/api/v1
```

Legacy `/api/*` временно переписывается в `/api/v1/*` для совместимости.

OpenAPI:

```text
GET /openapi/v1.json
```

Health:

```text
GET /health/live
GET /health/ready
```

Readiness проверяет PostgreSQL, а при включённом RabbitMQ Outbox delivery — также broker connection/channel/exchange.

Полный endpoint catalog: [docs/API.md](docs/API.md).

## Persistence

Основная СУБД — **PostgreSQL 18**.

В composition root используется `UseNpgsql(connectionString)`. CI проверяет `SHOW server_version_num` и требует PostgreSQL 18+ (`>= 180000`).

Migrations:

```text
src/TestApp.Infrastructure/Persistence/Migrations
```

Production-режим только миграций:

```bash
dotnet TestApp.Api.dll --migrate
```

API replicas в Production не должны конкурировать за schema migration.

## Concurrency и idempotency

- aggregates используют optimistic `ConcurrencyVersion`;
- EF concurrency conflict преобразуется в HTTP 409;
- start attempt защищён unique DB request key + PostgreSQL advisory lease на assignment/user + короткая transaction для replay/count/insert;
- publish/assign/bulk-assign/submit используют persistent idempotency records + session-level PostgreSQL advisory lease (`pg_try_advisory_lock` / `pg_advisory_unlock`).

Основной HTTP contract использует standard `Idempotency-Key` header; legacy body field временно поддерживается с mismatch validation и request fingerprint. Publish/start/submit принимают настоящий zero-length body, когда key передан только в header.

## Events / Outbox

Только события, явно реализующие `IIntegrationEvent`, могут попасть в transactional Outbox.

RabbitMQ transport реализован:

- at-least-once;
- подтверждения публикации;
- устойчивые сообщения;
- EventId -> MessageId;
- retry/backoff/dead-letter;
- advisory-блокировка между несколькими экземплярами;
- readiness.

При этом production integration event catalog ещё должен быть определён отдельно — обычные domain events не публикуются наружу автоматически.

## Observability и audit

- `X-Correlation-ID`;
- структурированная телеметрия запросов;
- durable audit для POST/PUT/PATCH/DELETE;
- метрики OpenTelemetry для ASP.NET Core/HttpClient/runtime;
- необязательный экспортер OTLP;
- admin operational endpoints для audit и Outbox;
- retention cleanup для audit/idempotency/processed Outbox;
- аудируемые просмотр, повторная постановка и отбрасывание dead-letter;
- operational metrics и SLO/alert contract;
- Prometheus + Grafana в Compose stack — provisioned dashboard и alert rule expressions для `docs/SLO_ALERTS.md` контракта (ADR-027).

## Локальный запуск через Docker Compose

Запуск полного development stack:

```bash
docker compose up --build
```

Сервисы:

- API — `http://localhost:8080`;
- Keycloak — `http://localhost:8081`;
- PostgreSQL 18 — `localhost:5432`;
- RabbitMQ AMQP — `localhost:5672`;
- панель управления RabbitMQ — `http://localhost:15672`;
- OTLP gRPC — `localhost:4317`;
- OTLP HTTP — `localhost:4318`;
- Prometheus — `http://localhost:9090`;
- Grafana — `http://localhost:3000`.

Compose создаёт новый volume `testapp-postgres`. Старый MariaDB volume не удаляется автоматически. Если в нём есть значимые данные, до переключения выполните отдельный [ETL/cutover](docs/PERSISTENCE.md#engine-cutover-и-существующие-mariadb-данные); обычный PostgreSQL backup script MariaDB dump не конвертирует.

Development credentials находятся в [docs/OPERATIONS.md](docs/OPERATIONS.md) и предназначены только для локальной разработки.

## Build/Test

```bash
dotnet restore TestApp.slnx
dotnet build TestApp.slnx --no-restore --configuration Release
dotnet test TestApp.slnx --no-build --configuration Release
dotnet test TestApp.slnx --no-build --configuration Release --collect:"XPlat Code Coverage"
node tests/TestApp.Postman/scripts/validate.mjs
docker compose -f compose.yaml config --quiet
docker build -t testapp-api:local .
bash scripts/validate-observability-stack.sh  # after `docker compose up`
```

Импортируемая Postman collection с real Keycloak flow и Newman CI находится в [`tests/TestApp.Postman`](tests/TestApp.Postman/README.md).

GitHub Actions поднимает настоящие PostgreSQL 18 и RabbitMQ service containers, прогоняет tests, валидирует Compose, строит production image, запускает этот же image в `--migrate` режиме, проверяет logical backup/restore, выполняет image/secret scan и формирует SBOM. Отдельные workflows используют production-shaped stack и real Keycloak для Postman/Newman API contract, performance/capacity gates и Prometheus/Grafana observability stack validation (`observability`, `scripts/validate-observability-stack.sh`).

## Приоритет дальнейшего развития

Ближайший порядок работ:

```text
1. 1.0 release rehearsal: restore, alerts, rollback/API freeze
2. student presentation/resume, затем authoring/reporting и выбранные advanced features
```

Подробно: [docs/ROADMAP.md](docs/ROADMAP.md) и [docs/FEATURE_PLAN.md](docs/FEATURE_PLAN.md).

## Правило для изменений

Новая фича, меняющая domain invariant, public API, schema, security или runtime semantics, должна обновлять соответствующий файл в `docs/` и иметь regression tests в том же change set.
