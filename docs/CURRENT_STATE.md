# Текущее состояние проекта

> Статус: **Implemented snapshot** на code baseline `ff33a954b381e0632ade738c88fe3ac386003529` (2026-08-13). Phase A, B, C и D1–D5 завершены; D6 реализована, но её exit gate ещё не пройден.

## 1. Назначение системы

TestApp — серверная часть системы создания, публикации, назначения и прохождения тестов. Текущая реализация ориентирована на роли:

- **test-author** — управление только собственными тестами и просмотр результатов собственных тестов;
- **test-admin** — глобальный author/reviewer scope, назначения и operational endpoints;
- **student/user** — собственные назначения, попытки, ответы, submit и собственный результат.

Identity source of truth — Keycloak. TestApp не создаёт локальные user accounts.

## 2. Реализованные продуктовые возможности

### 2.1 Authoring

- создание `Test`;
- immutable `OwnerId` из current Keycloak `sub`;
- изменение названия и settings;
- question/answer-option CRUD и reorder;
- `SingleChoice` и `MultipleChoice`;
- публикация;
- immutable revision history;
- архивирование;
- owner-scoped catalog, editor и revision list;
- admin global scope.

### 2.2 Publication model

- `Draft -> Published`;
- изменение опубликованного working definition возвращает его в `Draft`;
- PublishedTestRevision неизменяема;
- повторная публикация без изменений запрещена;
- Archived test заблокирован для изменения/публикации;
- revision snapshot содержит title/settings/questions/options/correctness.

### 2.3 Ownership boundary

`Test.OwnerId` является обязательным non-null external user ID.

Для `test-author` SQL/read и Application/write boundary ограничены owner:

- catalog;
- editor;
- revision list;
- rename/settings/questions/options;
- publish/archive;
- reviewer result list/detail.

`test-admin` имеет global scope.

Migration существующих tests использует специальный owner `__legacy_admin_only__`, после чего DB default удаляется. Такие legacy records не становятся автоматически доступными случайному author.

### 2.4 Assignments

- назначение конкретной immutable revision;
- target user или group;
- availability window;
- optional attempt limit;
- cancel audit;
- изменение window/attempt-limit;
- bulk assignment до 500 targets;
- deduplication targets;
- admin list/detail/statistics;
- student visibility по direct target или group claims.

### 2.5 Attempts

- target/availability/attempt-limit validation;
- idempotent start;
- deadline из revision snapshot;
- answer/clear/submit;
- ownership attempt;
- background expiration;
- `Submitted`/`TimedOut`;
- `Passed`/`Failed`;
- exact-set scoring;
- reviewer correctness detail;
- student-safe result без correct flags.

## 3. Persistence и consistency

- MariaDB **12.3** runtime/CI baseline;
- EF Core 9.0.18 + Pomelo 9.0.0;
- explicit migrations;
- production `--migrate` mode;
- startup migration разрешена только Development;
- optimistic concurrency через `ConcurrencyVersion`;
- `DbUpdateConcurrencyException -> ConcurrencyConflictException`;
- normalized attempt responses;
- immutable revision questions JSON snapshot;
- distributed MariaDB advisory locks для common idempotency и Outbox.

## 4. HTTP optimistic concurrency

Для mutable authoring resource `Test` реализован HTTP precondition contract.

`GET /api/v1/tests/{id}/editor`:

- возвращает `ConcurrencyVersion`;
- выставляет strong `ETag`, например `"3"`.

Mutating endpoints существующего `Test` требуют `If-Match`:

- rename/settings;
- question/option CRUD/reorder;
- publish;
- archive.

Semantics:

- отсутствует `If-Match` -> `428 concurrency.precondition_required`;
- malformed/weak/wildcard validator -> `400 concurrency.if_match`;
- stale ETag -> `412 concurrency.precondition_failed`;
- valid current ETag -> use case выполняется;
- EF concurrency token остаётся финальным race guard между precondition check и commit.

