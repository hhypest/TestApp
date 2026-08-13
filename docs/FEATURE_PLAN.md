# Детальный план фич TestApp

> Этот backlog является рабочим планом ветки `beta-ddd`. `DONE` означает реализовано и покрыто repository baseline; `VERIFYING` — automation есть, но обязательный exact-head gate ещё не green; `PLANNED` — согласованный следующий шаг; `DECISION` — требуется отдельное решение.

## Обозначения

Priority:

- **P0** — блокирует production 1.0/security/correctness;
- **P1** — высокая продуктовая ценность после core safety;
- **P2** — расширение/оптимизация;
- **P3** — исследование/дальняя перспектива.

Effort — относительный: `S`, `M`, `L`, `XL`.

---

# A. Уже реализованный core baseline

| ID | Feature | Status | Notes |
|---|---|---|---|
| CORE-001 | Test aggregate lifecycle | DONE | Draft/Published/Archived |
| CORE-002 | Question authoring | DONE | add/update/remove/reorder |
| CORE-003 | Answer option authoring | DONE | add/update/remove/reorder |
| CORE-004 | SingleChoice | DONE | exactly one correct on publish |
| CORE-005 | MultipleChoice | DONE | >=1 correct on publish |
| CORE-006 | Test settings | DONE | pass percentage + time limit |
| CORE-007 | Immutable published revision | DONE | JSON snapshot + version |
| CORE-008 | Exact-set scoring | DONE | full points or zero per question |
| CORE-009 | Test archive | DONE | terminal authoring state |
| CAT-001 | Author catalog | DONE | search/status/paging |
| CAT-002 | Revision list | DONE | immutable metadata |
| ASSIGN-001 | User assignment | DONE | revision-scoped |
| ASSIGN-002 | Group assignment | DONE | dynamic group claim membership |
| ASSIGN-003 | Availability window | DONE | from/until |
| ASSIGN-004 | Attempt limit | DONE | DB-safe start |
| ASSIGN-005 | Cancel assignment | DONE | audit actor/time/reason |
| ASSIGN-006 | Bulk assignment | DONE | <=500, idempotent |
| ASSIGN-007 | Admin assignment queries | DONE | filters/detail/statistics |
| ATT-001 | Start attempt | DONE | target/availability/limit |
| ATT-002 | Answer/clear response | DONE | ownership + revision validation |
| ATT-003 | Submit | DONE | scoring + outcome |
| ATT-004 | Timeout | DONE | score preserved |
| ATT-005 | Automatic expiration | DONE | background worker |
| ATT-006 | Own attempt/result reads | DONE | student-safe |
| RES-001 | Reviewer result list | DONE | test/revision/outcome filters |
| RES-002 | Reviewer result detail | DONE | correctness breakdown |
| DB-001 | PostgreSQL 18 | DONE | runtime + CI version assertion |
| DB-002 | Optimistic concurrency | DONE | aggregate version -> 409 |
| DB-003 | Npgsql schema baseline | DONE | native uuid/timestamptz/jsonb + model snapshot |
| IDEM-001 | Distributed idempotency store | DONE | PostgreSQL advisory lease |
| IDEM-002 | Idempotent publish | DONE | persistent result |
| IDEM-003 | Idempotent assign/bulk | DONE | persistent result |
| IDEM-004 | Idempotent submit | DONE | cached score |
| OUT-001 | Transactional Outbox | DONE | IIntegrationEvent only |
| OUT-002 | Retry/dead-letter | DONE | exponential backoff |
| RMQ-001 | RabbitMQ publisher | DONE | confirms/persistent/mandatory |
| RMQ-002 | RabbitMQ readiness | DONE | connection/channel/exchange |
| SEC-BASE-001 | Keycloak JWT | DONE | sub/roles/groups |
| SEC-BASE-002 | Role policies | DONE | author/admin/reviewer/operations |
| API-BASE-001 | `/api/v1` | DONE | canonical API |
| API-BASE-002 | legacy `/api/*` rewrite | DONE | compatibility |
| API-BASE-003 | OpenAPI endpoint | DONE | `/openapi/v1.json` |
| API-BASE-004 | ProblemDetails | DONE | application + exception errors |
| OBS-001 | Correlation ID | DONE | `X-Correlation-ID` |
| OBS-002 | HTTP audit | DONE | state-changing requests |
| OBS-003 | OpenTelemetry | DONE | ASP.NET/HttpClient/runtime |
| OPS-001 | Docker image | DONE | multi-stage/non-root |
| OPS-002 | Compose stack | DONE | DB/RMQ/Keycloak/OTEL/migrate/API |
| OPS-003 | Migration-only mode | DONE | `--migrate`; DB-only composition остаётся STAB-005 |
| TEST-001 | PostgreSQL integration suite | DONE | real provider |
| TEST-002 | RabbitMQ integration suite | DONE | real broker |
| TEST-003 | production image migration CI | DONE | release-path gate |

