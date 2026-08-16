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
| CORE-001 | Test aggregate lifecycle | ГОТОВО | Draft/Published/Archived |
| CORE-002 | Question authoring | ГОТОВО | add/update/remove/reorder |
| CORE-003 | Answer option authoring | ГОТОВО | add/update/remove/reorder |
| CORE-004 | SingleChoice | ГОТОВО | exactly one correct on publish |
| CORE-005 | MultipleChoice | ГОТОВО | >=1 correct on publish |
| CORE-006 | Test settings | ГОТОВО | pass percentage + time limit |
| CORE-007 | Immutable published revision | ГОТОВО | JSON snapshot + version |
| CORE-008 | Exact-set scoring | ГОТОВО | full points or zero per question |
| CORE-009 | Test archive | ГОТОВО | terminal authoring state |
| CAT-001 | Author catalog | ГОТОВО | search/status/paging |
| CAT-002 | Revision list | ГОТОВО | immutable metadata |
| ASSIGN-001 | User assignment | ГОТОВО | revision-scoped |
| ASSIGN-002 | Group assignment | ГОТОВО | dynamic group claim membership |
| ASSIGN-003 | Availability window | ГОТОВО | from/until |
| ASSIGN-004 | Attempt limit | ГОТОВО | DB-safe start |
| ASSIGN-005 | Cancel assignment | ГОТОВО | audit actor/time/reason |
| ASSIGN-006 | Bulk assignment | ГОТОВО | <=500, idempotent |
| ASSIGN-007 | Admin assignment queries | ГОТОВО | filters/detail/statistics |
| ATT-001 | Start attempt | ГОТОВО | target/availability/limit |
| ATT-002 | Answer/clear response | ГОТОВО | ownership + revision validation |
| ATT-003 | Submit | ГОТОВО | scoring + outcome |
| ATT-004 | Timeout | ГОТОВО | score preserved |
| ATT-005 | Automatic expiration | ГОТОВО | background worker |
| ATT-006 | Own attempt/result reads | ГОТОВО | student-safe |
| RES-001 | Reviewer result list | ГОТОВО | test/revision/outcome filters |
| RES-002 | Reviewer result detail | ГОТОВО | correctness breakdown |
| DB-001 | PostgreSQL 18 | ГОТОВО | runtime + CI version assertion |
| DB-002 | Optimistic concurrency | ГОТОВО | aggregate version -> 409 |
| DB-003 | Npgsql schema baseline | ГОТОВО | native uuid/timestamptz/jsonb + model snapshot |
| IDEM-001 | Distributed idempotency store | ГОТОВО | PostgreSQL advisory lease |
| IDEM-002 | Idempotent publish | ГОТОВО | persistent result |
| IDEM-003 | Idempotent assign/bulk | ГОТОВО | persistent result |
| IDEM-004 | Idempotent submit | ГОТОВО | cached score |
| OUT-001 | Transactional Outbox | ГОТОВО | IIntegrationEvent only |
| OUT-002 | Retry/dead-letter | ГОТОВО | exponential backoff |
| RMQ-001 | RabbitMQ publisher | ГОТОВО | confirms/persistent/mandatory |
| RMQ-002 | RabbitMQ readiness | ГОТОВО | connection/channel/exchange |
| SEC-BASE-001 | Keycloak JWT | ГОТОВО | sub/roles/groups |
| SEC-BASE-002 | Role policies | ГОТОВО | author/admin/reviewer/operations |
| API-BASE-001 | `/api/v1` | ГОТОВО | canonical API |
| API-BASE-002 | legacy `/api/*` rewrite | ГОТОВО | compatibility |
| API-BASE-003 | OpenAPI endpoint | ГОТОВО | `/openapi/v1.json` |
| API-BASE-004 | ProblemDetails | ГОТОВО | application + exception errors |
| OBS-001 | Correlation ID | ГОТОВО | `X-Correlation-ID` |
| OBS-002 | HTTP audit | ГОТОВО | state-changing requests |
| OBS-003 | OpenTelemetry | ГОТОВО | ASP.NET/HttpClient/runtime |
| OPS-001 | Docker image | ГОТОВО | multi-stage/non-root |
| OPS-002 | Compose stack | ГОТОВО | DB/RMQ/Keycloak/OTEL/Prometheus/Grafana/migrate/API |
| OPS-003 | Migration-only mode | ГОТОВО | `--migrate`; database-only composition (STAB-005) |
| TEST-001 | PostgreSQL integration suite | ГОТОВО | real provider |
| TEST-002 | RabbitMQ integration suite | ГОТОВО | real broker |
| TEST-003 | production image migration CI | ГОТОВО | release-path gate |
| TEST-004 | Core/Domain/Application unit suites | ГОТОВО | 267 tests, 90.3% line coverage (`0.9.5`) |

