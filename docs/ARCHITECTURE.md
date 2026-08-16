# Архитектура TestApp

> Статус: **Implemented architecture** + явно выделенный технический долг.

## 1. Архитектурный стиль

TestApp развивается как **modular monolith** на .NET 10. Основные принципы:

- Domain-Driven Design для бизнес-инвариантов;
- Clean Architecture для направления зависимостей;
- CQRS на уровне application/read-side разделения;
- Minimal API как transport boundary;
- PostgreSQL как единственная transactional source of truth;
- Keycloak как внешний source of truth для identity;
- transactional Outbox для будущих/текущих integration events;
- RabbitMQ как opt-in transport;
- background workers внутри того же deployable API process.

Проект **не является** набором микросервисов и не использует Event Sourcing.

## 2. Solution structure

```text
src/
  TestApp.Core
  TestApp.Domain
  TestApp.Messaging
  TestApp.Application
  TestApp.Infrastructure
  TestApp.Api

tests/
  TestApp.Domain.Tests
  TestApp.Application.Tests
  TestApp.IntegrationTests
```

### 2.1 Dependency graph

```mermaid
graph TD
    API[TestApp.Api] --> APP[TestApp.Application]
    API --> INFRA[TestApp.Infrastructure]
    INFRA --> APP
    INFRA --> DOMAIN[TestApp.Domain]
    APP --> DOMAIN
    APP --> CORE[TestApp.Core]
    APP --> MSG[TestApp.Messaging]
    DOMAIN --> CORE
```

Фактические project references:

- `TestApp.Domain -> TestApp.Core`;
- `TestApp.Application -> TestApp.Core + TestApp.Domain + TestApp.Messaging`;
- `TestApp.Infrastructure -> TestApp.Application + TestApp.Domain`;
- `TestApp.Api -> TestApp.Application + TestApp.Infrastructure`.

`TestApp.Messaging` не зависит от Domain/Application и содержит CQRS messaging abstractions.

## 3. Обязанности слоёв

### TestApp.Core

Минимальные общие primitives, не зависящие от ASP.NET Core/EF/Keycloak. Текущий реально используемый functional primitive — `Result<TValue, TError>`; speculative `Option`/`Validation` удалены до появления подтверждённого use case.

### TestApp.Domain

Содержит:

- aggregates и entities;
- strong IDs;
- business invariants;
- domain errors;
- domain events;
- immutable publication model;
- scoring/state transitions.

Domain **не должен** ссылаться на:

- `HttpContext`;
- `ClaimsPrincipal`;
- JWT;
- Keycloak SDK;
- EF Core;
- RabbitMQ;
- OpenTelemetry;
- transport DTO.

### TestApp.Messaging

Содержит только используемые generic `ICommand<TResponse>`, `IQuery<TResponse>` и соответствующие handler abstractions. Dispatcher, notification bus и broker integration сюда не добавляются до появления реального потребителя.

### TestApp.Application

Содержит use cases:

- commands/queries;
- repository/UoW/actor/clock/idempotency abstractions;
- permission-independent business orchestration;
- transport-neutral `Error`;
- CQRS read contracts.

Command/query records располагаются рядом со своими handlers и группируются по одному use-case family. Общий файл не используется как каталог всех application types: это сохраняет локальность изменений и делает review границ сценария явным.

Application не знает о PostgreSQL SQL, RabbitMQ client и ASP.NET endpoint routing.

### TestApp.Infrastructure

Реализует:

- EF Core/PostgreSQL persistence;
- repositories/read models;
- optimistic concurrency mapping;
- PostgreSQL advisory locks;
- persistent idempotency store;
- Keycloak claim mapping/current actor;
- Outbox processing;
- RabbitMQ publisher/readiness;
- audit trail;
- health checks;
- OpenTelemetry;
- background attempt expiration worker.

`IReadModelQueries` реализован одним Infrastructure adapter, но его EF projections разделены partial-файлами по editor, assignment, attempt и reviewer сценариям в `Persistence/ReadModels/`. Это сохраняет единый DI contract и одновременно не заставляет reviewer разбирать несвязанные запросы в одном файле.

### TestApp.Api

Содержит:

- Minimal API routing;
- JWT Bearer setup;
- authorization policies;
- transport request records;
- ProblemDetails mapping;
- API version path;
- legacy rewrite;
- rate limiting;
- correlation/audit middleware;
- DI composition root.