---

## Current `0.9.4` stabilization backlog

| ID | Priority | Status | Scope |
|---|---:|---|---|
| STAB-001 | P0 | DONE | actor-scoped StartAttempt replay before mutable assignment checks + cancellation/expiry/group regression tests |
| STAB-002 | P0 | PLANNED | audit stores final handled HTTP status |
| STAB-003 | P0 | PLANNED | cycle-level Outbox/expiration worker recovery |
| STAB-004 | P0 | VERIFYING | diagnostic RabbitMQ capacity probe + full green D6 run |
| STAB-005 | P0 | PLANNED | database-only `--migrate` configuration path |
| STAB-006 | P1 | PLANNED | deterministic timestamp + ID pagination order |
| STAB-007 | P1 | PLANNED | SQL joins/aggregates for reviewer/admin hot queries |

`STAB-004` остаётся `VERIFYING`: после миграции persistence полный HTTP/expiration/Outbox gate должен заново пройти на exact PostgreSQL implementation HEAD.

---

# B. Production hardening baseline

## CFG-001 — Fail-fast Database configuration

- **Priority:** P0
- **Effort:** S
- **Status:** DONE
- **Dependencies:** none

### Implemented scope

- убрать production fallback `testapp/testapp`;
- Development default оставить только явно;
- validated options/startup check.

### Acceptance

- Production without `ConnectionStrings:Database` fails before serving traffic;
- secret не логируется;
- integration test host startup failure.

## CFG-002 — Validate Keycloak configuration

- **Priority:** P0
- **Effort:** S
- **Status:** DONE

### Acceptance

- Production requires non-empty Authority/Audience;
- invalid URL rejected;
- HTTPS metadata remains required outside Development.

## CFG-003 — Typed RabbitMQ/worker options binding

- **Priority:** P1
- **Effort:** S
- **Status:** DONE

Bind/validate:

- RabbitMQ;
- Outbox delivery;
- attempt expiration.

## EDGE-001 — Trusted forwarded headers

- **Priority:** P0
- **Effort:** M
- **Status:** DONE

### Acceptance

- configured KnownProxies/KnownNetworks;
- untrusted `X-Forwarded-For` cannot spoof rate-limit partition;
- scheme/IP tests.

## EDGE-002 — CORS policy

- **Priority:** P0 if browser frontend deployed
- **Effort:** S
- **Status:** DONE

### Acceptance

- explicit origin allow-list;
- no `AllowAnyOrigin + credentials`;
- env-specific config.

## EDGE-003 — HTTPS/HSTS deployment policy

- **Priority:** P0
- **Effort:** S/M
- **Status:** DONE

Document and test ingress termination behavior.

## EDGE-004 — Configurable rate limits

- **Priority:** P0
- **Effort:** M
- **Status:** DONE

### Policies

- general;
- student write;
- reviewer/admin expensive read;
- operations.

### Acceptance

- configuration binding;
- deterministic 429 tests;
- correlation still returned on rejection.

## API-SEC-001 — Production OpenAPI policy

- **Priority:** P0
- **Effort:** S
- **Status:** DONE

Configuration decides public/internal/disabled.

## CI-SEC-001 — Dependency vulnerability gate

- **Priority:** P0
- **Effort:** S/M
- **Status:** DONE

- NuGet vulnerability check;
- fail on high/critical agreed policy.