---

## Current `0.9.4` stabilization backlog

| ID | Priority | Status | Scope |
|---|---:|---|---|
| STAB-001 | P0 | ГОТОВО | actor-scoped StartAttempt replay before mutable assignment checks + cancellation/expiry/group regression tests |
| STAB-002 | P0 | ГОТОВО | audit wraps exception mapping + response/audit equality tests for 400/409/412/500 |
| STAB-003 | P0 | ГОТОВО | cycle-level Outbox/expiration worker recovery |
| STAB-004 | P0 | ГОТОВО | RabbitMQ 4.3-compatible diagnostic probe + green D6 on `9916b98` |
| STAB-005 | P0 | ГОТОВО | database-only `--migrate` configuration path |
| STAB-006 | P1 | ГОТОВО | deterministic timestamp + ID pagination order |
| STAB-007 | P1 | ГОТОВО | SQL joins/aggregates for reviewer/admin hot queries |
| STAB-008 | P1 | ГОТОВО | unified domain/application failure contract: `Result<T, DomainError>` for reachable business rules instead of raw exceptions |

`STAB-002` закрыт перестановкой middleware boundary: correlation/audit выполняется снаружи exception handler и наблюдает уже обработанный response. Regression suite проверяет равенство response/audit status для binding `400`, concurrency `409`, precondition `412` и unhandled `500`.

`STAB-004` закрыт: полный HTTP/expiration/Outbox gate прошёл на PostgreSQL implementation commit `9916b98`; exact-head `dotnet` и `security` также green.

`STAB-005` закрыт: composition root (`Program.cs`) при `--migrate` загружает только `RuntimeConfiguration.LoadDatabase` и пропускает Keycloak/RabbitMQ/attempt-expiration/rate-limiting/OpenAPI/CORS/transport-security/reverse-proxy loaders и связанные DI-регистрации. `compose.yaml` сервис `migrate` и CI-шаг "Apply migrations from production image" передают только `ConnectionStrings:Database`/`Database:ApplyMigrationsOnStartup`. Проверено локально: production build запускает `--migrate` (exit 0, миграция применяется) без единой Keycloak/RabbitMQ/CORS переменной; полный `dotnet test` (18 unit + 69 integration) green.

`STAB-003` закрыт: `OutboxProcessor`/`OverdueAttemptProcessor` оборачивают каждый poll cycle в try/catch (`RunCycleAsync`) — cycle-level failure (напр. transient DB outage при открытии scope) логируется и worker продолжает на следующий `PeriodicTimer` tick вместо fault хостового `BackgroundService` (default `BackgroundServiceExceptionBehavior.StopHost` иначе останавливает весь API process). `WorkerCycleResilienceTests.cs` симулирует однократный сбой `IServiceScopeFactory.CreateScope()` и проверяет: (1) recovery — сообщение/attempt обрабатывается на следующем cycle; (2) `ExecuteTask.IsFaulted == false`. Оба теста подтверждённо red без fix (revert проверен вручную) и green с ним; прямые вызовы `ProcessBatchAsync`/`DrainAvailableAsync` (existing tests) продолжают бросать исключения как раньше — swallow только на уровне hosted-service cycle.

`STAB-006` закрыт: все paged read models с offset pagination (`AssignmentAdminQueries.GetAssignmentsAsync`/`GetAssignmentAttemptsAsync`, `AssignmentReadModelQueries.GetAssignmentsAsync`, `AttemptReadModelQueries.GetAttemptsAsync`, `ReviewerReadModelQueries.GetReviewerResultsAsync`) добавили `.ThenByDescending(x => x.Id)` вслед за primary timestamp order — та же схема, что уже применялась в `TestCatalogQueries.GetTestsAsync` (`.ThenBy(x => x.Id)`). `AdminReadModelQueryTests.cs` проверяет, что полный обход страниц через записи с одинаковым `AssignedAt`/`StartedAt` возвращает каждую запись ровно один раз без дублей/пропусков.

