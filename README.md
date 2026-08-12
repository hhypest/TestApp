# TestApp

Backend-система создания, публикации, назначения и прохождения тестов. Проект развивается как **modular monolith** на .NET 10 с DDD, Clean Architecture и CQRS.

## Текущий технологический baseline

| Компонент | Текущее состояние |
|---|---|
| Runtime | .NET 10 |
| Persistence | MariaDB 12.3 |
| ORM | EF Core 9.0.18 + Pomelo 9.0.0 |
| Identity | Keycloak / JWT Bearer |
| Messaging | Transactional Outbox + RabbitMQ 4.3.x |
| Observability | OpenTelemetry 1.17.0 |
| API | Minimal API, canonical `/api/v1` |
| Deployment | Docker/Compose + migration-only mode |
| CI | GitHub Actions + real MariaDB/RabbitMQ integration tests |

## Документация

Полная документация находится в [`docs/`](docs/README.md).

Ключевые документы:

- [Текущее состояние и ограничения](docs/CURRENT_STATE.md)
- [Архитектура](docs/ARCHITECTURE.md)
- [Доменная модель](docs/DOMAIN_MODEL.md)
- [HTTP API](docs/API.md)
- [Persistence / MariaDB](docs/PERSISTENCE.md)
- [Events / Outbox / RabbitMQ](docs/EVENTS_AND_OUTBOX.md)
- [Безопасность](docs/SECURITY.md)
- [Эксплуатация и deployment](docs/OPERATIONS.md)
- [Тестирование](docs/TESTING.md)
- [Архитектурные решения](docs/DECISIONS.md)
- [Дорожная карта](docs/ROADMAP.md)
- [Детальный план фич](docs/FEATURE_PLAN.md)

Документация различает **Implemented**, **Planned** и **Decision required**, чтобы план развития не воспринимался как уже существующий функционал.

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

Важное текущее ограничение: `Test` пока не имеет owner/workspace boundary, поэтому `test-author` — coarse role для общей доверенной author-группы. Это зафиксировано как P0/P1 задача в roadmap.

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

Readiness проверяет MariaDB, а при включённом RabbitMQ Outbox delivery — также broker connection/channel/exchange.

Полный endpoint catalog: [docs/API.md](docs/API.md).

## Persistence

Основная СУБД — **MariaDB 12.3**.

В production composition root используется `ServerVersion.AutoDetect(connectionString)`. CI отдельно проверяет фактический `SELECT VERSION()` и требует MariaDB >= 12.3.

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
- start attempt защищён unique DB request key + serializable attempt-limit transaction;
- publish/assign/bulk-assign/submit используют persistent idempotency records + MariaDB `GET_LOCK/RELEASE_LOCK` distributed lease.

Текущий HTTP idempotency key передаётся в JSON body; переход на standard `Idempotency-Key` header находится в roadmap.

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
- admin operational endpoints для audit и Outbox.

## Local Docker Compose

Запуск полного development stack:

```bash
docker compose up --build
```

Сервисы:

- API — `http://localhost:8080`;
- Keycloak — `http://localhost:8081`;
- MariaDB 12.3 — `localhost:3306`;
- RabbitMQ AMQP — `localhost:5672`;
- RabbitMQ management — `http://localhost:15672`;
- OTLP gRPC — `localhost:4317`;
- OTLP HTTP — `localhost:4318`.

После перехода со старой MariaDB development volume рекомендуется чистый локальный старт:

```bash
docker compose down -v
docker compose up --build
```

Development credentials находятся в [docs/OPERATIONS.md](docs/OPERATIONS.md) и предназначены только для локальной разработки.

## Build/Test

```bash
dotnet restore TestApp.slnx
dotnet build TestApp.slnx --no-restore --configuration Release
dotnet test TestApp.slnx --no-build --configuration Release
docker compose -f compose.yaml config --quiet
docker build -t testapp-api:local .
```

GitHub Actions поднимает настоящие MariaDB 12.3 и RabbitMQ service containers, прогоняет tests, валидирует Compose, строит production image и запускает этот же image в `--migrate` режиме.

## Приоритет дальнейшего развития

Ближайший порядок работ:

```text
1. production configuration / proxy / TLS / configurable rate limiting
2. Test ownership / resource-level authorization
3. standard Idempotency-Key + HTTP concurrency/OpenAPI contracts
4. backup/restore/retention/alerts/load tests
5. 1.0 stabilization
6. authoring productivity / reporting / selected advanced assessment features
```

Подробно: [docs/ROADMAP.md](docs/ROADMAP.md) и [docs/FEATURE_PLAN.md](docs/FEATURE_PLAN.md).

## Правило для изменений

Новая фича, меняющая domain invariant, public API, schema, security или runtime semantics, должна обновлять соответствующий файл в `docs/` и иметь regression tests в том же change set.