# Текущее состояние проекта

> Статус: **Implemented snapshot**. Этот документ описывает фактическое состояние `beta-ddd` после перехода на MariaDB 12.3.

## 1. Назначение системы

TestApp — серверная часть системы создания, публикации, назначения и прохождения тестов. Текущая реализация ориентирована на следующие роли:

- **test-author** — создание/редактирование/публикация тестов, просмотр результатов;
- **test-admin** — все author-capabilities плюс назначения и operational endpoints;
- **student/user** — просмотр собственных назначений, запуск попыток, ответы, submit и просмотр собственного результата.

Пользователи и группы не создаются внутри TestApp: identity source of truth — Keycloak.

## 2. Реализованные продуктовые возможности

### 2.1 Authoring

**Implemented:**

- создание `Test`;
- изменение названия;
- настройки passing percentage и optional time limit;
- добавление/обновление/удаление/перестановка вопросов;
- добавление/обновление/удаление/перестановка вариантов ответа;
- типы вопросов `SingleChoice` и `MultipleChoice`;
- публикация теста;
- immutable revision history;
- архивирование;
- catalog read-side с pagination/status/search;
- editor view;
- revision list.

### 2.2 Publication model

**Implemented:**

- `Draft -> Published` после успешной публикации;
- изменение опубликованного working `Test` автоматически возвращает его в `Draft`;
- опубликованные revisions не изменяются после создания;
- повторный publish без изменений запрещён;
- archived test нельзя редактировать или повторно публиковать;
- revision содержит title, settings, ordered questions/options и correctness snapshot.

### 2.3 Assignments

**Implemented:**

- назначение конкретной `PublishedTestRevision`;
- target: ровно один user или group;
- availability window;
- optional attempt limit;
- cancel с actor/time/reason audit fields;
- изменение окна доступности и attempt limit для active assignment;
- bulk assignment до 500 targets;
- deduplication targets внутри bulk request;
- admin list/detail/attempt statistics;
- student visibility по direct user target или membership в external group.

### 2.4 Attempts

**Implemented:**

- start attempt только для target-user/target-group;
- проверка availability window;
- database-safe attempt limit;
- idempotent start по `(AssignmentId, UserId, StartRequestId)`;
- snapshot question IDs при старте;
- ответы только option IDs, принадлежащие revision/question;
- ownership: пользователь не может работать с чужой попыткой;
- deadline рассчитывается из immutable revision;
- answer/clear/submit после deadline переводят attempt в timeout либо возвращают conflict согласно use case;
- background expiration worker переводит просроченные `InProgress` attempts в `TimedOut` без пользовательского запроса;
- terminal states: `Submitted` и `TimedOut`;
- outcome: `Passed`/`Failed`;
- exact-set scoring;
- reviewer result detail с correctness;
- student result без раскрытия correctness.

### 2.5 Reporting/read side

**Implemented:**

- `/me/assignments`;
- `/me/attempts`;
- attempt detail/result;
- reviewer result list с фильтрами test/revision/outcome;
- reviewer detailed result с selected/correct answer breakdown;
- admin assignment statistics;
- pagination с нормализацией `page >= 1`, `pageSize = 1..100`.

## 3. Реализованные технические возможности

### Persistence

- MariaDB **12.3** как runtime/CI baseline;
- EF Core 9.0.18 + Pomelo 9.0.0;
- explicit migrations;
- production `--migrate` mode;
- optimistic concurrency через `ConcurrencyVersion` на aggregates;
- `DbUpdateConcurrencyException -> ConcurrencyConflictException -> HTTP 409`;
- normalized assignment target (`TargetType`, `TargetId`);
- normalized mutable attempt responses;
- JSON snapshot published revision.

### Idempotency

- persistent idempotency records;
- MariaDB `GET_LOCK/RELEASE_LOCK` distributed lease;
- publish, single assign, bulk assign и submit защищены common idempotency store;
- start attempt защищён отдельным unique DB key и serializable attempt-limit transaction.

### Authentication/authorization

- Keycloak JWT Bearer;
- `MapInboundClaims=false`;
- `sub` как primary external user ID;
- `roles` как role claim;
- `groups` поддерживаются как scalar или JSON array claims;
- policies `tests:write`, `tests:publish`, `tests:assign`, `results:review`, `operations:read`.

### HTTP/runtime

- canonical API `/api/v1/*`;
- compatibility rewrite старого `/api/*` в `/api/v1/*`;
- OpenAPI `/openapi/v1.json`;
- ProblemDetails;
- global fixed-window rate limiter: 120 req/min на `sub`, fallback — remote IP;
- `X-Correlation-ID`;
- structured request logging;
- durable audit trail для POST/PUT/PATCH/DELETE;
- liveness/readiness endpoints.

