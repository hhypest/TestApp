# Дорожная карта развития TestApp

> Baseline: `beta-ddd`, 2026-08-12. Roadmap описывает **последовательность**, а не обещанные календарные даты. Каждый этап начинается только после зелёных gates предыдущего.

## 0. Принцип развития

Приоритет проекта:

```text
correctness & data isolation
    > production safety
    > stable API contracts
    > operational reliability
    > product breadth
    > scale complexity
```

Не следует добавлять новые question types/analytics/microservices, пока остаются P0 security/ownership/configuration gaps.

## 1. Baseline — уже реализовано

### Release band: `0.8.x` — Core architecture baseline

**Status: DONE.**

Реализовано:

- modular monolith / Clean Architecture / DDD/CQRS;
- Test authoring lifecycle;
- immutable PublishedTestRevision;
- SingleChoice/MultipleChoice publication validation;
- exact-set scoring;
- user/group assignments;
- availability + attempt limit;
- bulk assignment;
- attempt start/answer/clear/submit/timeout;
- background deadline expiration;
- reviewer/admin read sides;
- MariaDB 12.3;
- optimistic concurrency;
- distributed idempotency;
- Minimal API `/api/v1` + legacy rewrite;
- Keycloak roles/groups;
- audit/correlation;
- transactional Outbox;
- RabbitMQ transport + confirms/retry/dead-letter/readiness;
- OpenTelemetry;
- Docker/Compose;
- production migration-only image path;
- real MariaDB/RabbitMQ CI integration tests.

**Exit gate:** full CI green on MariaDB 12.3.

---

# 2. Phase A — Production configuration & edge security

### Target band: `0.9.0`
### Priority: P0
### Status: **DONE**
### Goal: безопасный запуск API вне developer machine.

Реализовано:

- typed/validated runtime configuration для Database, Keycloak, RabbitMQ, Outbox, attempt expiration, rate limiting, OpenAPI, CORS, transport security и reverse proxy;
- production DB fallback удалён; Development fallback остаётся только в Development;
- Production запрещает `Database:ApplyMigrationsOnStartup=true`, migrations выполняются через `--migrate`;
- trusted `ForwardedHeaders` только через configured `KnownProxies/KnownNetworks`;
- deterministic tests: untrusted forwarded headers игнорируются;
- explicit Production HTTPS redirect/HSTS decision;
- CORS allow-list, wildcard origin запрещён;
- secure response headers (`nosniff`, frame deny, referrer/permissions/CSP baseline);
- Kestrel server header отключён;
- rate limit classes: general, student-write, privileged-read, operations;
- production OpenAPI disabled by default, при включении может быть admin-only;
- high/critical NuGet vulnerability audit является CI gate (`NU1903/NU1904`).

**Exit gate: PASSED** — configuration/security tests, boundary E2E, production image и migration smoke зелёные.

---

# 3. Phase B — Resource ownership и authorization boundary

### Target band: `0.9.1`
### Priority: P0
### Status: **DONE**
### Goal: исключить cross-author data access.

Принято решение для текущего single-organization этапа:

```text
Test.OwnerId = Keycloak sub создавшего автора
```

Workspace остаётся эволюционным следующим шагом, а не обязательной сложностью текущей версии.

Реализовано:

- `Test.OwnerId` — обязательный immutable owner;
- новый test получает `OwnerId` из `ICurrentActor.UserId`;
- authoring commands, publish/archive проверяют ownership в Application;
- catalog/editor/revision/reviewer list/reviewer detail фильтруются по owner на SQL-side;
- `test-admin` сохраняет global scope;
- reviewer DTO с `IsCorrect` не доступен чужому author;
- migration `TestOwnership` backfill-ит существующие записи owner `__legacy_admin_only__`, затем удаляет DB default;
- индекс `(OwnerId, Status)`;
- cross-author E2E: author A не видит и не изменяет test/result author B; admin видит оба.

Assignment administration остаётся global `test-admin` policy до появления Workspace scope.

**Exit gate: PASSED** — two-author negative E2E, SQL filters и production migration зелёные.

---

# 4. Phase C — API contract maturity

### Target band: `0.9.2`
### Priority: P0/P1
### Status: **IN PROGRESS**

## C1. Standard `Idempotency-Key`

**Status: DONE.**

Реализовано:

- `Idempotency-Key` header — основной HTTP retry contract;
- legacy body `idempotencyKey` временно поддерживается;
- если header и body заданы и не совпадают -> `400 idempotency.key_mismatch`;
- canonical SHA-256 request fingerprint вычисляется в Application из логических полей команды;
- fingerprint хранится в `idempotency_records.RequestFingerprint`;
- один key + другой payload -> `409 idempotency.key_reused`;
- старые records с `RequestFingerprint = NULL` сохраняют replay compatibility;
- header-only replay и fingerprint mismatch покрыты MariaDB/API E2E;
- publish operation дополнительно resource-scoped по TestId.

## C2. HTTP optimistic concurrency contract

**Status: DONE.**

Реализовано для mutable `Test` authoring:

- editor DTO содержит `ConcurrencyVersion`;
- `GET /api/v1/tests/{id}/editor` возвращает strong `ETag: "N"`;
- изменения существующего `Test` требуют `If-Match`;
- отсутствующий `If-Match` -> `428 concurrency.precondition_required`;
- weak/wildcard/invalid validator -> `400 concurrency.if_match`;
- stale strong ETag -> `412 concurrency.precondition_failed`;
- application handlers проверяют expected aggregate version до mutation;
- EF Core concurrency token остаётся последней защитой от race между precondition check и commit;
- ETag/If-Match regression E2E покрывает fresh/stale/missing/invalid validators.

## C3. Validation normalization

**Status: NEXT.**

Единый transport validation strategy:

- malformed UUID/enums;
- required fields;
- max lengths;
- body model validation;
- stable error codes;
- generic framework binding errors не должны становиться нестабильным публичным контрактом.

## C4. OpenAPI quality

Добавить:

- operation summaries/descriptions;
- examples;
- auth requirements;
- ProblemDetails schemas;
- pagination schemas;
- `Idempotency-Key`/`If-Match`/`ETag` metadata;
- enum documentation;
- deprecation metadata.

## C5. API version lifecycle

Определить:

- срок поддержки v1;
- deprecation policy;
- retirement legacy `/api/*` rewrite;
- breaking-change rules.

**Exit gate Phase C:** generated OpenAPI contract snapshot + full boundary E2E.

---

# 5. Phase D — Operational reliability

### Target band: `0.9.3`
### Priority: P0/P1

## D1. Backup/restore

- MariaDB backup strategy;
- RPO/RTO;
- automated restore verification;
- staging restore drill.

## D2. Retention/cleanup

Policies/jobs для:

- audit entries;
- idempotency records;
- processed Outbox messages;
- dead letters;
- old operational records.

Cleanup должен быть batch/index-friendly.

## D3. Dead-letter management

Admin command/API:

- inspect safe metadata;
- requeue after fix;
- acknowledge/drop only with explicit audit;
- prohibit accidental duplicate uncontrolled replay.

## D4. SLO/alerts

Минимальные signals:

- API error rate;
- p95 latency;
- readiness failures;
- DB connection/concurrency errors;
- Outbox lag/dead-letter;
- expiration lag;
- RabbitMQ failures.

## D5. Dependency/security CI

Уже реализовано:

- high/critical NuGet vulnerability gate.

Остаётся:

- container image scanning;
- secret scanning;
- SBOM/artifact metadata.

## D6. Load/capacity tests

Critical scenarios:

- simultaneous start attempts;
- answer write burst;
- bulk assignments;
- reviewer pagination;
- expiration storm;
- Outbox backlog recovery.

**Exit gate:** staging soak + restore drill + alert verification.

---

# 6. Phase E — 1.0 stabilization

### Target: `1.0.0`
### Priority: P0

1. Freeze v1 public contracts.
2. Close P0 security findings.
3. Verify ownership isolation.
4. Verify migration from supported previous schema.
5. Production config contains no dev secrets/fallback.
6. Backup restore demonstrated.
7. Full CI green repeatedly.
8. Load target defined and met.
9. Integration event exposure explicitly documented (even if none enabled).
10. Release notes + operational rollback/forward-fix plan.

**1.0 definition:** production-safe core assessment workflow, не максимальное количество feature types.

---

# 7. Phase F — Authoring productivity

### Target band: `1.1.x`
### Priority: P1

После 1.0 расширять author experience.

## F1. Clone test / create draft from revision

Позволяет быстро создавать новый test/version definition без ручного копирования.

## F2. Tags/categories/search

- tags;
- category/subject;
- enhanced catalog filters;
- indexes.

## F3. Question bank — Decision required

Если подтверждён use case повторного использования questions:

- отдельный QuestionBankItem aggregate/read model;
- copy/reference semantics должны быть явными;
- published revision всё равно snapshot-ит content.

## F4. Import/export

