# Дорожная карта развития TestApp

> Baseline: `beta-ddd`, 2026-08-13. Roadmap задаёт последовательность и acceptance gates, а exact-head evidence определяется GitHub Actions ветки.

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

Новый этап считается завершённым только после зелёных automated gates и синхронизации документации.

---

# 1. `0.8.x` — Core architecture baseline

**Status: DONE**

Реализовано:

- modular monolith / Clean Architecture / DDD / CQRS;
- Test aggregate и controlled authoring;
- immutable PublishedTestRevision;
- SingleChoice / MultipleChoice publication validation;
- exact-set scoring;
- user/group assignments, windows, attempt limits, bulk assignment;
- attempt start/answer/clear/submit/timeout;
- reviewer/admin/student read models;
- PostgreSQL 18 runtime/CI baseline;
- Keycloak authentication/roles/groups;
- transactional Outbox + RabbitMQ transport;
- Docker/Compose + migration-only startup;
- real PostgreSQL/RabbitMQ integration tests.

---

# 2. `0.9.0` — Phase A: Production configuration & edge security

**Priority: P0**
**Status: DONE**

Реализовано:

- fail-fast production configuration;
- отсутствие production fallback DB credentials;
- `--migrate` вместо startup migrations outside Development;
- trusted reverse-proxy configuration;
- explicit CORS allow-list;
- configurable HTTPS/HSTS policy;
- security headers + disabled Kestrel server banner;
- class-based rate limiting;
- production OpenAPI disabled by default / optionally admin protected;
- high/critical NuGet audit gate.

**Exit gate: PASSED.**

---

# 3. `0.9.1` — Phase B: Resource ownership

**Priority: P0**
**Status: DONE**

Текущая single-organization модель:

```text
Test.OwnerId = Keycloak sub создавшего автора
```

Реализовано:

- immutable mandatory `OwnerId`;
- owner checks в Application write boundary;
- owner SQL filtering для catalog/editor/revisions/reviewer list/detail;
- `test-admin` global scope;
- safe legacy backfill `__legacy_admin_only__`;
- cross-author E2E, включая reviewer correctness isolation.

**Exit gate: PASSED.**

---

# 4. `0.9.2` — Phase C: API contract maturity

**Priority: P0/P1**  
**Status: DONE**

## C1. Standard Idempotency-Key — DONE

- primary retry contract: `Idempotency-Key` header;
- transitional body compatibility;
- header/body mismatch -> `400 idempotency.key_mismatch`;
- canonical SHA-256 request fingerprint;
- same key + different logical request -> `409 idempotency.key_reused`;
- distributed PostgreSQL advisory lock;
- publish/single assignment/bulk assignment/submit covered.

Phase C завершила header resolution/fingerprint contract. Настоящий zero-length body для no-payload publish/start/submit закрыт в `0.9.4` (API-009): request DTO параметр nullable, key только в header достаточен.

## C2. HTTP optimistic concurrency — DONE

- editor returns `ConcurrencyVersion` + strong `ETag`;
- Test mutations require `If-Match`;
- missing -> `428`;
- malformed/weak/wildcard -> `400`;
- stale -> `412`;
- DB race remains protected by EF concurrency token -> `409`.

## C3. Validation normalization — DONE

- malformed route/query/body -> stable `400 request.invalid`;
- DataAnnotations-based DTO boundary validation;
- undefined enum protection;
- stable `400 request.validation` + structured errors;
- domain max-length/type invariants aligned with persistence.

## C4. OpenAPI quality — DONE

- Bearer security scheme;
- stable operation IDs, summaries/descriptions;
- authorization requirements;
- documented Idempotency-Key / If-Match / ETag;
- ProblemDetails responses;
- enum/examples metadata;
- serialized OpenAPI contract test.

## C5. API version lifecycle — DONE

Canonical path:

```text
/api/v1/*
```

Legacy `/api/*` compatibility является управляемым lifecycle layer:

- configurable enable/disable;
- RFC-style `Deprecation` header;
- optional `Sunset`;
- retirement -> `410 api.version.retired`;
- lifecycle boundary E2E.

**Exit gate Phase C: PASSED.**

---

# 5. `0.9.3` — Phase D: Operational reliability

**Priority: P0/P1**  
**Status: DONE — D1..D6 PASSED**

## D1. Backup / restore — DONE

Реализовано:

- logical PostgreSQL backup script;
- PostgreSQL custom-format archive + SHA-256;
- restore verification в отдельной database;
- schema/table count + EF migration history verification;
- application data marker verification;
- CI restore drill;
- documented baseline RPO/RTO expectations.