## CI-SEC-002 — Container image scan/SBOM

- **Priority:** P1
- **Effort:** M
- **Status:** DONE

---

# C. Ownership и authorization

## AUTHZ-001 — Test ownership model

- **Priority:** P0
- **Effort:** L
- **Status:** DONE — selected `OwnerId` model for current single-organization scope

### Options

**A. OwnerId only** — проще для single-organization.

**B. Workspace + membership** — правильнее для multi-team/multi-tenant.

### Minimum acceptance

- every Test has non-null ownership scope;
- creation assigns current actor;
- migration/backfill existing tests;
- indexed SQL filters.

## AUTHZ-002 — Author catalog isolation

- **Priority:** P0
- **Effort:** M
- **Dependencies:** AUTHZ-001
- **Status:** DONE

Author sees only allowed scope; admin behavior explicitly defined.

## AUTHZ-003 — Authoring command ownership

- **Priority:** P0
- **Effort:** M
- **Dependencies:** AUTHZ-001
- **Status:** DONE

Protect rename/settings/question/option/publish/archive.

## AUTHZ-004 — Revision access isolation

- **Priority:** P0
- **Effort:** S/M
- **Dependencies:** AUTHZ-001
- **Status:** DONE

## AUTHZ-005 — Reviewer result isolation

- **Priority:** P0
- **Effort:** M
- **Dependencies:** AUTHZ-001
- **Status:** DONE

Two-author E2E negative test mandatory.

## AUTHZ-006 — Admin scope policy

- **Priority:** P1
- **Effort:** M
- **Status:** DONE — `test-admin` has explicit global scope

Decision: current `test-admin` scope is global. Workspace admin is reconsidered only with a Workspace/Tenant model.

## ID-001 — `(Issuer, Subject)` external identity

- **Priority:** P1; P0 if multi-realm production
- **Effort:** XL
- **Status:** DECISION/PLANNED

### Migration impact

- assignments;
- attempts;
- idempotency;
- audit;
- ownership;
- integration events;
- indexes.

---

# D. API contract maturity

## API-001 — Standard `Idempotency-Key` header

- **Priority:** P0/P1
- **Effort:** M
- **Status:** DONE — header resolver/fingerprint compatibility; see API-009 for zero-length body

### Scope

- publish;
- assign;
- bulk assign;
- start attempt;
- submit.

### Acceptance

- header required for designated commands;
- body key transitional support;
- generated OpenAPI;
- stable validation error.

Current transport limitation: publish/start/submit still require a JSON body (`{}` is sufficient) because their Minimal API body parameter is non-nullable. The key may live only in the header, but a truly empty body is tracked separately as API-009.

## API-002 — Idempotency request fingerprint

- **Priority:** P0/P1
- **Effort:** M
- **Dependencies:** API-001
- **Status:** DONE

Same key + different payload must not silently replay unrelated result.

Store canonical request hash with record.

## API-003 — ETag / `If-Match`

- **Priority:** P1
- **Effort:** M/L
- **Status:** DONE

Expose aggregate version on mutable author/admin resources.

### Acceptance

- stale If-Match -> 412/409 policy documented;
- no blind lost update;
- OpenAPI examples.

## API-004 — Unified request validation

- **Priority:** P1
- **Effort:** M
- **Status:** DONE

Length/range/required/enum validation before handler where transport-specific.

## API-005 — OpenAPI enrichment

- **Priority:** P1
- **Effort:** M
- **Status:** DONE

- descriptions;
- examples;
- policy/security metadata;
- ProblemDetails;
- enum values;
- pagination;
- idempotency.

## API-006 — OpenAPI contract snapshot test

- **Priority:** P1
- **Effort:** S/M
- **Dependencies:** API-005
- **Status:** DONE

Detect accidental breaking changes.

## API-007 — Legacy `/api/*` deprecation

- **Priority:** P2
- **Effort:** S
- **Status:** DONE — lifecycle headers/retirement behavior implemented; rewrite removal remains a future compatibility decision

### Steps

1. response deprecation/sunset headers if desired;
2. client migration;
3. telemetry of legacy usage;
4. remove rewrite in next major/version window.