## 5. Idempotency

### 5.1 HTTP contract

Основной public contract — header:

```text
Idempotency-Key: <UUID>
```

Legacy body `idempotencyKey` временно поддерживается.

Если оба заданы и различаются:

```text
400 idempotency.key_mismatch
```

### 5.2 Request fingerprint

Persistent idempotency rows содержат SHA-256 fingerprint логического request payload.

- одинаковый key + тот же request -> replay прежнего result;
- одинаковый key + другой payload -> `409 idempotency.key_reused`;
- historical rows с NULL fingerprint сохраняют compatibility replay;
- publish operation resource-scoped по TestId.

Fingerprint используется publish/single assignment/bulk assignment/submit. Start attempt дополнительно защищён DB unique key `(AssignmentId, UserId, StartRequestId)` и serializable attempt-limit transaction.

Текущий transport resolver принимает key из header и legacy body. Для publish/start/submit Minimal API всё ещё требует JSON body (`{}` достаточно), даже когда key находится только в header; zero-length body является stabilization gap.

Также replay уже созданного start attempt выполняется внутри repository после повторной проверки текущей availability/group membership. Поэтому retry с тем же key после cancellation/expiry либо изменения group claim сейчас может вернуть `409/403` вместо прежнего attempt ID. Это correctness gap до 1.0.

## 6. Authentication/authorization

- Keycloak JWT Bearer;
- `MapInboundClaims=false`;
- `sub` — external identity;
- `roles` — role claim;
- `groups` — group membership;
- policies `tests:write`, `tests:publish`, `tests:assign`, `results:review`, `operations:read`;
- coarse role checks дополняются owner/attempt/assignment business authorization в Application/read side.

## 7. Production configuration & edge security

Phase A реализована.

### Fail-fast configuration

Outside Development:

- `ConnectionStrings:Database` обязателен;
- startup migrations запрещены;
- Keycloak config обязателен для HTTP host;
- HTTPS metadata ожидается по умолчанию;
- RabbitMQ config валидируется при enabled transport;
- attempt expiration/outbox/rate limits валидируются;
- HTTPS redirect/HSTS deployment decision должен быть явным.

Migration-only `--migrate` не требует Keycloak и transport-security configuration, но composition root пока всё равно загружает и валидирует RabbitMQ/worker/CORS/rate-limit/OpenAPI/proxy options. Для независимого production migration job это известный stabilization gap: целевой режим должен требовать только database configuration.

### Reverse proxy

- ForwardedHeaders opt-in;
- explicit `KnownProxies/KnownNetworks`;
- ForwardLimit;
- untrusted X-Forwarded-* игнорируется.

### CORS/transport headers

- CORS только explicit allow-list;
- wildcard origin запрещён;
- optional credentials;
- configurable HTTPS redirect/HSTS;
- Kestrel server header выключен;
- security headers baseline: `nosniff`, `DENY`, `no-referrer`, Permissions-Policy, restrictive CSP.

### Rate limiting

Configuration-driven classes:

- general;
- student-write;
- privileged-read;
- operations.

Partition key = authenticated `sub`, иначе trusted remote IP.

### OpenAPI exposure

- Development: enabled/public по умолчанию;
- Production: disabled по умолчанию;
- если Production OpenAPI включён с `AllowAnonymous=false`, требуется `operations:read`.

## 8. Audit/correlation/observability

- canonical `/api/v1/*` + legacy rewrite;
- configurable legacy lifecycle с `Deprecation`, optional `Sunset` и `410 api.version.retired`;
- ProblemDetails;
- `X-Correlation-ID`;
- durable audit для state-changing HTTP methods без body/query/secrets;
- OpenTelemetry ASP.NET Core/HttpClient/runtime;
- custom `TestApp.Operations` metrics для Outbox, expiration, retention и bounded API exception categories;
- optional OTLP exporter;
- `/health/live`;
- `/health/ready` MariaDB + RabbitMQ при enabled transport.