`STAB-007` закрыт: `AssignmentAdminQueries.GetAssignmentsAsync` и `ReviewerReadModelQueries.GetReviewerResultsAsync` больше не материализуют весь matching revision-ID set в память перед основным запросом (`revisionIds = await ...ToArrayAsync()` + `.Where(x => revisionIds.Contains(...))`) — testId/ownerId filters теперь выражены как correlated `Any()` subquery (SQL `EXISTS`) прямо в основном LINQ-запросе, а revisionId filter сравнивается напрямую с уже существующей FK-колонкой на самой таблице без обращения к `Revisions`. Post-page lookups (revision/attempt-stats/score aggregation, ограниченные текущей страницей) не менялись — они уже были bounded by `pageSize`. `AdminReadModelQueryTests.cs` проверяет testId/revisionId isolation через несколько тестов; owner isolation уже покрыт `TestOwnershipTests.cs`. Замечание: тесты подтверждают корректность результата после рефакторинга (все 91 существующих + 4 новых теста green до и после), но не воспроизводят надёжный red-before-fix сценарий — маленькая тестовая БД в одной транзакции не демонстрирует high-cardinality/scale проблему исходного подхода так же явно, как STAB-003/API-009.

`STAB-008` закрыт (см. `docs/DECISIONS.md` ADR-026 для полного rationale): `Test.Normalize`/`NormalizeAnswerOptionText` и приватный конструктор `TestAssignment` больше не кидают `ArgumentException`/`ArgumentOutOfRangeException` для business-rule invariants, достижимых через application use case — title/question-text/answer-option-text length и assignment availability-window/attempt-limit теперь возвращаются как `Result<T, DomainError>` с теми же error codes, что были у duplicated Application-level проверок (`test.title`, `test.question.text`, `test.answer_option.text`, `assignment.window`, `assignment.attempt_limit`). Убраны 6 `try/catch (ArgumentException ex) { return Error.Validation(code, ex.Message); }` в `TestLifecycleCommands.cs`/`QuestionCommands.cs`/`AnswerOptionCommands.cs` (эти try/catch утекали CLR-суффикс `" (Parameter 'x')"` в `ProblemDetails.detail` — подтверждено эмпирически) и дублированные availability-window/attempt-limit проверки в `AssignTestCommandHandler`/`BulkAssignTestsCommandHandler`. Domain-инварианты, недостижимые через correct API caller (value-object length checks, unreachable switch defaults, null Id guard) намеренно оставлены exceptions — критерий отбора см. в ADR-026. Regression tests: `TestAggregateTests.cs` (`DoesNotContain("Parameter", ...)` assertions — подтверждено сработавшими на намеренно реинтродуцированной утечке, затем откачено) и новый HTTP-level тест `RequestValidationContractTests.Assignment_business_rule_violations_return_domain_error_without_leaking_clr_exception_text`. Полный `dotnet test` на момент закрытия STAB-008 (12 domain + 8 application + 78 integration) был green; текущий baseline после coverage pass `0.9.5` — 267 тестов, см. `docs/TESTING.md` §2.

---

# B. Production hardening baseline

## CFG-001 — Fail-fast Database configuration

- **Приоритет:** P0
- **Трудоёмкость:** S
- **Статус:** ГОТОВО
- **Зависимости:** none

### Implemented scope

- убрать production fallback `testapp/testapp`;
- Development default оставить только явно;
- validated options/startup check.

### Критерии приёмки

- Production without `ConnectionStrings:Database` fails before serving traffic;
- secret не логируется;
- integration test host startup failure.

## CFG-002 — Validate Keycloak configuration

- **Приоритет:** P0
- **Трудоёмкость:** S
- **Статус:** ГОТОВО

### Критерии приёмки

- Production requires non-empty Authority/Audience;
- invalid URL rejected;
- HTTPS metadata remains required outside Development.

## CFG-003 — Typed RabbitMQ/worker options binding

- **Приоритет:** P1
- **Трудоёмкость:** S
- **Статус:** ГОТОВО

Bind/validate:

- RabbitMQ;
- Outbox delivery;
- attempt expiration.

## EDGE-001 — Trusted forwarded headers

- **Приоритет:** P0
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

### Критерии приёмки

- configured KnownProxies/KnownNetworks;
- untrusted `X-Forwarded-For` cannot spoof rate-limit partition;
- scheme/IP tests.

## EDGE-002 — CORS policy