### Outbox/RabbitMQ

- transactional Outbox infrastructure;
- publish только для `IIntegrationEvent`;
- at-least-once semantics;
- RabbitMQ publisher confirms;
- durable topic exchange;
- persistent messages;
- `EventId -> MessageId`;
- retry/backoff/dead-letter;
- MariaDB advisory lock на OutboxMessage;
- operational outbox status endpoint;
- RabbitMQ readiness when transport enabled.

### Observability/deployment

- OpenTelemetry traces/metrics;
- ASP.NET Core, HttpClient, runtime instrumentation;
- optional OTLP exporter;
- multi-stage Docker image;
- Docker Compose: API + MariaDB 12.3 + RabbitMQ + Keycloak + migration job + OTEL collector;
- GitHub Actions проверяет build/tests/Compose/image/production-image migration path.

## 4. Критические текущие ограничения

Следующие пункты **не реализованы** и должны учитываться при проектировании клиентов/production deployment.

### 4.1 Нет ownership/tenant boundary для Test

`Test` не хранит `CreatedBy/OwnerId`, а authoring handlers не проверяют владение. Роль `test-author` в текущем виде даёт доступ к общему authoring catalog и операциям над тестами по известному `TestId`.

Это допустимо только для доверенной единой author-группы. Для multi-team/multi-tenant эксплуатации требуется отдельная ownership-модель.

### 4.2 Integration transport готов, но production integration event contracts ещё не сформированы

Domain events (`TestPublished`, `TestAssigned`, `AttemptStarted`, `AttemptSubmitted`, `AttemptTimedOut` и др.) являются `IDomainEvent`, но не автоматически `IIntegrationEvent`. `AppDbContext` помещает в Outbox только события, явно реализующие `IIntegrationEvent`.

Следствие: наличие RabbitMQ transport не означает, что все domain events публикуются наружу.

### 4.3 Idempotency key пока находится в JSON body

Unsafe operations используют `Guid IdempotencyKey`/`RequestId` в request body. Стандартный HTTP `Idempotency-Key` header пока не является общим API contract.

### 4.4 Rate limit пока hard-coded

Global limit — 120 requests/minute, queue limit 0. Нет конфигурируемых policy classes для auth, authoring, student answering, operational endpoints.

### 4.5 Production configuration пока имеет development fallback

`ConnectionStrings:Database` имеет fallback `testapp/testapp` в `Program.cs`. Для production нужен fail-fast validation и исключение insecure fallback вне Development.

### 4.6 Multi-realm identity не реализована

В домене external user сейчас идентифицируется только Keycloak `sub`. Для нескольких issuers/realms требуется identity key `(Issuer, Subject)` или эквивалентная модель.

### 4.7 Нет полноценного user-facing UI

Репозиторий содержит backend API/runtime. Отдельный frontend application в текущей solution отсутствует.

### 4.8 Question model ограничен choice-вопросами

`QuestionType` содержит только:

- `SingleChoice`;
- `MultipleChoice`.

Free-text, numeric, ordering, matching, file upload и manual grading отсутствуют.

### 4.9 Scoring только exact-set

Для MultipleChoice вопрос получает все points только при точном совпадении выбранного множества с correct set. Partial/negative/custom scoring отсутствует.

### 4.10 Нет formal author/reviewer ownership rules

`results:review` разрешён любой роли `test-author`/`test-admin`; filter по owner отсутствует.

## 5. Технический долг, который не должен маскироваться новыми фичами

- production fail-fast configuration/secrets;
- trusted proxies/forwarded headers/TLS policy;
- configurable rate limiting;
- standard HTTP idempotency contract;
- ownership/authorization model для tests/results;
- OpenAPI descriptions/examples/version lifecycle;
- explicit production integration event catalog;
- migration model snapshot/standardized EF migration workflow;
- retention policies для audit/idempotency/outbox;
- backup/restore/DR runbook;
- load/performance tests;
- SLO/alerts dashboards;
- dependency/security scanning в CI.

## 6. Определение текущего архитектурного уровня

Проект уже вышел за рамки CRUD-прототипа: присутствуют aggregate invariants, immutable revisions, application use cases, real DB concurrency, distributed idempotency, integration tests на реальных инфраструктурных сервисах и production-oriented runtime path.

При этом до production-ready 1.0 остаются прежде всего **security/ownership/configuration/operational** вопросы, а не необходимость в микросервисах или новой инфраструктурной сложности.