**Известное ограничение:** из-за текущего порядка `UseExceptionHandler`/`CorrelationAuditMiddleware` некоторые exceptions, позже корректно преобразованные в HTTP `400/409`, могут сохраниться в audit как `500`. Response для клиента остаётся корректным, но operational audit status требует исправления.

## 9. Events / Outbox / RabbitMQ

- `IDomainEvent` — internal notification;
- только explicit `IIntegrationEvent` сохраняется в Outbox;
- RabbitMQ transport opt-in;
- publisher confirms;
- durable topic exchange/persistent messages;
- EventId -> MessageId;
- at-least-once delivery;
- exponential retry/backoff;
- dead-letter state;
- admin-only safe dead-letter detail/requeue/discard с mandatory reason и action audit;
- MariaDB advisory lock per message;
- operations Outbox monitoring endpoint;
- RabbitMQ readiness.

**Ограничение:** production business integration-event catalog ещё не определён. Готовность transport не означает автоматическую публикацию всех domain events.

**Известное ограничение:** per-message publish failures обрабатываются, но failure batch query/advisory lock/failure-state persistence может выйти из `BackgroundService` cycle. Аналогичный риск есть у initial/batch scan expiration worker; до 1.0 нужен recovery loop с backoff и health/metric signal.

## 10. Deployment / CI

- multi-stage production Docker image;
- non-root runtime user;
- Compose: API, MariaDB 12.3, RabbitMQ, Keycloak, migration container, OTEL Collector;
- CI использует реальные MariaDB/RabbitMQ;
- MariaDB 12.3 runtime assertion;
- restore/build/tests;
- high/critical NuGet vulnerability gate;
- Compose validation;
- production image build;
- `--migrate` из production image;
- logical backup + isolated restore verification;
- repository secret scan;
- production-image HIGH/CRITICAL vulnerability scan;
- CycloneDX SBOM artifact;
- authenticated k6 + expiration/Outbox capacity workflow.

## 11. Главные оставшиеся ограничения

### Stabilization/correctness

До 1.0 необходимо закрыть:

- stable replay `StartAttempt` до mutable assignment checks;
- audit final-status correctness для handled `400/409/412`;
- cycle-level resilience Outbox/expiration workers;
- настоящий zero-body header-only contract для no-payload commands;
- database-only composition для `--migrate`;
- deterministic tie-breaker для offset pagination;
- SQL joins/aggregates вместо high-cardinality ID/score materialization;
- унификацию ожидаемых domain errors/value-object invariants.

### Operational reliability

Реализованы repository-level D1–D5: logical backup/restore CI, retention cleanup, dead-letter management, metrics/SLO contract и security/SBOM workflow.

Не завершены:

- D6: последний exact-head `performance` run упал на RabbitMQ Management API topology setup до создания synthetic Outbox backlog; полного green evidence ещё нет;
- staging/platform restore drill с измеренным RTO;
- реальные dashboards/alert routes и alert drill;
- deployment-owned secret store, backup scheduling/PITR/offsite policy;
- rollback/forward-fix release rehearsal.

### Identity/product breadth

Не реализованы:

- multi-realm `(Issuer, Subject)`;
- Workspace/multi-tenant model;
- frontend application;
- non-choice question types;
- partial/custom scoring;
- manual grading;
- notification business events/consumers;
- advanced analytics.

## 12. Текущий архитектурный уровень

Проект уже является production-oriented modular monolith core, а не CRUD prototype: domain invariants, immutable revisions, owner isolation, HTTP/DB concurrency, distributed idempotency, real infrastructure tests, durable Outbox и deployment path реализованы.

До 1.0 остаются прежде всего **точечные correctness/stability fixes, доказанный D6 gate и deployment rehearsal**. После них первой продуктовой вертикалью должен стать student-safe attempt presentation/resume contract; расширенные типы вопросов следует добавлять позже, по одной versioned vertical slice.