- **Приоритет:** P0 if browser frontend deployed
- **Трудоёмкость:** S
- **Статус:** ГОТОВО

### Критерии приёмки

- explicit origin allow-list;
- no `AllowAnyOrigin + credentials`;
- env-specific config.

## EDGE-003 — HTTPS/HSTS deployment policy

- **Приоритет:** P0
- **Трудоёмкость:** S/M
- **Статус:** ГОТОВО

Document and test ingress termination behavior.

## EDGE-004 — Configurable rate limits

- **Приоритет:** P0
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

### Policies

- general;
- student write;
- reviewer/admin expensive read;
- operations.

### Критерии приёмки

- configuration binding;
- deterministic 429 tests;
- correlation still returned on rejection.

## API-SEC-001 — Production OpenAPI policy

- **Приоритет:** P0
- **Трудоёмкость:** S
- **Статус:** ГОТОВО

Configuration decides public/internal/disabled.

## CI-SEC-001 — Dependency vulnerability gate

- **Приоритет:** P0
- **Трудоёмкость:** S/M
- **Статус:** ГОТОВО

- NuGet vulnerability check;
- fail on high/critical agreed policy.

## CI-SEC-002 — Container image scan/SBOM

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

---

# C. Ownership и authorization

## AUTHZ-001 — Test ownership model

- **Приоритет:** P0
- **Трудоёмкость:** L
- **Статус:** ГОТОВО — selected `OwnerId` model for current single-organization scope

### Options

**A. OwnerId only** — проще для single-organization.

**B. Workspace + membership** — правильнее для multi-team/multi-tenant.

### Minimum acceptance

- every Test has non-null ownership scope;
- creation assigns current actor;
- migration/backfill existing tests;
- indexed SQL filters.

## AUTHZ-002 — Author catalog isolation

- **Приоритет:** P0
- **Трудоёмкость:** M
- **Зависимости:** AUTHZ-001
- **Статус:** ГОТОВО

Author sees only allowed scope; admin behavior explicitly defined.

## AUTHZ-003 — Authoring command ownership

- **Приоритет:** P0
- **Трудоёмкость:** M
- **Зависимости:** AUTHZ-001
- **Статус:** ГОТОВО

Protect rename/settings/question/option/publish/archive.

## AUTHZ-004 — Revision access isolation

- **Приоритет:** P0
- **Трудоёмкость:** S/M
- **Зависимости:** AUTHZ-001
- **Статус:** ГОТОВО

## AUTHZ-005 — Reviewer result isolation

- **Приоритет:** P0
- **Трудоёмкость:** M
- **Зависимости:** AUTHZ-001
- **Статус:** ГОТОВО

Two-author E2E negative test mandatory.

## AUTHZ-006 — Admin scope policy

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО — `test-admin` has explicit global scope

Decision: current `test-admin` scope is global. Workspace admin is reconsidered only with a Workspace/Tenant model.

## ID-001 — `(Issuer, Subject)` external identity

- **Приоритет:** P1; P0 if multi-realm production
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ/PLANNED

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

- **Приоритет:** P0/P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО — header resolver/fingerprint compatibility, including true zero-length body (API-009)

### Scope

- publish;
- assign;
- bulk assign;
- start attempt;
- submit.

### Критерии приёмки

- header required for designated commands;
- body key transitional support;
- generated OpenAPI;
- stable validation error.

Publish/start/submit accept a true zero-length body (nullable Minimal API body parameter); the key may live only in the `Idempotency-Key` header (API-009).

## API-002 — Idempotency request fingerprint

- **Приоритет:** P0/P1
- **Трудоёмкость:** M
- **Зависимости:** API-001
- **Статус:** ГОТОВО

Same key + different payload must not silently replay unrelated result.

Store canonical request hash with record.

## API-003 — ETag / `If-Match`

- **Приоритет:** P1
- **Трудоёмкость:** M/L
- **Статус:** ГОТОВО

Expose aggregate version on mutable author/admin resources.

### Критерии приёмки

- stale If-Match -> 412/409 policy documented;
- no blind lost update;
- OpenAPI examples.

## API-004 — Unified request validation

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

Length/range/required/enum validation before handler where transport-specific.

## API-005 — OpenAPI enrichment

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

- descriptions;
- examples;
- policy/security metadata;
- ProblemDetails;
- enum values;
- pagination;
- idempotency.

## API-006 — OpenAPI contract snapshot test