`Program.cs` содержит только runtime composition и порядок middleware. HTTP request contracts вынесены в `ApiRequests.cs`, application-handler registrations — в `DependencyInjection.cs`, route mapping разделён на feature-модули в `Endpoints/`, runtime configuration — по concerns в `Configuration/`, OpenAPI transformers и operation catalog — в `OpenApi/`. Загруженные runtime option values передаются непосредственно их runtime consumers; неиспользуемые `IOptions<T>` registrations не создаются. Правила поддержания этой структуры описаны в [CODE_REVIEW.md](CODE_REVIEW.md).

## 4. Write-side flow

Типовой write flow:

```mermaid
sequenceDiagram
    participant Client
    participant API
    participant Handler as Application Handler
    participant Aggregate as Domain Aggregate
    participant Repo as Repository/EF
    participant DB as PostgreSQL

    Client->>API: HTTP command
    API->>Handler: typed command
    Handler->>Repo: load aggregate
    Repo->>DB: SELECT
    Handler->>Aggregate: domain operation
    Aggregate-->>Handler: Result<T, DomainError>
    Handler->>DB: SaveChanges
    DB-->>Handler: commit / concurrency error
    Handler-->>API: Result<T, Error>
    API-->>Client: 200 / ProblemDetails
```

Ключевой принцип: бизнес-инварианты проверяются aggregate/domain method, а не endpoint code.

## 5. Read-side flow

```mermaid
sequenceDiagram
    participant Client
    participant API
    participant QueryHandler
    participant ReadQueries as Infrastructure projection
    participant DB as PostgreSQL

    Client->>API: GET
    API->>QueryHandler: query DTO
    QueryHandler->>ReadQueries: normalized filters/paging
    ReadQueries->>DB: AsNoTracking projection
    DB-->>ReadQueries: projected rows
    ReadQueries-->>API: read model
    API-->>Client: JSON
```

Read-side не обязан materialize aggregates. Для каталога/результатов/assignment administration используются отдельные projection DTO.

## 6. HTTP request pipeline

Текущий порядок middleware/endpoint setup концептуально:

```text
legacy /api/* -> /api/v1/* rewrite
        ↓
UseRouting
        ↓
CORS (if enabled)
        ↓
RequestTelemetryMiddleware
        ↓
CorrelationAuditMiddleware
        ↓
ExceptionHandler
        ↓
Authentication
        ↓
RateLimiter
        ↓
Authorization
        ↓
Minimal API endpoint
```

OpenAPI доступен через `/openapi/v1.json`.

### Audit и exception ordering

`CorrelationAuditMiddleware` оборачивает `ExceptionHandler`. Correlation ID поэтому доступен exception mapping, а после возврата downstream pipeline audit видит финальный handled status. Binding/concurrency exceptions сохраняются как фактические `400/409`, обычный precondition result — как `412`, unhandled exception — как `500`. Actor определяется после выполнения downstream authentication.

### Почему rewrite выполняется до routing

Legacy compatibility меняет `Request.Path`, поэтому canonical route должен быть выбран после rewrite. Если rewrite выполнить после endpoint routing, выбранный endpoint не пересчитывается и старые URLs получают 404.

## 7. Background processing

### 7.1 OverdueAttemptProcessor

**Всегда зарегистрирован** через Infrastructure.

- batch size default: 100;
- poll interval default: 30 seconds;
- выбирает IDs просроченных `InProgress` attempts;
- каждая попытка обрабатывается в отдельном DI scope;
- application handler повторно проверяет state/deadline;
- optimistic concurrency безопасно разрешает гонку с submit.

Per-attempt exceptions обрабатываются, но initial/batch DB scan выполняется без cycle-level recovery boundary. Transient query failure способен завершить `BackgroundService`; это stabilization gap.

### 7.2 OutboxProcessor

Регистрируется только когда подключён реальный publisher (`RabbitMq:Enabled=true` для RabbitMQ path).

- default batch size 100;
- poll interval 5 seconds;
- retry exponential backoff;
- max attempts 10;
- PostgreSQL advisory lock по OutboxMessage ID;
- at-least-once delivery.

Per-message publish failures получают retry/dead-letter state. Ошибки batch query, advisory lock acquisition либо сохранения failure state могут выйти из worker loop; до 1.0 нужен общий resilient cycle с bounded backoff/health signal.

## 8. Concurrency model

Все основные aggregates наследуют `AggregateRoot<TId>` и имеют `ConcurrencyVersion`.

EF mapping использует его как concurrency token. Business mutation вызывает `Touch()`. При конфликте:

```text
DbUpdateConcurrencyException
    -> ConcurrencyConflictException
    -> API ProblemDetails HTTP 409
```

Это защищает от lost updates между несколькими API requests/instances.

## 9. Idempotency architecture

Два механизма существуют сознательно.

### Start attempt

Специализированная DB-safe модель:

- `StartRequestId` хранится в `test_attempts`;
- unique `(AssignmentId, UserId, StartRequestId)`;
- attempt limit проверяется под PostgreSQL advisory lease на `(AssignmentId, UserId)` внутри короткой transaction.

Application handler сначала выполняет actor-scoped replay lookup по `(AssignmentId, UserId, StartRequestId)`. Уже закоммиченный start воспроизводится до загрузки assignment и mutable target/availability checks, поэтому cancellation, expiry и изменение group claim не ломают retry. Для нового key handler выполняет полный eligibility flow, а repository берёт session-level PostgreSQL advisory lease на assignment/user и повторяет lookup перед count/insert в transaction, закрывая concurrent race между API replicas без SSI serialization storm.

### Publish/assign/bulk-assign/submit

Общий `IdempotencyStore`:

1. lookup cached result;
2. bounded retry `pg_try_advisory_lock` по deterministic 64-bit hash(database + namespace + operation + actor + requestId);
3. повторный lookup после lease;
4. use case;
5. insert `idempotency_records`;
6. business result + idempotency result commit одной UoW;
7. explicit `pg_advisory_unlock` и dispose dedicated Npgsql connection.

## 10. Event boundaries

Aggregate может поднимать `IDomainEvent`. `AppDbContext.SaveChangesAsync()` помещает в Outbox **только** `IIntegrationEvent`.

Это разделение запрещает неосознанно публиковать внутренние domain details наружу.

Текущие бизнес-события вроде `TestPublished`, `TestAssigned`, `AttemptSubmitted` — domain events. Integration contracts должны создаваться явно как стабильные внешние schemas.

## 11. Runtime dependencies

- PostgreSQL 18 — required;
- Keycloak — required для нормальной JWT authentication runtime;
- RabbitMQ — optional, только если `RabbitMq:Enabled=true`;
- OTLP collector — optional;
- Docker — не требуется приложению, но используется для стандартного deployment/dev stack.

## 12. Architectural constraints для новых фич

Новая фича должна соблюдать:

1. Domain не получает Infrastructure/API dependencies.
2. Endpoint не содержит бизнес-правила, кроме transport parsing/policy selection.
3. Student read models не раскрывают `IsCorrect`.
4. Published revision не меняется после публикации.
5. Assignment всегда ссылается на revision, не на mutable Test.
6. Cross-instance retry hazard должен иметь DB-backed idempotency/concurrency strategy.
7. Integration event должен быть отдельным явным contract, а не «любой domain event».
8. Read-side filtering/paging должен выполняться в SQL до materialization, если это технически возможно.

## 13. Текущие архитектурные gaps

- `Test.OwnerId` и owner-scoped writes/reads реализованы; tenant/workspace boundary сознательно отсутствует до business requirement;
- handlers регистрируются напрямую в API, общего command/query dispatcher pipeline нет; это допустимо, пока cross-cutting duplication остаётся управляемым;
- standard `Idempotency-Key` resolver/fingerprint реализован, но no-payload publish/start/submit всё ещё требуют JSON body (`{}`), а не настоящий zero-length body;
- Outbox/expiration workers не имеют cycle-level recovery для DB/query/lock failures;
- migration-only composition всё ещё загружает unrelated RabbitMQ/CORS/rate-limit/proxy options;
- paged read models не везде имеют deterministic timestamp + ID tie-breaker;
- reviewer/admin queries materialize большие revision-ID/score sets вместо SQL joins/aggregates;
- student-safe attempt presentation DTO с question/option text ещё отсутствует;
- migrations поддерживаются explicit files и актуальным EF model snapshot;
- Outbox transport готов, но production integration event catalog ещё не определён;
- нет separate read database/cache — и сейчас это сознательно не требуется.

## 14. Когда разделять монолит

Не следует выделять микросервисы только ради масштаба кода. Разделение имеет смысл, если появится конкретный operational/business pressure:

- независимый release cadence;
- отдельная ownership команда;
- значимо разные scaling profiles;
- тяжёлые analytics/workloads, мешающие transactional API;
- независимые security/compliance boundaries.

До появления этих факторов modular monolith остаётся предпочтительной архитектурой.
