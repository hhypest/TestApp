# Архитектурные решения TestApp

> Формат: компактный ADR register. `Accepted` означает действующее решение; `Transitional` — временный совместимый контракт; `Superseded` — историческое решение, заменённое новым текущим contract.

## ADR-001 — Modular monolith вместо микросервисов

**Status:** Accepted.

**Decision:** единый deployable backend с внутренними проектными/модульными границами.

**Why:** текущий domain тесно связан транзакциями publication/assignment/attempt; нет независимых команд/scale profiles, оправдывающих distributed complexity.

**Consequences:**

- одна PostgreSQL transactional boundary;
- проще consistency/migrations/testing;
- modules должны сохранять dependency discipline;
- service extraction только по реальному organizational/operational pressure.

## ADR-002 — Clean dependency direction

**Status:** Accepted.

**Decision:** Domain не зависит от Infrastructure/API. Application зависит от Domain abstractions, Infrastructure реализует adapters, API является composition/transport boundary.

**Consequences:**

- Keycloak/EF/RabbitMQ не проникают в Domain;
- domain tests быстрые;
- transport можно менять без изменения invariants.

## ADR-003 — DDD aggregates как write boundary

**Status:** Accepted.

Aggregates:

- Test;
- PublishedTestRevision;
- TestAssignment;
- TestAttempt.

**Decision:** business state transitions выполняются через aggregate methods, а не arbitrary EF property setters/CRUD service.

**Consequences:** invariants централизованы; optimistic concurrency привязана к aggregate mutation.

## ADR-004 — Immutable PublishedTestRevision

**Status:** Accepted.

**Decision:** publication создаёт immutable snapshot. Assignment/Attempt ссылаются на revision ID, не на mutable Test.

**Why:** historical result должен воспроизводиться после дальнейшего редактирования test definition.

**Consequences:**

- correctness history сохраняется;
- editing Published Test возвращает working definition в Draft;
- storage содержит JSON snapshot questions/options.

## ADR-005 — Exact-set scoring для choice questions

**Status:** Accepted current behavior; product extension allowed later.

**Decision:** question points начисляются только при полном совпадении selected set и correct set.

**Consequences:** simple deterministic scoring; partial credit отсутствует.

При добавлении partial scoring нужна новая явная scoring strategy, а не silent изменение исторической semantics.

## ADR-006 — Keycloak является source of truth identity

**Status:** Accepted.

**Decision:** TestApp не создаёт локальных users/passwords. Domain хранит внешние identity IDs.

**Consequences:**

- authentication делегирована OIDC/Keycloak;
- system roles приходят claims;
- business authorization остаётся Application/Domain;
- multi-realm требует будущего `(issuer, subject)` identity key.

## ADR-007 — PostgreSQL как production persistence

**Status:** Accepted.

**Decision:** PostgreSQL 18 runtime/CI baseline.

**History:** SQLite development provider был удалён при переходе на MariaDB; затем MariaDB contract заменён PostgreSQL, а migrations rebased на PostgreSQL baseline.

**Consequences:**

- persistence tests выполняются на real PostgreSQL;
- используются PostgreSQL advisory locks;
- CI проверяет `server_version_num >= 180000`;
- старые MariaDB volumes не удаляются и требуют отдельного ETL, если содержат значимые данные.

## ADR-008 — EF Core 10/Npgsql 10 при net10.0 application

**Status:** Accepted.