## API-008 — Stable filter/sort conventions

- **Priority:** P2
- **Effort:** M
- **Status:** PLANNED

Needed before richer catalogs/reporting.

## API-009 — True empty-body header-only commands

- **Priority:** P0/P1
- **Effort:** S/M
- **Status:** PLANNED
- **Dependencies:** API-001

Publish/start/submit должны принимать zero-length body при валидном `Idempotency-Key` header. Нужны реальные empty-body HTTP tests и соответствующий OpenAPI contract.

---

# E. Operational reliability

## OPS-010 — Backup policy

- **Priority:** P0
- **Effort:** M
- **Status:** DONE — repository logical baseline; provider PITR/scheduling remain deployment-owned

Repository baseline defines engineering RPO/RTO and portable retention; concrete schedule/encrypted storage/PITR remain deployment-owned.

## OPS-011 — Automated restore verification

- **Priority:** P0
- **Effort:** M/L
- **Dependencies:** OPS-010
- **Status:** DONE

CI restores each generated backup to an isolated database and verifies schema, migration history and business marker.

## OPS-012 — Audit retention cleanup

- **Priority:** P1
- **Effort:** M
- **Status:** DONE

Batch delete/archive with index-friendly range.

## OPS-013 — Idempotency retention cleanup

- **Priority:** P1
- **Effort:** M
- **Status:** DONE

Retention must exceed maximum retry window/client guarantees.

## OPS-014 — Processed Outbox retention

- **Priority:** P1
- **Effort:** M
- **Status:** DONE

Do not delete pending/dead-letter rows blindly.

## OPS-015 — Dead-letter requeue API

- **Priority:** P1
- **Effort:** M
- **Status:** DONE

### Acceptance

- admin-only;
- audit;
- only dead-letter rows;
- reset attempt schedule explicitly;
- concurrency-safe;
- no payload edit.

## OPS-016 — Dead-letter acknowledge/drop

- **Priority:** P2
- **Effort:** M
- **Status:** DONE — explicit audited `discard` terminal state selected

Needed only if operations requires permanent suppression state.

## OBS-010 — Metrics for Outbox lag

- **Priority:** P1
- **Effort:** M
- **Status:** DONE

## OBS-011 — Attempt expiration lag metric

- **Priority:** P1
- **Effort:** S/M
- **Status:** DONE

## OBS-012 — API SLO dashboard

- **Priority:** P1
- **Effort:** M
- **Status:** PLANNED — application metrics/SLO contract done; backend dashboard is deployment-owned

## OBS-013 — Alerts

- **Priority:** P1
- **Effort:** M
- **Dependencies:** OBS-010..012
- **Status:** PLANNED — thresholds/runbook documented; routes and staging drill remain

Alert on:

- readiness;
- 5xx;
- latency;
- dead letters;
- Outbox lag;
- DB/RMQ connectivity.

## PERF-001 — Load test baseline

- **Priority:** P0 before sized production launch
- **Effort:** L
- **Status:** VERIFYING

Scenarios documented in `TESTING.md`.

---

# F. Integration event catalog

## EVT-001 — `TestRevisionPublishedV1`

- **Priority:** P1 when consumer exists
- **Effort:** M
- **Status:** DECISION

Must be explicit `IIntegrationEvent`; no raw aggregate serialization.

## EVT-002 — `TestAssignedV1`

- **Priority:** P1 when notification/integration consumer exists
- **Effort:** M
- **Status:** DECISION

## EVT-003 — `AttemptCompletedV1`

- **Priority:** P1 for analytics/notifications
- **Effort:** M
- **Status:** DECISION

Prefer stable external completion contract rather than exposing internal submitted/timed-out event classes.

## EVT-004 — Integration event schema version policy

- **Priority:** P1 before first external consumer
- **Effort:** S/M
- **Status:** PLANNED

## EVT-005 — Consumer dedup reference implementation/test harness

- **Priority:** P2
- **Effort:** M
- **Status:** PLANNED

---

# G. Authoring productivity

## AUTHOR-001 — Draft validation endpoint

- **Priority:** P1
- **Effort:** M
- **Status:** PLANNED