- **Приоритет:** P1
- **Трудоёмкость:** S/M
- **Зависимости:** API-005
- **Статус:** ГОТОВО

Detect accidental breaking changes.

## API-007 — Legacy `/api/*` deprecation

- **Приоритет:** P2
- **Трудоёмкость:** S
- **Статус:** ГОТОВО — lifecycle headers/retirement behavior implemented; rewrite removal remains a future compatibility decision

### Steps

1. response deprecation/sunset headers if desired;
2. client migration;
3. telemetry of legacy usage;
4. remove rewrite in next major/version window.

## API-008 — Stable filter/sort conventions

- **Приоритет:** P2
- **Трудоёмкость:** M
- **Статус:** ЗАПЛАНИРОВАНО

Needed before richer catalogs/reporting.

## API-009 — True empty-body header-only commands

- **Приоритет:** P0/P1
- **Трудоёмкость:** S/M
- **Статус:** ГОТОВО
- **Зависимости:** API-001

Publish/start/submit endpoint handlers принимают nullable request DTO (`PublishRequest?`/`StartAttemptRequest?`/`SubmitAttemptRequest?`); zero-length body binds to `null` and `IdempotencyKeyResolver.Resolve` falls back to `Guid.Empty` for the legacy body key, requiring the `Idempotency-Key` header. Covered by `EmptyBodyIdempotencyTests.cs` (real zero-length HTTP requests, no `Content`/payload). OpenAPI request body `required` flag is derived automatically from the now-nullable parameter type.

---

# E. Operational reliability

## OPS-010 — Backup policy

- **Приоритет:** P0
- **Трудоёмкость:** M
- **Статус:** ГОТОВО — repository logical baseline; provider PITR/scheduling remain deployment-owned

Repository baseline defines engineering RPO/RTO and portable retention; concrete schedule/encrypted storage/PITR remain deployment-owned.

## OPS-011 — Automated restore verification

- **Приоритет:** P0
- **Трудоёмкость:** M/L
- **Зависимости:** OPS-010
- **Статус:** ГОТОВО

CI restores each generated backup to an isolated database and verifies schema, migration history and business marker.

## OPS-012 — Audit retention cleanup

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

Batch delete/archive with index-friendly range.

## OPS-013 — Idempotency retention cleanup

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

Retention must exceed maximum retry window/client guarantees.

## OPS-014 — Processed Outbox retention

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

Do not delete pending/dead-letter rows blindly.

## OPS-015 — Dead-letter requeue API

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

### Критерии приёмки

- admin-only;
- audit;
- only dead-letter rows;
- reset attempt schedule explicitly;
- concurrency-safe;
- no payload edit.

## OPS-016 — Dead-letter acknowledge/drop

- **Приоритет:** P2
- **Трудоёмкость:** M
- **Статус:** ГОТОВО — explicit audited `discard` terminal state selected

Needed only if operations requires permanent suppression state.

## OBS-010 — Metrics for Outbox lag

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

## OBS-011 — Attempt expiration lag metric

- **Приоритет:** P1
- **Трудоёмкость:** S/M
- **Статус:** ГОТОВО

## OBS-012 — API SLO dashboard

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО — Prometheus + Grafana added to `compose.yaml` (ADR-027); `deploy/grafana/dashboards/testapp-overview.json` implements the `docs/SLO_ALERTS.md` §6 dashboard minimum, provisioned automatically and CI-validated (`scripts/validate-observability-stack.sh`, workflow `observability`)

## OBS-013 — Alerts

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Зависимости:** OBS-010..012
- **Статус:** ГОТОВО — rule expressions implemented for every `docs/SLO_ALERTS.md` §4 rule expressible from existing metrics (`deploy/prometheus/alerts.yml`, 6 page + 6 warning rules, CI-validated); routing to an actionable destination and the staging drill remain open (`docs/SLO_ALERTS.md` §7)

Alert on:

- readiness — **not implemented**: needs an active HTTP prober (e.g. `blackbox_exporter`), the metrics pipeline alone cannot express this;
- 5xx — done;
- latency — done (p95/p99);
- dead letters — done;
- Outbox lag — done;
- DB/RMQ connectivity — covered indirectly via readiness once the prober above exists.

## PERF-001 — Load test baseline

- **Приоритет:** P0 before sized production launch
- **Трудоёмкость:** L
- **Статус:** ГОТОВО — full PostgreSQL/RabbitMQ D6 gate green on `9916b98`