Начать с versioned JSON contract. CSV подходит только для ограниченных simple-choice cases.

## F5. Draft validation endpoint

Показать publication errors до publish command без изменения state.

---

# 8. Phase G — Advanced assessment behavior

### Target band: `1.2.x`
### Priority: P1/P2, product decisions required

## G1. Randomization

Варианты:

- shuffle question order;
- shuffle option order;
- deterministic seed per attempt.

Historical attempt должен хранить/воспроизводить фактический presentation order.

## G2. Question pools

Выбор N из pool на attempt требует attempt snapshot выбранных question IDs и stable scoring maximum.

## G3. Partial scoring

Новая explicit scoring strategy:

- ExactSet;
- PartialPositive;
- custom policy.

Revision должна snapshot-ить strategy/version, чтобы старые результаты не менялись.

## G4. New question types

Порядок рекомендуемый:

1. Numeric/short deterministic answer;
2. FreeText + manual grading;
3. Ordering;
4. Matching;
5. attachments/rich content.

Каждый тип должен иметь собственную validation/scoring model; нельзя перегружать `OptionIds` универсальным JSON blob.

## G5. Manual grading

Потребует нового lifecycle:

```text
Submitted -> AwaitingReview -> Graded
```

и отделения auto score от final score.

---

# 9. Phase H — Assignment orchestration & notifications

### Target band: `1.3.x`
### Priority: P1/P2

## H1. Assignment templates/batches

Reusable campaign-like assignment configuration.

## H2. Scheduled assignment activation

Window уже есть; добавить admin scheduling/readability без внешнего cron при отсутствии необходимости.

## H3. Notifications

Только после определения production integration events:

- assignment created;
- deadline approaching;
- result completed.

Notification consumer должен быть отдельным adapter/service только если это реально нужно; core API не должен отправлять email внутри business transaction.

## H4. Group expansion policy

Сейчас group membership проверяется в момент доступа через claims. Решить product semantics:

- dynamic membership;
- snapshot membership at assignment time.

Это важное бизнес-решение для compliance/training cases.

---

# 10. Phase I — Reporting & analytics

### Target band: `1.4.x`
### Priority: P1/P2

## I1. Aggregated test dashboards

- completion rate;
- pass rate;
- average/median;
- time-to-complete;
- question difficulty.

## I2. Export

Admin/reviewer CSV/JSON export с authorization и audit.

## I3. Question analytics

Для immutable revision анализировать по `(RevisionId, QuestionId)`.

## I4. Separate analytics store — только при необходимости

Если transactional queries становятся тяжёлыми:

- projection tables;
- materialized analytics;
- warehouse/columnar store.

Не вводить заранее.

---

# 11. Phase J — Workspace/multi-tenant evolution

### Target: `2.x` или раньше при business requirement
### Priority: depends on product

Если система обслуживает несколько организаций/подразделений:

- Workspace/Tenant aggregate;
- memberships;
- workspace-scoped roles;
- test/assignment/result isolation;
- `(Issuer, Subject)` identity;
- tenant-aware idempotency/audit/events;
- tenant-aware rate limits;
- data export/delete policies.

Если product остаётся single-organization, этот этап не нужно форсировать.

---

# 12. Phase K — Scale extraction triggers

Не является feature milestone. Это checklist, когда modular monolith может перестать быть достаточным.

Рассматривать выделение отдельных deployables, если измерено:

- Outbox/notification worker требует независимого scale;
- analytics мешает OLTP;
- отдельная команда владеет bounded context;
- release cadence конфликтует;
- security boundary требует isolation;
- DB workload невозможно разумно разделить внутри монолита.

Первый вероятный extraction candidate — worker/analytics, а не Test aggregate CRUD.

---

# 13. Общие gates каждого этапа

Каждая roadmap item считается Done только если:

- domain/application semantics документированы;
- schema migration tested на MariaDB 12.3;
- authorization negative tests есть;
- concurrency/idempotency рассмотрены;
- student correctness boundary сохранён;
- OpenAPI обновлён при public API change;
- `docs/` обновлены;
- full GitHub Actions pipeline green;
- production image `--migrate` green.

# 14. Что намеренно не входит в ближайший roadmap

Без конкретной потребности не планируются:

- Event Sourcing;
- Kafka параллельно RabbitMQ;
- Redis как обязательная инфраструктура;
- отдельная read DB;
- Kubernetes-specific code inside application;
- microservice decomposition;
- generic plugin engine;
- arbitrary scripting inside tests.

Эти технологии могут появиться только после измеренного требования, а не как архитектурная цель сами по себе.