**Decision:** Infrastructure использует EF Core 10.0.11 + `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3; integration tests используют Npgsql 10.0.3; приложение таргетирует .NET 10.

**Why:** это актуальная stable major line, соответствующая .NET 10. Microsoft EF Core packages и `dotnet-ef` выровнены на одной patch-версии; provider и driver используют последнюю стабильную версию своей 10.x line. Preview EF/Npgsql 11 исключены из production baseline.

**Compatibility gate:** repository проверяет отсутствие pending model changes, применение существующего baseline к пустой PostgreSQL 18 database, full integration suite и production-image migration path.

**Upgrade rule:** следующий provider/EF major обновляется отдельным compatibility step с review breaking changes и full migration/integration suite.

## ADR-009 — CQRS без отдельной read database

**Status:** Accepted.

**Decision:** commands работают через aggregates/repositories; queries используют отдельные read DTO/projections, но читают ту же PostgreSQL.

**Consequences:**

- CQRS separation без distributed consistency;
- `AsNoTracking`, SQL filters/paging;
- отдельный read store вводится только при измеренной необходимости.

## ADR-010 — Optimistic concurrency через aggregate version

**Status:** Accepted.

**Decision:** `ConcurrencyVersion` — EF concurrency token, `Touch()` на mutation.

**Consequences:** lost update -> conflict/HTTP 409; клиент перечитывает state.

## ADR-011 — Database-backed idempotency

**Status:** Accepted.

**Decision:** retry-safe unsafe operations не полагаются на in-memory locks/cache.

General operations:

- persistent result record;
- session-level PostgreSQL `pg_try_advisory_lock` distributed lease с explicit `pg_advisory_unlock`;
- cache re-check inside lease.

Start attempt:

- domain-specific unique request key;
- PostgreSQL advisory lease на assignment/user + transaction для replay/count/insert.

**Consequences:** несколько API replicas имеют общую retry semantics.

## ADR-012 — Transactional Outbox только для explicit integration events

**Status:** Accepted.

**Decision:** `IDomainEvent` не публикуется наружу автоматически. Только `IIntegrationEvent` сохраняется в Outbox.

**Why:** external event schema является API contract и не должна случайно совпадать с internal domain notification.

**Consequences:** transport может быть готов до определения внешнего event catalog; это допустимо.

## ADR-013 — RabbitMQ и at-least-once delivery

**Status:** Accepted.

**Decision:** RabbitMQ transport opt-in; delivery guarantee — at-least-once.

Mechanisms:

- publisher confirms;
- persistent messages;
- EventId -> MessageId;
- retry/backoff/dead-letter;
- Outbox advisory locks.

**Consequences:** consumer обязан дедуплицировать EventId. Exactly-once не обещается.

## ADR-014 — No-op Outbox publisher запрещён как success path

**Status:** Accepted.

**Decision:** отсутствие configured transport означает отсутствие OutboxProcessor, а не `Publish => success`.

**Why:** no-op success помечал бы событие delivered, хотя оно потеряно.

## ADR-015 — Background timeout eventual processing

**Status:** Accepted.

**Decision:** deadline enforced:

- синхронно на answer/submit;
- background scan с default 30 sec cadence.

**Consequences:** Attempt может оставаться `InProgress` небольшой интервал после deadline, но писать/submit после deadline нельзя; worker eventual фиксирует terminal state.

## ADR-016 — OpenTelemetry через стандартный OTLP

**Status:** Accepted.

**Decision:** instrumentation остаётся vendor-neutral; exporter включается только при OTLP endpoint.

**Consequences:** backend можно менять без Domain/Application changes.

## ADR-017 — Production migrations отдельным job

**Status:** Accepted.

**Decision:** production API replicas не должны автоматически конкурировать за schema migrations. Используется тот же image с `--migrate`.

**Consequences:** deployment pipeline содержит explicit migration gate.

**Current limitation:** migrate-only composition пока загружает часть unrelated RabbitMQ/CORS/rate-limit/proxy options. Это implementation gap, а не изменение решения: целевой job остаётся database-only.

## ADR-018 — Canonical API path `/api/v1`

**Status:** Accepted.

**Decision:** новые clients должны использовать `/api/v1`.

Legacy `/api/*` rewrite сохранён временно.

## ADR-019 — Legacy `/api/*` rewrite

**Status:** Transitional.

**Decision:** старые routes переписываются в canonical v1 до routing.

**Current lifecycle:** enable/disable configuration, `Deprecation`, optional `Sunset`, configured retirement -> `410 api.version.retired` реализованы.

**Exit criteria:** telemetry/client migration и объявленная дата удаления rewrite.

## ADR-020 — Body-based idempotency key

**Status:** Superseded by standard `Idempotency-Key` header contract.

**Historical decision:** commands первоначально принимали `idempotencyKey` в JSON request body.

**Current decision:** primary contract — `Idempotency-Key` header; legacy body field временно поддерживается, header/body mismatch валидируется, common operations используют request fingerprint. Для publish/start/submit JSON body (`{}`) пока обязателен из-за endpoint binding; zero-length body остаётся отдельным implementation gap.

## ADR-021 — Role authorization не заменяет resource ownership

**Status:** Accepted; current `OwnerId` implementation complete for single-organization scope.

**Decision:** Keycloak roles — coarse permission. Resource ownership/eligibility должен проверяться отдельно.

**Current implementation:** `Test.OwnerId = current Keycloak sub`, owner-scoped author/reviewer reads/writes, global `test-admin`, safe legacy backfill.

**Remaining decision:** Workspace/Tenant/ACL и `(issuer, subject)` вводятся только при multi-organization/multi-realm requirement.

## ADR-022 — Не добавлять Event Sourcing без отдельной причины

**Status:** Accepted.

**Decision:** current state persistence + domain events + Outbox достаточны.

Event Sourcing не нужен для:

- revision history (она уже immutable snapshot);
- audit HTTP operations;
- integration delivery.

## ADR-023 — Не добавлять Redis/cache заранее

**Status:** Accepted current policy.

**Decision:** кеш/Redis вводится после profiling конкретного read/coordination bottleneck.

PostgreSQL уже обеспечивает consistency, idempotency locks и indexed read queries.

## ADR-024 — Не вводить отдельный broker кроме RabbitMQ без consumer requirement

**Status:** Accepted.

Kafka/другой transport не добавляется «для универсальности». Event contracts должны зависеть от business boundary, а adapter к другому broker можно добавить позже.

## ADR-025 — Documentation is part of Definition of Done

**Status:** Accepted с созданием `docs/`.

Изменение public API/domain/schema/security/runtime semantics обязано обновлять соответствующий документ в том же change set.