Scenarios documented in `TESTING.md`.

---

# F. Integration event catalog

## EVT-001 — `TestRevisionPublishedV1`

- **Приоритет:** P1 when consumer exists
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

Must be explicit `IIntegrationEvent`; no raw aggregate serialization.

## EVT-002 — `TestAssignedV1`

- **Приоритет:** P1 when notification/integration consumer exists
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

## EVT-003 — `AttemptCompletedV1`

- **Приоритет:** P1 for analytics/notifications
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

Prefer stable external completion contract rather than exposing internal submitted/timed-out event classes.

## EVT-004 — Integration event schema version policy

- **Приоритет:** P1 before first external consumer
- **Трудоёмкость:** S/M
- **Статус:** ЗАПЛАНИРОВАНО

## EVT-005 — Consumer dedup reference implementation/test harness

- **Приоритет:** P2
- **Трудоёмкость:** M
- **Статус:** ЗАПЛАНИРОВАНО

---

# G. Authoring productivity

## AUTHOR-001 — Draft validation endpoint

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ЗАПЛАНИРОВАНО

Returns publication problems without changing status.

## AUTHOR-002 — Clone test

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ЗАПЛАНИРОВАНО

Creates new Draft with copied content and new IDs according to explicit policy.

## AUTHOR-003 — Create draft from published revision

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ЗАПЛАНИРОВАНО

Useful for branching/copying historical version.

## AUTHOR-004 — Tags

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ/PLANNED

Need normalized tag storage/index/filtering.

## AUTHOR-005 — Category/subject

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

## AUTHOR-006 — Rich catalog filters/sort

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Зависимости:** AUTHOR-004/005 as selected

## AUTHOR-007 — Versioned JSON export

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ЗАПЛАНИРОВАНО

Do not export internal EF entity schema directly.

## AUTHOR-008 — Versioned JSON import

- **Приоритет:** P1
- **Трудоёмкость:** L
- **Зависимости:** AUTHOR-007

Full validation before persistence; no partial import.

## AUTHOR-009 — CSV import/export simple-choice

- **Приоритет:** P2
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

Only if business users need spreadsheet workflow.

## AUTHOR-010 — Question bank

- **Приоритет:** P1/P2
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

Key decision: copy vs live reference. Recommendation: authoring may reference/copy, published revision always snapshots.

---

# H. Assessment behavior

## ASMT-001 — Shuffle answer options

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

Presentation order should be deterministic/stored per attempt if result review must reproduce UI.

## ASMT-002 — Shuffle questions

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

## ASMT-003 — Question pools

- **Приоритет:** P1/P2
- **Трудоёмкость:** L/XL
- **Статус:** РЕШЕНИЕ

Attempt must snapshot selected question IDs.

## ASMT-004 — Partial scoring strategy

- **Приоритет:** P1
- **Трудоёмкость:** L
- **Статус:** РЕШЕНИЕ

Revision snapshots scoring strategy/version.

## ASMT-005 — Negative marking

- **Приоритет:** P2
- **Трудоёмкость:** M/L
- **Статус:** РЕШЕНИЕ

Requires explicit minimum score/rounding policy.

## ASMT-006 — Numeric answer

- **Приоритет:** P1
- **Трудоёмкость:** L
- **Статус:** РЕШЕНИЕ

Need tolerance/normalization rules.

## ASMT-007 — Short text auto-match

- **Приоритет:** P2
- **Трудоёмкость:** L
- **Статус:** РЕШЕНИЕ

Normalization/localization complexity.

## ASMT-008 — FreeText manual grading

- **Приоритет:** P1/P2
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

Requires new attempt grading lifecycle and reviewer write permissions.

## ASMT-009 — Ordering question

- **Приоритет:** P2
- **Трудоёмкость:** L
- **Статус:** РЕШЕНИЕ

## ASMT-010 — Matching question

- **Приоритет:** P2
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

## ASMT-011 — Attachments/images

- **Приоритет:** P2
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

Requires object storage, scanning, signed access, content security.

## ASMT-012 — Rich text/Markdown

- **Приоритет:** P1/P2
- **Трудоёмкость:** M/L
- **Статус:** РЕШЕНИЕ

Requires rendering/sanitization policy in frontend.

---

# I. Attempt UX/lifecycle

## ATT-010 — Attempt presentation DTO

- **Приоритет:** P1 when frontend starts
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