Returns publication problems without changing status.

## AUTHOR-002 — Clone test

- **Priority:** P1
- **Effort:** M
- **Status:** PLANNED

Creates new Draft with copied content and new IDs according to explicit policy.

## AUTHOR-003 — Create draft from published revision

- **Priority:** P1
- **Effort:** M
- **Status:** PLANNED

Useful for branching/copying historical version.

## AUTHOR-004 — Tags

- **Priority:** P1
- **Effort:** M
- **Status:** DECISION/PLANNED

Need normalized tag storage/index/filtering.

## AUTHOR-005 — Category/subject

- **Priority:** P1
- **Effort:** M
- **Status:** DECISION

## AUTHOR-006 — Rich catalog filters/sort

- **Priority:** P1
- **Effort:** M
- **Dependencies:** AUTHOR-004/005 as selected

## AUTHOR-007 — Versioned JSON export

- **Priority:** P1
- **Effort:** M
- **Status:** PLANNED

Do not export internal EF entity schema directly.

## AUTHOR-008 — Versioned JSON import

- **Priority:** P1
- **Effort:** L
- **Dependencies:** AUTHOR-007

Full validation before persistence; no partial import.

## AUTHOR-009 — CSV import/export simple-choice

- **Priority:** P2
- **Effort:** M
- **Status:** DECISION

Only if business users need spreadsheet workflow.

## AUTHOR-010 — Question bank

- **Priority:** P1/P2
- **Effort:** XL
- **Status:** DECISION

Key decision: copy vs live reference. Recommendation: authoring may reference/copy, published revision always snapshots.

---

# H. Assessment behavior

## ASMT-001 — Shuffle answer options

- **Priority:** P1
- **Effort:** M
- **Status:** DECISION

Presentation order should be deterministic/stored per attempt if result review must reproduce UI.

## ASMT-002 — Shuffle questions

- **Priority:** P1
- **Effort:** M
- **Status:** DECISION

## ASMT-003 — Question pools

- **Priority:** P1/P2
- **Effort:** L/XL
- **Status:** DECISION

Attempt must snapshot selected question IDs.

## ASMT-004 — Partial scoring strategy

- **Priority:** P1
- **Effort:** L
- **Status:** DECISION

Revision snapshots scoring strategy/version.

## ASMT-005 — Negative marking

- **Priority:** P2
- **Effort:** M/L
- **Status:** DECISION

Requires explicit minimum score/rounding policy.

## ASMT-006 — Numeric answer

- **Priority:** P1
- **Effort:** L
- **Status:** DECISION

Need tolerance/normalization rules.

## ASMT-007 — Short text auto-match

- **Priority:** P2
- **Effort:** L
- **Status:** DECISION

Normalization/localization complexity.

## ASMT-008 — FreeText manual grading

- **Priority:** P1/P2
- **Effort:** XL
- **Status:** DECISION

Requires new attempt grading lifecycle and reviewer write permissions.

## ASMT-009 — Ordering question

- **Priority:** P2
- **Effort:** L
- **Status:** DECISION

## ASMT-010 — Matching question

- **Priority:** P2
- **Effort:** XL
- **Status:** DECISION

## ASMT-011 — Attachments/images

- **Priority:** P2
- **Effort:** XL
- **Status:** DECISION

Requires object storage, scanning, signed access, content security.

## ASMT-012 — Rich text/Markdown

- **Priority:** P1/P2
- **Effort:** M/L
- **Status:** DECISION

Requires rendering/sanitization policy in frontend.

---

# I. Attempt UX/lifecycle

## ATT-010 — Attempt presentation DTO

- **Priority:** P1 when frontend starts
- **Effort:** M
- **Status:** PLANNED

Current AttemptView focuses on responses, not complete student question presentation. Add student-safe revision/attempt presentation without correctness.

## ATT-011 — Resume active attempt

- **Priority:** P1
- **Effort:** M
- **Status:** PLANNED

Current detail can support basis; define UI/API semantics for finding active attempt.

## ATT-012 — Explicit attempt start metadata

- **Priority:** P2
- **Effort:** S/M
- **Status:** DECISION