Logical backup является portability/recovery baseline; более жёсткий production RPO требует provider-native snapshot/WAL/PITR.

## D2. Retention / cleanup — DONE

Реализовано:

- typed retention configuration;
- bounded batch deletion;
- DB-scoped advisory lock;
- audit retention;
- idempotency retention;
- processed Outbox retention;
- required cleanup indexes;
- PostgreSQL safety regression tests.

**Не удаляются автоматически:** pending, retrying и active dead-letter Outbox rows.

## D3. Dead-letter management — DONE

Реализовано:

- admin-only safe detail без payload;
- explicit requeue;
- explicit discard как terminal state, не physical delete;
- mandatory reason;
- atomic manager audit: actor/action/reason/correlation;
- shared lock boundary с publisher;
- API/PostgreSQL/OpenAPI E2E.

## D4. SLO / operational metrics — DONE

Реализовано:

- `TestApp.Operations` Meter;
- Outbox publish outcomes / delivery lag;
- active dead-letter actions;
- Outbox pending/dead-letter/oldest-age gauges;
- attempt overdue/lag gauges;
- expiration outcomes;
- retention deleted counter;
- bounded API exception categories;
- OpenTelemetry registration;
- `docs/SLO_ALERTS.md` с начальным alerting contract.

## D5. Security / supply-chain CI — DONE

Отдельный `security` workflow:

- repository secret scan;
- production-image HIGH/CRITICAL vulnerability gate;
- CycloneDX image SBOM;
- SBOM artifact retention;
- существующий NuGet high/critical gate остаётся в основном pipeline.

## D6. Load / capacity regression — DONE

Добавлено:

- `.github/workflows/performance.yml`;
- authenticated k6 через реальный local Keycloak;
- production-shaped stack: API + PostgreSQL 18 + RabbitMQ + Keycloak;
- performance-only rate-limit ceilings;
- scenario-specific p95 gates;
- artifact `testapp-capacity-results`.

HTTP critical scenarios:

1. simultaneous attempt starts;
2. answer write burst;
3. bulk assignment creation;
4. reviewer pagination.

Worker critical scenarios:

5. expiration storm — overdue backlog должен drain до zero <= 30 s;
6. Outbox backlog recovery — 100 synthetic messages должны пройти real RabbitMQ transport и стать processed <= 30 s.

Подробности и thresholds: `docs/PERFORMANCE.md`.

**D6 exit gate:** первый полный `performance` workflow green на exact head + сохранённые artifacts.

**Phase D exit gate:** D6 green + основной `dotnet` и `security` workflows green на совместимом head.

### Verification evidence

PostgreSQL implementation commit `9916b98` прошёл полный `performance` run #9: 8771/8771 checks, HTTP failure rate 0, все scenario p95 ниже thresholds, expiration 1247 -> 0 за 14 s и Outbox 100 -> 0 за 1 s. `dotnet` и `security` на том же commit также green; D6 и Phase D exit gates выполнены.

---

# 6. `0.9.4` — Phase D7: Correctness/stabilization fixes

**Priority: P0**
**Status: IN PROGRESS**

Перед 1.0 RC необходимо закрыть findings текущего exact-head review:

1. **DONE:** actor-scoped `StartAttempt` replay до повторной проверки mutable assignment availability/group membership, с regression tests для cancellation/expiry/group change и нового key;
2. **DONE:** audit сохраняет итоговый HTTP status после exception mapping, включая handled `400/409`, а не промежуточный `500`;
3. **DONE:** Outbox/expiration hosted workers переживают transient cycle-level DB/query/lock failures;
4. **DONE:** publish/start/submit принимают настоящий zero-length body при валидном `Idempotency-Key` header;
5. **DONE:** `--migrate` загружает только database-required configuration;
6. **DONE:** RabbitMQ 4.3-compatible diagnostic capacity probe и полный green D6 run;
7. documentation source of truth должна оставаться синхронизированной с code/tests/CI.

**Exit gate:** regression tests для пунктов 1–5, полный `dotnet`/`security`/`performance` green на одном implementation HEAD, сохранённый D6 artifact и отсутствие открытых P0 correctness findings.

---

# 7. `1.0.0` — Phase E: Stabilization / production release candidate

**Priority: P0**  
**Status: NEXT AFTER PHASE D**

До freeze 1.0 требуется:

1. public API v1 contract freeze;
2. zero open P0 security/data-isolation defects;
3. repeat owner-isolation verification;
4. migration verification from supported previous schema;
5. no production dev credentials/fallbacks;
6. demonstrated backup/restore;
7. repeated green `dotnet`, `security`, `performance` pipelines;
8. staging capacity target defined and met;
9. integration-event catalog explicitly documented;
10. release notes + rollback/forward-fix runbook;
11. dependency/container/SBOM evidence attached to release process;
12. SLO dashboards/alerts exercised against staging;
13. **DONE:** deterministic pagination order (`timestamp + ID`) на всех paged read models;
14. **DONE:** reviewer/admin hot queries выполняют joins/aggregates в SQL без high-cardinality materialization;
15. **DONE:** domain/application error и value-object invariants имеют единый ожидаемый failure contract (STAB-008, ADR-026).

**1.0 definition:** production-safe core assessment workflow, а не максимальное число типов вопросов.

---

# 8. `1.1.x` — Phase F: Student journey & authoring productivity

**Priority: P1**

## F0. Student-safe attempt presentation/resume

Первая product vertical после 1.0:

- question/option presentation из immutable attempt revision;
- saved responses, status и deadline;
- запрет `IsCorrect`/answer-key leakage;
- ownership/OpenAPI/contract tests.

## F1. Clone test / draft from revision

Создание нового working test из immutable revision без ручного копирования.

## F2. Tags / categories / enhanced search

- tags;
- category/subject;
- indexed catalog filters.

## F3. Question bank — product decision required

Если подтверждён reuse use case:

- отдельный QuestionBankItem aggregate/read model;
- explicit copy/reference semantics;
- PublishedTestRevision всё равно snapshot-ит content.

## F4. Import / export

Начать с versioned JSON contract; CSV только для ограниченных choice scenarios.

## F5. Draft validation endpoint

Получение publication errors без state mutation.

---

# 9. `1.2.x` — Phase G: Advanced assessment behavior

**Priority: P1/P2**

## G1. Randomization

- question order;
- option order;
- deterministic attempt seed;
- historical presentation order reproducibility.

## G2. Question pools

Attempt snapshot выбранных question IDs + stable scoring maximum.

## G3. Scoring strategies

Explicit versioned strategy, например:

- ExactSet;
- PartialPositive;
- будущие custom strategies.

## G4. New question types

Рекомендуемая последовательность:

1. Numeric / short deterministic answer;
2. FreeText + manual grading;
3. Ordering;
4. Matching;
5. rich content / attachments.

Каждый тип получает собственную model/validation/scoring semantics; универсальный opaque JSON answer blob не вводится.

## G5. Manual grading

Потребуется lifecycle:

```text
Submitted -> AwaitingReview -> Graded
```

и разделение auto score / final score.

---

# 10. `1.3.x` — Phase H: Assignment orchestration & notifications

**Priority: P1/P2**

- reusable assignment campaigns/templates;
- scheduling/activation UX;
- business integration events: assignment created, deadline approaching, result completed;
- optional notification consumer outside core transaction;
- explicit decision: dynamic group membership vs snapshot membership at assignment time.

---

# 11. `1.4.x` — Phase I: Reporting & analytics

**Priority: P1/P2**

- completion/pass rate;
- average/median/time-to-complete;
- question difficulty by `(RevisionId, QuestionId)`;
- authorized CSV/JSON export with audit;
- projection/materialized analytics only after OLTP impact is measured.

---

# 12. `2.x` / business-triggered — Phase J: Workspace / multi-tenant evolution

Не форсируется без требования нескольких организаций/isolated workspaces.

При необходимости:

- Workspace/Tenant aggregate;
- memberships + workspace roles;
- workspace-scoped tests/assignments/results;
- `(Issuer, Subject)` identity;
- tenant-aware idempotency/audit/events/rate limits;
- export/delete policies.

---

# 13. Phase K: Scale extraction triggers

Microservices не являются roadmap milestone сами по себе.

Рассматривать extraction только после измеренного pressure:

- worker требует независимого scaling;
- analytics мешает OLTP;
- отдельный security boundary;
- независимая команда/релизный цикл;
- DB workload невозможно разумно разделить внутри modular monolith.

Вероятные первые candidates: worker/analytics, а не Test CRUD.

---

# 14. Общие gates

Каждый пункт считается DONE только если применимо выполнены:

- Domain/Application semantics завершены;
- authorization/data-isolation проверены;
- PostgreSQL migration path проверен;
- automated tests добавлены;
- public HTTP/OpenAPI contract синхронизирован;
- production image собирается;
- migration smoke зелёный;
- operational side effects наблюдаемы;
- документация обновлена;
- relevant GitHub Actions workflows зелёные на exact implementation head.