`GET /api/v1/attempts/{id}/presentation` возвращает `AttemptPresentationView`: вопросы/варианты из immutable revision привязанной попытки, сохранённые ответы студента, `status`/`deadlineAt`/`serverTime`. Признака корректности нет ни на одном уровне DTO — граница закреплена тестом на сериализованном HTTP-ответе, а не только по полям (ADR-028). Существующий `GET /attempts/{id}` не менялся, чтобы не ломать v1 contract.

## ATT-011 — Resume active attempt

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

`GET /api/v1/assignments/{id}/attempts/active` отдаёт попытку студента в статусе `InProgress` для этого assignment или `404`, если возобновлять нечего. Клиент, потерявший `attemptId`, возобновляет работу вместо старта новой попытки (что израсходовало бы attempt limit). Завершённые попытки через resume не отдаются — они читаются через `/result`.

## ATT-012 — Explicit attempt start metadata

- **Приоритет:** P2
- **Трудоёмкость:** S/M
- **Статус:** РЕШЕНИЕ

Client/device metadata only if privacy/business need.

## ATT-013 — Pause/resume clock

- **Приоритет:** P3
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

Not compatible with current simple deadline semantics without domain redesign.

## ATT-014 — Autosave batching

- **Приоритет:** P2
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

Current per-question PUT is already retryable via aggregate concurrency but not idempotency-keyed.

## ATT-015 — Attempt abandon

- **Приоритет:** P2
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

Need clear impact on attempt limit/result statistics.

---

# J. Assignment features

## ASN-010 — Assignment template

- **Приоритет:** P1
- **Трудоёмкость:** L
- **Статус:** РЕШЕНИЕ

Reusable config for revision/window/limit/targets.

## ASN-011 — Campaign/batch entity

- **Приоритет:** P1/P2
- **Трудоёмкость:** L
- **Статус:** РЕШЕНИЕ

Useful if bulk assignments need lifecycle/report as one unit.

## ASN-012 — Scheduled future assignments

- **Приоритет:** P1
- **Трудоёмкость:** S/M
- **Статус:** ЗАПЛАНИРОВАНО

Current `AvailableFrom` already supports future availability; feature mainly adds UX/query/filter/scheduling semantics.

## ASN-013 — Assignment duplicate prevention policy

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

Current system allows multiple assignments of same revision/target with distinct IDs. Decide whether that is intentional.

## ASN-014 — Group membership semantics

- **Приоритет:** P1
- **Трудоёмкость:** L
- **Статус:** РЕШЕНИЕ

Choose:

- dynamic at access time (current);
- snapshot membership at assignment time.

## ASN-015 — Reassignment after completion

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

Clarify whether new assignment or attempt-limit change is canonical.

## ASN-016 — Assignment reminder metadata

- **Приоритет:** P2
- **Трудоёмкость:** M
- **Зависимости:** notification integration

---

# K. Reporting/analytics

## REP-010 — Test summary dashboard

- **Приоритет:** P1
- **Трудоёмкость:** L
- **Статус:** ЗАПЛАНИРОВАНО

Metrics:

- attempts;
- completion rate;
- pass rate;
- average score;
- average duration.

## REP-011 — Revision comparison

- **Приоритет:** P2
- **Трудоёмкость:** L
- **Статус:** РЕШЕНИЕ

Never mix results across revisions without explicit grouping.

## REP-012 — Question difficulty

- **Приоритет:** P1/P2
- **Трудоёмкость:** L
- **Статус:** ЗАПЛАНИРОВАНО after sufficient data

Group by `(RevisionId, QuestionId)`.

## REP-013 — Answer distribution

- **Приоритет:** P2
- **Трудоёмкость:** L
- **Статус:** ЗАПЛАНИРОВАНО

Reviewer/admin only; careful correctness exposure.

## REP-014 — CSV result export

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ЗАПЛАНИРОВАНО

Authorization + audit + streaming response.

## REP-015 — Analytics projection tables

- **Приоритет:** P2
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

Only after query/load measurements.

## REP-016 — External warehouse

- **Приоритет:** P3
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

Requires integration event catalog and real analytics scale need.

---

# L. Notifications/integrations

## NOTIF-001 — Assignment-created notification contract

- **Приоритет:** P1/P2
- **Трудоёмкость:** M
- **Зависимости:** EVT-002
- **Статус:** РЕШЕНИЕ

## NOTIF-002 — Deadline reminder scheduler

