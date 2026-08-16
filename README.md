# TestApp

Backend-система создания, публикации, назначения и прохождения тестов. Проект развивается как **modular monolith** на .NET 10 с DDD, Clean Architecture и CQRS.

## Текущий технологический baseline

| Компонент | Текущее состояние |
|---|---|
| Runtime | .NET 10 |
| Persistence | PostgreSQL 18 |
| ORM | EF Core 10.0.11 + Npgsql EF provider 10.0.3 |
| Identity | Keycloak / JWT Bearer |
| Messaging | Transactional Outbox + RabbitMQ 4.3.x |
| Observability | OpenTelemetry 1.17.0 |
| API | Minimal API, canonical `/api/v1` |
| Deployment | Docker/Compose + migration-only mode |
| CI | GitHub Actions + real PostgreSQL/RabbitMQ integration tests |

## Документация

Полная документация находится в [`docs/`](docs/README.md).

Ключевые документы:

- [Текущее состояние и ограничения](docs/CURRENT_STATE.md)
- [Архитектура](docs/ARCHITECTURE.md)
- [Доменная модель](docs/DOMAIN_MODEL.md)
- [HTTP API](docs/API.md)
- [Persistence / PostgreSQL](docs/PERSISTENCE.md)
- [Events / Outbox / RabbitMQ](docs/EVENTS_AND_OUTBOX.md)
- [Безопасность](docs/SECURITY.md)
- [Эксплуатация и deployment](docs/OPERATIONS.md)
- [Backup / restore](docs/BACKUP_RESTORE.md)
- [SLO и alerts](docs/SLO_ALERTS.md)
- [Performance / capacity](docs/PERFORMANCE.md)
- [Тестирование](docs/TESTING.md)
- [Postman API tests](tests/TestApp.Postman/README.md)
- [Архитектурные решения](docs/DECISIONS.md)
- [Дорожная карта](docs/ROADMAP.md)
- [Детальный план фич](docs/FEATURE_PLAN.md)

Документация различает **Implemented**, **Verifying**, **Planned** и **Decision required**, чтобы наличие automation не путалось с пройденным exit gate, а план развития — с уже существующим функционалом.

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
- passing percentage;
- optional time limit;
- availability windows;
- attempt limits;
- bulk assignments;
- automatic timeout worker;
- exact-set scoring;
- reviewer correctness breakdown;
- student-safe result DTO.

## Identity и authorization

Keycloak — source of truth для пользователей/групп/системных ролей.

Canonical claims:

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

Canonical base:

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

Production migration-only mode:

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
- publisher confirms;
- persistent messages;
- EventId -> MessageId;
- retry/backoff/dead-letter;
- multi-instance advisory lock;
- readiness.

При этом production integration event catalog ещё должен быть определён отдельно — обычные domain events не публикуются наружу автоматически.

## Observability и audit

- `X-Correlation-ID`;
- structured request telemetry;
- durable audit для POST/PUT/PATCH/DELETE;
- OpenTelemetry ASP.NET Core/HttpClient/runtime metrics;
- optional OTLP exporter;
- admin operational endpoints для audit и Outbox;
- retention cleanup для audit/idempotency/processed Outbox;
- audited dead-letter detail/requeue/discard;
- operational metrics и SLO/alert contract.

## Local Docker Compose

Запуск полного development stack:

```bash
docker compose up --build
```

Сервисы:

- API — `http://localhost:8080`;
- Keycloak — `http://localhost:8081`;
- PostgreSQL 18 — `localhost:5432`;
- RabbitMQ AMQP — `localhost:5672`;
- RabbitMQ management — `http://localhost:15672`;
- OTLP gRPC — `localhost:4317`;
- OTLP HTTP — `localhost:4318`.

Compose создаёт новый volume `testapp-postgres`. Старый MariaDB volume не удаляется автоматически. Если в нём есть значимые данные, до переключения выполните отдельный [ETL/cutover](docs/PERSISTENCE.md#engine-cutover-и-существующие-mariadb-данные); обычный PostgreSQL backup script MariaDB dump не конвертирует.

Development credentials находятся в [docs/OPERATIONS.md](docs/OPERATIONS.md) и предназначены только для локальной разработки.

## Build/Test

```bash
dotnet restore TestApp.slnx
dotnet build TestApp.slnx --no-restore --configuration Release
dotnet test TestApp.slnx --no-build --configuration Release
node tests/TestApp.Postman/scripts/validate.mjs
docker compose -f compose.yaml config --quiet
docker build -t testapp-api:local .
```

Импортируемая Postman collection с real Keycloak flow и Newman CI находится в [`tests/TestApp.Postman`](tests/TestApp.Postman/README.md).

GitHub Actions поднимает настоящие PostgreSQL 18 и RabbitMQ service containers, прогоняет tests, валидирует Compose, строит production image, запускает этот же image в `--migrate` режиме, проверяет logical backup/restore, выполняет image/secret scan и формирует SBOM. Отдельные workflows используют production-shaped stack и real Keycloak для Postman/Newman API contract и performance/capacity gates.

## Приоритет дальнейшего развития

Ближайший порядок работ:

```text
1. 1.0 release rehearsal: restore, alerts, rollback/API freeze
2. student presentation/resume, затем authoring/reporting и выбранные advanced features
```

Подробно: [docs/ROADMAP.md](docs/ROADMAP.md) и [docs/FEATURE_PLAN.md](docs/FEATURE_PLAN.md).

## Правило для изменений

Новая фича, меняющая domain invariant, public API, schema, security или runtime semantics, должна обновлять соответствующий файл в `docs/` и иметь regression tests в том же change set.