Client/device metadata only if privacy/business need.

## ATT-013 — Pause/resume clock

- **Priority:** P3
- **Effort:** XL
- **Status:** DECISION

Not compatible with current simple deadline semantics without domain redesign.

## ATT-014 — Autosave batching

- **Priority:** P2
- **Effort:** M
- **Status:** DECISION

Current per-question PUT is already retryable via aggregate concurrency but not idempotency-keyed.

## ATT-015 — Attempt abandon

- **Priority:** P2
- **Effort:** M
- **Status:** DECISION

Need clear impact on attempt limit/result statistics.

---

# J. Assignment features

## ASN-010 — Assignment template

- **Priority:** P1
- **Effort:** L
- **Status:** DECISION

Reusable config for revision/window/limit/targets.

## ASN-011 — Campaign/batch entity

- **Priority:** P1/P2
- **Effort:** L
- **Status:** DECISION

Useful if bulk assignments need lifecycle/report as one unit.

## ASN-012 — Scheduled future assignments

- **Priority:** P1
- **Effort:** S/M
- **Status:** PLANNED

Current `AvailableFrom` already supports future availability; feature mainly adds UX/query/filter/scheduling semantics.

## ASN-013 — Assignment duplicate prevention policy

- **Priority:** P1
- **Effort:** M
- **Status:** DECISION

Current system allows multiple assignments of same revision/target with distinct IDs. Decide whether that is intentional.

## ASN-014 — Group membership semantics

- **Priority:** P1
- **Effort:** L
- **Status:** DECISION

Choose:

- dynamic at access time (current);
- snapshot membership at assignment time.

## ASN-015 — Reassignment after completion

- **Priority:** P1
- **Effort:** M
- **Status:** DECISION

Clarify whether new assignment or attempt-limit change is canonical.

## ASN-016 — Assignment reminder metadata

- **Priority:** P2
- **Effort:** M
- **Dependencies:** notification integration

---

# K. Reporting/analytics

## REP-010 — Test summary dashboard

- **Priority:** P1
- **Effort:** L
- **Status:** PLANNED

Metrics:

- attempts;
- completion rate;
- pass rate;
- average score;
- average duration.

## REP-011 — Revision comparison

- **Priority:** P2
- **Effort:** L
- **Status:** DECISION

Never mix results across revisions without explicit grouping.

## REP-012 — Question difficulty

- **Priority:** P1/P2
- **Effort:** L
- **Status:** PLANNED after sufficient data

Group by `(RevisionId, QuestionId)`.

## REP-013 — Answer distribution

- **Priority:** P2
- **Effort:** L
- **Status:** PLANNED

Reviewer/admin only; careful correctness exposure.

## REP-014 — CSV result export

- **Priority:** P1
- **Effort:** M
- **Status:** PLANNED

Authorization + audit + streaming response.

## REP-015 — Analytics projection tables

- **Priority:** P2
- **Effort:** XL
- **Status:** DECISION

Only after query/load measurements.

## REP-016 — External warehouse

- **Priority:** P3
- **Effort:** XL
- **Status:** DECISION

Requires integration event catalog and real analytics scale need.

---

# L. Notifications/integrations

## NOTIF-001 — Assignment-created notification contract

- **Priority:** P1/P2
- **Effort:** M
- **Dependencies:** EVT-002
- **Status:** DECISION

## NOTIF-002 — Deadline reminder scheduler

- **Priority:** P2
- **Effort:** L
- **Status:** DECISION

Should produce event/job, not send email inside transaction.

## NOTIF-003 — Completion/result notification

- **Priority:** P2
- **Effort:** M/L
- **Dependencies:** EVT-003

## INT-001 — Webhook delivery adapter

- **Priority:** P2
- **Effort:** XL
- **Status:** DECISION

Only if external consumers cannot consume RabbitMQ.

## INT-002 — Webhook signatures/retry

- **Priority:** P2
- **Effort:** L
- **Dependencies:** INT-001

---

# M. Workspace/multi-tenancy

## TEN-001 — Workspace aggregate

- **Priority:** P1/P2 depending deployment
- **Effort:** XL
- **Status:** DECISION