- **Приоритет:** P2
- **Трудоёмкость:** L
- **Статус:** РЕШЕНИЕ

Should produce event/job, not send email inside transaction.

## NOTIF-003 — Completion/result notification

- **Приоритет:** P2
- **Трудоёмкость:** M/L
- **Зависимости:** EVT-003

## INT-001 — Webhook delivery adapter

- **Приоритет:** P2
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

Only if external consumers cannot consume RabbitMQ.

## INT-002 — Webhook signatures/retry

- **Приоритет:** P2
- **Трудоёмкость:** L
- **Зависимости:** INT-001

---

# M. Workspace/multi-tenancy

## TEN-001 — Workspace aggregate

- **Приоритет:** P1/P2 depending deployment
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

## TEN-002 — Workspace membership

- **Приоритет:** same
- **Трудоёмкость:** XL
- **Зависимости:** TEN-001, ID-001 likely

## TEN-003 — Workspace-scoped roles

- **Приоритет:** same
- **Трудоёмкость:** L

## TEN-004 — Workspace data isolation

- **Приоритет:** P0 if multi-tenant
- **Трудоёмкость:** XL

Every read/write query must become workspace-aware and indexed.

## TEN-005 — Tenant-aware audit/Outbox

- **Приоритет:** P1
- **Трудоёмкость:** L

---

# N. Frontend/client readiness

## UX-001 — Student test-taking presentation API

- **Приоритет:** P1
- **Трудоёмкость:** M/L
- **Статус:** ГОТОВО — закрыт вместе с ATT-010/ATT-011

Student-safe question/options DTO + текущие ответы + deadline реализованы; см. ATT-010, ATT-011 и ADR-028.

## UX-002 — Author editing API ergonomics

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ЗАПЛАНИРОВАНО

May include batch reorder/update to reduce chatty UI.

## UX-003 — Typed client SDK generation

- **Приоритет:** P2
- **Трудоёмкость:** M
- **Зависимости:** stable enriched OpenAPI

## UX-004 — Frontend application

- **Приоритет:** product-dependent
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

Not part of current repository baseline.

---

# O. Developer experience

## DEV-001 — Central package management

- **Приоритет:** P2
- **Трудоёмкость:** S/M
- **Статус:** ЗАПЛАНИРОВАНО

Introduce `Directory.Packages.props` if package count continues growing.

## DEV-002 — Standard EF migration tooling/snapshot

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

Repeatable `dotnet ef` workflow реализован через `AppDbContextDesignFactory`; PostgreSQL baseline имеет designer metadata и `AppDbContextModelSnapshot`.

## DEV-003 — Architecture dependency tests

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО — assembly reference direction baseline

Automate Domain-no-EF/API and layer reference constraints.

## DEV-004 — Formatter/analyzer CI

- **Приоритет:** P2
- **Трудоёмкость:** S/M
- **Статус:** ЗАПЛАНИРОВАНО

## DEV-005 — Conventional changelog/release notes

- **Приоритет:** P1 before 1.0
- **Трудоёмкость:** S
- **Статус:** ГОТОВО

`CHANGELOG.md` (repo root) реализован по формату Keep a Changelog + SemVer, с явно задокументированным отклонением: до первого git-тега версии совпадают с фазами `docs/ROADMAP.md` (`0.8.x` … `0.9.4`), а не с датами. Записи покрывают Phase A–D7 (включая все `STAB-001..008` и `API-009`), плюс открытый `[Unreleased]` раздел с оставшимися Phase E gaps. Conventional Commits prefixes рекомендованы (не обязательны) начиная с `1.0.0`.

Отдельно от этой задачи остаётся rollback/forward-fix release runbook (ROADMAP.md item 10, вторая половина) — он требует staging rehearsal и не закрывается документацией changelog.

---

# P. Recommended implementation order

Следующие фичи выполнять именно в этом порядке, если business priority не меняется:

```text
1  1.0 release rehearsal: migration/restore/alerts/rollback/API freeze (all STAB-001..008, API-009 and PERF-001/D6 are DONE)
2  ATT-010/011 + UX-001 student presentation/resume (DONE)
3  AUTHOR-001/002/003 + selected tags/catalog + versioned JSON import/export
4  reporting hot paths REP-010/014 after SQL scalability work
5  EVT catalog only when first real consumer appears
6  assignment orchestration/notifications selected by product need
7  advanced assessment features one versioned vertical slice at a time
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
