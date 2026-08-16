# Текущее состояние проекта

> Статус: **Implemented snapshot** ветки `beta-ddd`, 2026-08-16. Phase A–D7 полностью реализованы (STAB-001..008, API-009, PostgreSQL migration, EF Core 10 upgrade). Exact-head evidence определяется последними GitHub Actions runs ветки.

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

- PostgreSQL **18** runtime/CI baseline;
- EF Core 10.0.11 + Npgsql EF provider 10.0.3;
- generated PostgreSQL baseline migration + model snapshot;
- production `--migrate` mode;
- startup migration разрешена только Development;
- optimistic concurrency через `ConcurrencyVersion`;
- `DbUpdateConcurrencyException -> ConcurrencyConflictException`;
- normalized attempt responses;
- immutable revision questions JSON snapshot;
- session-level PostgreSQL advisory locks для common idempotency, Outbox и retention.

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

Fingerprint используется publish/single assignment/bulk assignment/submit. Start attempt дополнительно защищён DB unique key `(AssignmentId, UserId, StartRequestId)` и PostgreSQL advisory lease на `(AssignmentId, UserId)` вокруг replay/count/insert transaction.

Текущий transport resolver принимает key из header и legacy body. Publish/start/submit принимают настоящий zero-length body (Minimal API request DTO параметр nullable), когда key находится только в header.

Actor-scoped lookup уже созданного start attempt выполняется до загрузки assignment и mutable availability/group-membership checks. Поэтому retry того же пользователя с тем же key возвращает прежний attempt ID после cancellation, expiry или изменения group claim. Новый key по-прежнему проходит все актуальные eligibility checks; транзакционная повторная проверка в repository сохраняет race safety.

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

Migration-only `--migrate` загружает только database configuration (`RuntimeConfiguration.LoadDatabase`); Keycloak/RabbitMQ/worker/CORS/rate-limit/OpenAPI/transport-security/reverse-proxy loaders и связанные DI-регистрации пропускаются, так что независимый production migration job не должен получать эти secrets/config.

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
- Prometheus + Grafana в `compose.yaml` как local/CI metrics backend позади OTel Collector: dashboard (`TestApp Overview`) и alert rule expressions (`deploy/prometheus/alerts.yml`, все технически выразимые правила `docs/SLO_ALERTS.md` §4) provisioned и CI-validated (ADR-027, `OBS-012`/`OBS-013`);
- `/health/live`;
- `/health/ready` PostgreSQL + RabbitMQ при enabled transport.

`CorrelationAuditMiddleware` оборачивает exception handler и сохраняет финальный status state-changing response. Контракт покрыт real HTTP/PostgreSQL regression tests для `400`, `409`, `412` и `500`.

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
- PostgreSQL advisory lock per message;
- operations Outbox monitoring endpoint;
- RabbitMQ readiness.

**Ограничение:** production business integration-event catalog ещё не определён. Готовность transport не означает автоматическую публикацию всех domain events.

Per-message publish failures обрабатываются, и cycle-level failures (batch query/advisory lock/failure-state persistence) больше не могут вывести `BackgroundService` из `PeriodicTimer` loop: `OutboxProcessor`/`OverdueAttemptProcessor` логируют cycle failure и продолжают на следующий poll tick (`RunCycleAsync`).

## 10. Deployment / CI

- multi-stage production Docker image;
- non-root runtime user;
- Compose: API, PostgreSQL 18, RabbitMQ, Keycloak, migration container, OTEL Collector;
- CI использует реальные PostgreSQL/RabbitMQ;
- PostgreSQL 18 runtime assertion;
- restore/build/tests;
- high/critical NuGet vulnerability gate;
- Compose validation;
- production image build;
- `--migrate` из production image;
- logical backup + isolated restore verification;
- repository secret scan;
- production-image HIGH/CRITICAL vulnerability scan;
- CycloneDX SBOM artifact;
- importable Postman Collection v2.1 с real Keycloak author/admin/student flow;
- Newman API contract workflow с JUnit artifact;
- authenticated k6 + expiration/Outbox capacity workflow.

## 11. Главные оставшиеся ограничения

### Stabilization/correctness

Все P0/P1 stabilization findings текущего backlog (STAB-001..008) закрыты; domain/application errors и value-object invariants имеют единый `Result<T, DomainError>` failure contract (ADR-026).

### Operational reliability

Реализованы repository-level D1–D6: logical backup/restore CI, retention cleanup, dead-letter management, metrics/SLO contract, security/SBOM workflow и PostgreSQL/RabbitMQ capacity gate. D6 evidence на `9916b98`: 8771/8771 checks, HTTP failure rate 0, expiration 1247 -> 0 за 14 s, Outbox 100 -> 0 за 1 s. Дополнительно реализован и CI-validated local/CI observability backend — Prometheus + Grafana в `compose.yaml` (dashboard + alert rule expressions, `OBS-012`/`OBS-013`, ADR-027).

Не завершены:

- staging/platform restore drill с измеренным RTO;
- alert routes (Alertmanager + pager/chat receiver) и alert drill против staging — rule expressions сами по себе уже реализованы и оцениваются в Prometheus, но никуда не маршрутизируются;
- активный readiness prober (`blackbox_exporter` или аналог) для `/health/ready` — сейчас есть только metrics-pipeline health check;
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

Точечные correctness/stability fixes (Phase D7, `STAB-001..008`) закрыты; release notes/changelog (`DEV-005`) и local/CI dashboard+alert rule expressions (`OBS-012`/`OBS-013`) тоже. До 1.0 остаётся прежде всего **deployment rehearsal**: staging restore drill с измеренным RTO, маршрутизация алертов в actionable destination + alert drill против staging, rollback/forward-fix repetition. После них первой продуктовой вертикалью должен стать student-safe attempt presentation/resume contract; расширенные типы вопросов следует добавлять позже, по одной versioned vertical slice.
