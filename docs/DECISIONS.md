# Архитектурные решения TestApp

> Формат: компактный ADR register. Статус `Accepted` означает действующее решение; `Transitional` — временный контракт, который уже имеет planned replacement.

## ADR-001 — Modular monolith вместо микросервисов

**Status:** Accepted.

**Decision:** единый deployable backend с внутренними проектными/модульными границами.

**Why:** текущий domain тесно связан транзакциями publication/assignment/attempt; нет независимых команд/scale profiles, оправдывающих distributed complexity.

**Consequences:**

- одна MariaDB transactional boundary;
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

## ADR-007 — MariaDB как production persistence

**Status:** Accepted.

**Decision:** MariaDB 12.3 runtime/CI baseline.

**History:** SQLite development provider удалён; migrations rebased на MariaDB baseline.

**Consequences:**

- persistence tests выполняются на real MariaDB;
- используются MariaDB advisory locks;
- CI проверяет actual server version >= 12.3.

## ADR-008 — EF Core 9/Pomelo 9 при net10.0 application

**Status:** Accepted current compatibility choice.

**Decision:** Infrastructure использует EF Core 9.0.18 + Pomelo 9.0.0, приложение таргетирует .NET 10.

**Why:** стабильная совместимая provider line для MariaDB.

**Upgrade rule:** provider/EF major обновляется отдельным compatibility step с full migration/integration suite.

## ADR-009 — CQRS без отдельной read database

**Status:** Accepted.

**Decision:** commands работают через aggregates/repositories; queries используют отдельные read DTO/projections, но читают ту же MariaDB.

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
- MariaDB `GET_LOCK` distributed lease;
- cache re-check inside lease.

Start attempt:

- domain-specific unique request key;
- serializable attempt-limit transaction.

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

## ADR-018 — Canonical API path `/api/v1`

**Status:** Accepted.

**Decision:** новые clients должны использовать `/api/v1`.

Legacy `/api/*` rewrite сохранён временно.

## ADR-019 — Legacy `/api/*` rewrite

**Status:** Transitional.

**Decision:** старые routes переписываются в canonical v1 до routing.

**Exit criteria:** формальная version/deprecation policy и migration clients.

## ADR-020 — Body-based idempotency key

**Status:** Transitional.

**Decision:** текущие commands принимают `idempotencyKey` в JSON request body.

**Target:** standard HTTP `Idempotency-Key` header + request fingerprint semantics.

## ADR-021 — Role authorization не заменяет resource ownership

**Status:** Accepted principle; implementation incomplete.

**Decision:** Keycloak roles — coarse permission. Resource ownership/eligibility должен проверяться отдельно.

**Current gap:** Test ownership ещё не смоделирован.

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

MariaDB уже обеспечивает consistency, idempotency locks и indexed read queries.

## ADR-024 — Не вводить отдельный broker кроме RabbitMQ без consumer requirement

**Status:** Accepted.

Kafka/другой transport не добавляется «для универсальности». Event contracts должны зависеть от business boundary, а adapter к другому broker можно добавить позже.

## ADR-025 — Documentation is part of Definition of Done

**Status:** Accepted с созданием `docs/`.

Изменение public API/domain/schema/security/runtime semantics обязано обновлять соответствующий документ в том же change set.