## TEN-002 — Workspace membership

- **Priority:** same
- **Effort:** XL
- **Dependencies:** TEN-001, ID-001 likely

## TEN-003 — Workspace-scoped roles

- **Priority:** same
- **Effort:** L

## TEN-004 — Workspace data isolation

- **Priority:** P0 if multi-tenant
- **Effort:** XL

Every read/write query must become workspace-aware and indexed.

## TEN-005 — Tenant-aware audit/Outbox

- **Priority:** P1
- **Effort:** L

---

# N. Frontend/client readiness

## UX-001 — Student test-taking presentation API

- **Priority:** P1
- **Effort:** M/L
- **Status:** PLANNED when frontend begins

Needs student-safe question/options DTO + current responses/deadline.

## UX-002 — Author editing API ergonomics

- **Priority:** P1
- **Effort:** M
- **Status:** PLANNED

May include batch reorder/update to reduce chatty UI.

## UX-003 — Typed client SDK generation

- **Priority:** P2
- **Effort:** M
- **Dependencies:** stable enriched OpenAPI

## UX-004 — Frontend application

- **Priority:** product-dependent
- **Effort:** XL
- **Status:** DECISION

Not part of current repository baseline.

---

# O. Developer experience

## DEV-001 — Central package management

- **Priority:** P2
- **Effort:** S/M
- **Status:** PLANNED

Introduce `Directory.Packages.props` if package count continues growing.

## DEV-002 — Standard EF migration tooling/snapshot

- **Priority:** P1
- **Effort:** M
- **Status:** DONE

Repeatable `dotnet ef` workflow реализован через `AppDbContextDesignFactory`; PostgreSQL baseline имеет designer metadata и `AppDbContextModelSnapshot`.

## DEV-003 — Architecture dependency tests

- **Priority:** P1
- **Effort:** M
- **Status:** DONE — assembly reference direction baseline

Automate Domain-no-EF/API and layer reference constraints.

## DEV-004 — Formatter/analyzer CI

- **Priority:** P2
- **Effort:** S/M
- **Status:** PLANNED

## DEV-005 — Conventional changelog/release notes

- **Priority:** P1 before 1.0
- **Effort:** S
- **Status:** PLANNED

---

# P. Recommended implementation order

Следующие фичи выполнять именно в этом порядке, если business priority не меняется:

```text
1  STAB-001..005 + API-009 + full green PERF-001/D6
2  STAB-006/007 + error/value-object invariant normalization
3  1.0 release rehearsal: migration/restore/alerts/rollback/API freeze
4  ATT-010/011 + UX-001 student presentation/resume
5  AUTHOR-001/002/003 + selected tags/catalog + versioned JSON import/export
6  reporting hot paths REP-010/014 after SQL scalability work
7  EVT catalog only when first real consumer appears
8  assignment orchestration/notifications selected by product need
9  advanced assessment features one versioned vertical slice at a time
```

## Почему так

- ownership/security/API/operational repository baseline уже реализованы;
- оставшиеся correctness gaps нельзя переносить за 1.0 freeze;
- student presentation завершает текущий end-to-end product flow раньше новых question types;
- backup/restore/retention требуют deployment rehearsal поверх уже существующей automation;
- integration events должны появляться от конкретного consumer need;
- advanced question types существенно расширяют domain и должны строиться на стабильной платформе.

---

# Q. Definition of Ready для feature

Feature можно брать в реализацию, когда известны:

- actor/use case;
- business outcome;
- authorization scope;
- aggregate ownership/state changes;
- API contract;
- persistence impact;
- retry/idempotency/concurrency semantics;
- sensitive data impact;
- test plan;
- migration/backfill plan, если schema меняется.

# R. Definition of Done для feature

- code реализован по layer boundaries;
- domain invariants unit-tested;
- PostgreSQL integration tests при persistence change;
- HTTP E2E happy + negative auth;
- concurrency/idempotency test если применимо;
- student correctness boundary проверен;
- OpenAPI актуален;
- `docs/` обновлены;
- full CI green;
- production image builds;
- production image `--migrate` succeeds;
- feature status в этом файле изменён на `DONE`.
