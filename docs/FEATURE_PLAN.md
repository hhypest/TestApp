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
| TEST-004 | Core/Domain/Application unit suites | ГОТОВО | 341 tests, 90.58% exact-head line coverage; CI floor 90.00% |

---

## Текущий backlog стабилизации `0.9.4`

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
| STAB-009 | P1 | ГОТОВО | self-validating value objects: strong identifiers и `AttemptScore` проверяют себя сами; deadline становится authority для `TestAttempt.Timeout` |

`STAB-002` закрыт перестановкой middleware boundary: correlation/audit выполняется снаружи exception handler и наблюдает уже обработанный response. Regression suite проверяет равенство response/audit status для binding `400`, concurrency `409`, precondition `412` и unhandled `500`.

`STAB-004` закрыт: полный HTTP/expiration/Outbox gate прошёл на PostgreSQL implementation commit `9916b98`; exact-head `dotnet` и `security` также green.

`STAB-005` закрыт: composition root (`Program.cs`) при `--migrate` загружает только `RuntimeConfiguration.LoadDatabase` и пропускает Keycloak/RabbitMQ/attempt-expiration/rate-limiting/OpenAPI/CORS/transport-security/reverse-proxy loaders и связанные DI-регистрации. `compose.yaml` сервис `migrate` и CI-шаг "Apply migrations from production image" передают только `ConnectionStrings:Database`/`Database:ApplyMigrationsOnStartup`. Проверено локально: production build запускает `--migrate` (exit 0, миграция применяется) без единой Keycloak/RabbitMQ/CORS переменной; полный `dotnet test` (18 unit + 69 integration) green.

`STAB-003` закрыт: `OutboxProcessor`/`OverdueAttemptProcessor` оборачивают каждый poll cycle в try/catch (`RunCycleAsync`) — cycle-level failure (напр. transient DB outage при открытии scope) логируется и worker продолжает на следующий `PeriodicTimer` tick вместо fault хостового `BackgroundService` (default `BackgroundServiceExceptionBehavior.StopHost` иначе останавливает весь API process). `WorkerCycleResilienceTests.cs` симулирует однократный сбой `IServiceScopeFactory.CreateScope()` и проверяет: (1) recovery — сообщение/attempt обрабатывается на следующем cycle; (2) `ExecuteTask.IsFaulted == false`. Оба теста подтверждённо red без fix (revert проверен вручную) и green с ним; прямые вызовы `ProcessBatchAsync`/`DrainAvailableAsync` (existing tests) продолжают бросать исключения как раньше — swallow только на уровне hosted-service cycle.

`STAB-006` закрыт: все paged read models с offset pagination (`AssignmentAdminQueries.GetAssignmentsAsync`/`GetAssignmentAttemptsAsync`, `AssignmentReadModelQueries.GetAssignmentsAsync`, `AttemptReadModelQueries.GetAttemptsAsync`, `ReviewerReadModelQueries.GetReviewerResultsAsync`) добавили `.ThenByDescending(x => x.Id)` вслед за primary timestamp order — та же схема, что уже применялась в `TestCatalogQueries.GetTestsAsync` (`.ThenBy(x => x.Id)`). `AdminReadModelQueryTests.cs` проверяет, что полный обход страниц через записи с одинаковым `AssignedAt`/`StartedAt` возвращает каждую запись ровно один раз без дублей/пропусков.

`STAB-007` закрыт: `AssignmentAdminQueries.GetAssignmentsAsync` и `ReviewerReadModelQueries.GetReviewerResultsAsync` больше не материализуют весь matching revision-ID set в память перед основным запросом (`revisionIds = await ...ToArrayAsync()` + `.Where(x => revisionIds.Contains(...))`) — testId/ownerId filters теперь выражены как correlated `Any()` subquery (SQL `EXISTS`) прямо в основном LINQ-запросе, а revisionId filter сравнивается напрямую с уже существующей FK-колонкой на самой таблице без обращения к `Revisions`. Post-page lookups (revision/attempt-stats/score aggregation, ограниченные текущей страницей) не менялись — они уже были bounded by `pageSize`. `AdminReadModelQueryTests.cs` проверяет testId/revisionId isolation через несколько тестов; owner isolation уже покрыт `TestOwnershipTests.cs`. Замечание: тесты подтверждают корректность результата после рефакторинга (все 91 существующих + 4 новых теста green до и после), но не воспроизводят надёжный red-before-fix сценарий — маленькая тестовая БД в одной транзакции не демонстрирует high-cardinality/scale проблему исходного подхода так же явно, как STAB-003/API-009.

`STAB-008` закрыт (см. `docs/DECISIONS.md` ADR-026 для полного rationale): `Test.Normalize`/`NormalizeAnswerOptionText` и приватный конструктор `TestAssignment` больше не кидают `ArgumentException`/`ArgumentOutOfRangeException` для business-rule invariants, достижимых через application use case — title/question-text/answer-option-text length и assignment availability-window/attempt-limit теперь возвращаются как `Result<T, DomainError>` с теми же error codes, что были у duplicated Application-level проверок (`test.title`, `test.question.text`, `test.answer_option.text`, `assignment.window`, `assignment.attempt_limit`). Убраны 6 `try/catch (ArgumentException ex) { return Error.Validation(code, ex.Message); }` в `TestLifecycleCommands.cs`/`QuestionCommands.cs`/`AnswerOptionCommands.cs` (эти try/catch утекали CLR-суффикс `" (Parameter 'x')"` в `ProblemDetails.detail` — подтверждено эмпирически) и дублированные availability-window/attempt-limit проверки в `AssignTestCommandHandler`/`BulkAssignTestsCommandHandler`. Domain-инварианты, недостижимые через correct API caller (value-object length checks, unreachable switch defaults, null Id guard) намеренно оставлены exceptions — критерий отбора см. в ADR-026. Regression tests: `TestAggregateTests.cs` (`DoesNotContain("Parameter", ...)` assertions — подтверждено сработавшими на намеренно реинтродуцированной утечке, затем откачено) и новый HTTP-level тест `RequestValidationContractTests.Assignment_business_rule_violations_return_domain_error_without_leaking_clr_exception_text`. Полный `dotnet test` на момент закрытия STAB-008 (12 domain + 8 application + 78 integration) был green; текущий baseline после coverage pass `0.9.5` — 267 тестов, см. `docs/TESTING.md` §2.

`STAB-009` закрыт (см. `docs/DECISIONS.md` ADR-029 и ADR-030): это те четыре «invariant gaps до 1.0», которые перечислял `DOMAIN_MODEL.md` §11 и которые подтвердил независимый аудит `0b94db3`.

1. **Strong identifiers.** `TestId`/`QuestionId`/`AnswerOptionId`/`TestAssignmentId`/`TestAttemptId`/`PublishedTestRevisionId` перестали быть positional records с непроверяемым конструктором: конструктор теперь отвергает `Guid.Empty`. `ExternalUserId`/`ExternalGroupId` перенесли проверку из фабрик `FromSubject`/`FromExternalId` в конструктор, так что `new ExternalUserId(...)` больше не обходит валидацию — фабрики остались как intent-revealing имена и делегируют конструктору.
2. **`AttemptScore`.** Конструктор требует `0 <= Earned <= Maximum` и `Maximum >= 0`. Приватный parameterless конструктор оставлен только для EF-материализации owned-колонок — тот же контракт, что у остальных агрегатов.
3. **`Timeout`.** Deadline стал authority: `TestAttempt.Timeout` возвращает `attempt.not_expired` (409), если deadline ещё не наступил или его нет вовсе. Ручной административный таймаут получил отдельный метод `ForceTimeout` — capability сохранена, но call site теперь обязан назвать, чьей властью он закрывает попытку (ADR-030).
4. **Граница.** Так как identifiers отказываются строиться из нулевого GUID, транспорт обязан отвечать на него раньше конструктора, иначе любой клиент превращал бы domain guard в 500. `RequestValidationFilter` отвечает `404` на нулевой GUID в route-сегменте (то же, что возвращалось и раньше) и `400` в query-фильтре; `NotEmptyGuidAttribute` закрывает GUID-поля тела запроса.

Найденный по ходу дефект: get-only свойство `Value` ломало `System.Text.Json`-материализацию — сериализатор молча отдавал `Guid.Empty` для каждого идентификатора внутри `jsonb`-колонки `PublishedTestRevision.Questions`. Ошибку поймал новый unit-тест round-trip'а (`ValueObjectInvariantTests.The_published_question_payload_round_trips_through_system_text_json`), а не интеграционный прогон; закрыто атрибутом `[JsonConstructor]`, который заодно делает материализацию `jsonb` валидируемой. Тест подтверждённо red без атрибута.

Regression tests: `ValueObjectInvariantTests.cs` (18 тестов), пять новых тестов timeout-семантики в `AttemptLifecycleTests.cs`, HTTP-контракт нулевого GUID в `RequestValidationContractTests.cs`.

---

# B. Базовое усиление для production

## CFG-001 — Конфигурация базы данных с ранним отказом

- **Приоритет:** P0
- **Трудоёмкость:** S
- **Статус:** ГОТОВО
- **Зависимости:** none

### Реализованный объём

- убрать production fallback `testapp/testapp`;
- Development default оставить только явно;
- проверенные опции и стартовая проверка.

### Критерии приёмки

- production без `ConnectionStrings:Database` падает до приёма трафика;
- secret не логируется;
- интеграционный тест падения запуска хоста.

## CFG-002 — Валидация конфигурации Keycloak

- **Приоритет:** P0
- **Трудоёмкость:** S
- **Статус:** ГОТОВО

### Критерии приёмки

- production требует непустых Authority/Audience;
- некорректный URL отклоняется;
- HTTPS для метаданных остаётся обязательным вне Development.

## CFG-003 — Типизированная привязка опций RabbitMQ и воркеров

- **Приоритет:** P1
- **Трудоёмкость:** S
- **Статус:** ГОТОВО

Bind/validate:

- RabbitMQ;
- доставка Outbox;
- истечение попыток.

## EDGE-001 — Доверенные forwarded-заголовки

- **Приоритет:** P0
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

### Критерии приёмки

- настроенные KnownProxies/KnownNetworks;
- недоверенный `X-Forwarded-For` не может подменить ключ партиционирования rate limit;
- тесты схемы и IP.

## EDGE-002 — Политика CORS

- **Приоритет:** P0 if browser frontend deployed
- **Трудоёмкость:** S
- **Статус:** ГОТОВО

### Критерии приёмки

- явный список разрешённых источников;
- no `AllowAnyOrigin + credentials`;
- конфигурация под окружение.

## EDGE-003 — Политика HTTPS/HSTS при развёртывании

- **Приоритет:** P0
- **Трудоёмкость:** S/M
- **Статус:** ГОТОВО

Задокументировать и протестировать поведение терминации на ingress.

## EDGE-004 — Настраиваемые лимиты частоты

- **Приоритет:** P0
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

### Policies

- general;
- запись студента;
- тяжёлое чтение рецензента и администратора;
- operations.

### Критерии приёмки

- привязка конфигурации;
- deterministic 429 tests;
- корреляция возвращается и при отклонении.

## API-SEC-001 — Политика OpenAPI в production

- **Приоритет:** P0
- **Трудоёмкость:** S
- **Статус:** ГОТОВО

Конфигурация определяет режим: публичный, внутренний или выключенный.

## CI-SEC-001 — Гейт уязвимостей зависимостей

- **Приоритет:** P0
- **Трудоёмкость:** S/M
- **Статус:** ГОТОВО

- проверка уязвимостей NuGet;
- падение при high/critical согласно принятой политике.

## CI-SEC-002 — Сканирование образа и SBOM

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

---

# C. Ownership и authorization

## AUTHZ-001 — Модель владения тестом

- **Приоритет:** P0
- **Трудоёмкость:** L
- **Статус:** ГОТОВО — selected `OwnerId` model for current single-organization scope

### Options

**A. OwnerId only** — проще для single-organization.

**B. Workspace + membership** — правильнее для multi-team/multi-tenant.

### Минимальные критерии приёмки

- у каждого `Test` есть непустая область владения;
- при создании владельцем становится текущий актор;
- миграция и заполнение существующих тестов;
- индексируемые SQL-фильтры.

## AUTHZ-002 — Изоляция каталога автора

- **Приоритет:** P0
- **Трудоёмкость:** M
- **Зависимости:** AUTHZ-001
- **Статус:** ГОТОВО

Автор видит только разрешённую область; поведение администратора определено явно.

## AUTHZ-003 — Владение при командах авторинга

- **Приоритет:** P0
- **Трудоёмкость:** M
- **Зависимости:** AUTHZ-001
- **Статус:** ГОТОВО

Защитить переименование, настройки, вопросы, варианты, публикацию и архивирование.

## AUTHZ-004 — Изоляция доступа к ревизиям

- **Приоритет:** P0
- **Трудоёмкость:** S/M
- **Зависимости:** AUTHZ-001
- **Статус:** ГОТОВО

## AUTHZ-005 — Изоляция результатов рецензирования

- **Приоритет:** P0
- **Трудоёмкость:** M
- **Зависимости:** AUTHZ-001
- **Статус:** ГОТОВО

Сквозной негативный тест с двумя авторами обязателен.

## AUTHZ-006 — Политика области администратора

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО — `test-admin` has explicit global scope

Решение: текущая область `test-admin` глобальная. Администратор уровня workspace пересматривается только вместе с моделью Workspace/Tenant.

## ID-001 — Внешняя идентичность `(Issuer, Subject)`

- **Приоритет:** P1; P0 if multi-realm production
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ/PLANNED

### Влияние на миграцию

- assignments;
- attempts;
- idempotency;
- audit;
- ownership;
- интеграционные события;
- indexes.

---

# D. Зрелость контракта API

## API-001 — Standard `Idempotency-Key` header

- **Приоритет:** P0/P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО — header resolver/fingerprint compatibility, including true zero-length body (API-009)

### Scope

- publish;
- assign;
- массовое назначение;
- старт попытки;
- submit.

### Критерии приёмки

- заголовок обязателен для назначенных команд;
- переходная поддержка ключа в теле;
- сгенерированный OpenAPI;
- стабильная ошибка валидации.

Публикация, старт и отправка принимают настоящее тело нулевой длины (nullable-параметр тела Minimal API); ключ может находиться только в заголовке `Idempotency-Key` (API-009).

## API-002 — Отпечаток запроса для идемпотентности

- **Приоритет:** P0/P1
- **Трудоёмкость:** M
- **Зависимости:** API-001
- **Статус:** ГОТОВО

Тот же ключ с другим payload не должен молча возвращать несвязанный результат.

Хранить канонический хеш запроса вместе с записью.

## API-003 — ETag / `If-Match`

- **Приоритет:** P1
- **Трудоёмкость:** M/L
- **Статус:** ГОТОВО

Публиковать версию агрегата на изменяемых ресурсах автора и администратора.

### Критерии приёмки

- задокументированная политика устаревшего If-Match -> 412/409;
- отсутствие незаметной потери обновления;
- примеры в OpenAPI.

## API-004 — Единая валидация запросов

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

Валидация длины, диапазона, обязательности и перечислений до обработчика там, где она специфична для транспорта.

## API-005 — Обогащение OpenAPI

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

- descriptions;
- examples;
- метаданные политик и безопасности;
- ProblemDetails;
- значения перечислений;
- pagination;
- idempotency.

## API-006 — Снимочный контрактный тест OpenAPI

- **Приоритет:** P1
- **Трудоёмкость:** S/M
- **Зависимости:** API-005
- **Статус:** ГОТОВО

Обнаруживать случайные ломающие изменения.

## API-007 — Legacy `/api/*` deprecation

- **Приоритет:** P2
- **Трудоёмкость:** S
- **Статус:** ГОТОВО — lifecycle headers/retirement behavior implemented; rewrite removal remains a future compatibility decision

### Steps

1. заголовки deprecation/sunset в ответе при необходимости;
2. миграция клиентов;
3. телеметрия использования легаси;
4. удаление переписывания в следующем мажорном окне версий.

## API-008 — Стабильные соглашения фильтрации и сортировки

- **Приоритет:** P2
- **Трудоёмкость:** M
- **Статус:** ЗАПЛАНИРОВАНО

Необходимо до появления более богатых каталогов и отчётности.

## API-009 — Команды с настоящим пустым телом и ключом только в заголовке

- **Приоритет:** P0/P1
- **Трудоёмкость:** S/M
- **Статус:** ГОТОВО
- **Зависимости:** API-001

Publish/start/submit endpoint handlers принимают nullable request DTO (`PublishRequest?`/`StartAttemptRequest?`/`SubmitAttemptRequest?`); zero-length body binds to `null` and `IdempotencyKeyResolver.Resolve` falls back to `Guid.Empty` for the legacy body key, requiring the `Idempotency-Key` header. Covered by `EmptyBodyIdempotencyTests.cs` (real zero-length HTTP requests, no `Content`/payload). OpenAPI request body `required` flag is derived automatically from the now-nullable parameter type.

---

# E. Эксплуатационная надёжность

## OPS-010 — Политика резервного копирования

- **Приоритет:** P0
- **Трудоёмкость:** M
- **Статус:** ГОТОВО — repository logical baseline; provider PITR/scheduling remain deployment-owned

Baseline репозитория задаёт инженерные RPO/RTO и переносимые сроки хранения; конкретное расписание, шифрованное хранилище и PITR остаются за развёртыванием.

## OPS-011 — Автоматическая проверка восстановления

- **Приоритет:** P0
- **Трудоёмкость:** M/L
- **Зависимости:** OPS-010
- **Статус:** ГОТОВО

CI восстанавливает каждую созданную резервную копию в изолированную базу и проверяет схему, историю миграций и бизнес-маркер.

## OPS-012 — Очистка аудита по сроку хранения

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

Пакетное удаление или архивирование по диапазону, дружественному индексам.

## OPS-013 — Очистка записей идемпотентности по сроку хранения

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

Срок хранения должен превышать максимальное окно повторов и клиентские гарантии.

## OPS-014 — Срок хранения обработанных записей Outbox

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

Не удалять необработанные и dead-letter записи вслепую.

## OPS-015 — API повторной постановки dead-letter

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

### Критерии приёмки

- admin-only;
- audit;
- только записи dead-letter;
- явный сброс расписания попыток;
- concurrency-safe;
- редактирование payload запрещено.

## OPS-016 — Подтверждение и отбрасывание dead-letter

- **Приоритет:** P2
- **Трудоёмкость:** M
- **Статус:** ГОТОВО — explicit audited `discard` terminal state selected

Нужно только если эксплуатации требуется постоянное состояние подавления.

## OBS-010 — Метрики отставания Outbox

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

## OBS-011 — Метрика отставания истечения попыток

- **Приоритет:** P1
- **Трудоёмкость:** S/M
- **Статус:** ГОТОВО

## OBS-012 — Дашборд SLO для API

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО — Prometheus + Grafana added to `compose.yaml` (ADR-027); `deploy/grafana/dashboards/testapp-overview.json` implements the `docs/SLO_ALERTS.md` §6 dashboard minimum, provisioned automatically and CI-validated (`scripts/validate-observability-stack.sh`, workflow `observability`)

## OBS-013 — Alerts

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Зависимости:** OBS-010..012
- **Статус:** ГОТОВО — rule expressions implemented for every `docs/SLO_ALERTS.md` §4 rule expressible from existing metrics (`deploy/prometheus/alerts.yml`, 6 page + 6 warning rules, CI-validated); routing to an actionable destination and the staging drill remain open (`docs/SLO_ALERTS.md` §7)

Алерты по:

- readiness — **не реализовано**: требуется активный HTTP-пробер (например `blackbox_exporter`), одним metrics pipeline это не выражается;
- 5xx — done;
- latency — done (p95/p99);
- dead letter — готово;
- отставание Outbox — готово;
- доступность БД/RabbitMQ — покрывается косвенно через readiness, как только появится указанный выше пробер.

## PERF-001 — Базовые нагрузочные тесты

- **Приоритет:** P0 before sized production launch
- **Трудоёмкость:** L
- **Статус:** ГОТОВО — full PostgreSQL/RabbitMQ D6 gate green on `9916b98`

Сценарии описаны в `TESTING.md`.

---

# F. Каталог интеграционных событий

## EVT-001 — `TestRevisionPublishedV1`

- **Приоритет:** P1 when consumer exists
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

Должно быть явным `IIntegrationEvent`; сырая сериализация агрегата запрещена.

## EVT-002 — `TestAssignedV1`

- **Приоритет:** P1 when notification/integration consumer exists
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

## EVT-003 — `AttemptCompletedV1`

- **Приоритет:** P1 for analytics/notifications
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

Предпочтителен стабильный внешний контракт завершения, а не публикация внутренних классов событий отправки и таймаута.

## EVT-004 — Политика версионирования схемы интеграционных событий

- **Приоритет:** P1 before first external consumer
- **Трудоёмкость:** S/M
- **Статус:** ЗАПЛАНИРОВАНО

## EVT-005 — Эталонная реализация дедупликации у потребителя и тестовый стенд

- **Приоритет:** P2
- **Трудоёмкость:** M
- **Статус:** ЗАПЛАНИРОВАНО

---

# G. Продуктивность авторинга

## AUTHOR-001 — Эндпоинт валидации черновика

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ЗАПЛАНИРОВАНО

Возвращает проблемы публикации, не меняя статус.

## AUTHOR-002 — Клонирование теста

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ЗАПЛАНИРОВАНО

Создаёт новый черновик со скопированным содержимым и новыми ID согласно явной политике.

## AUTHOR-003 — Создание черновика из опубликованной ревизии

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ЗАПЛАНИРОВАНО

Полезно для ветвления и копирования исторической версии.

## AUTHOR-004 — Tags

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ/PLANNED

Требуется нормализованное хранение тегов, индекс и фильтрация.

## AUTHOR-005 — Category/subject

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

## AUTHOR-006 — Расширенные фильтры и сортировка каталога

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Зависимости:** AUTHOR-004/005 as selected

## AUTHOR-007 — Версионированный экспорт в JSON

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ЗАПЛАНИРОВАНО

Не экспортировать напрямую внутреннюю схему сущностей EF.

## AUTHOR-008 — Версионированный импорт из JSON

- **Приоритет:** P1
- **Трудоёмкость:** L
- **Зависимости:** AUTHOR-007

Полная валидация до сохранения; частичный импорт не допускается.

## AUTHOR-009 — Импорт и экспорт CSV для вопросов с выбором

- **Приоритет:** P2
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

Только если бизнес-пользователям нужен сценарий работы с таблицами.

## AUTHOR-010 — Банк вопросов

- **Приоритет:** P1/P2
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

Ключевое решение: копия или живая ссылка. Рекомендация: авторинг может ссылаться или копировать, опубликованная ревизия всегда делает снимок.

---

# H. Поведение оценивания

## ASMT-001 — Перемешивание вариантов ответа

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

Порядок предъявления должен быть детерминированным и сохраняться для попытки, если разбор результата обязан воспроизводить интерфейс.

## ASMT-002 — Перемешивание вопросов

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

## ASMT-003 — Пулы вопросов

- **Приоритет:** P1/P2
- **Трудоёмкость:** L/XL
- **Статус:** РЕШЕНИЕ

Попытка обязана сохранять снимок выбранных ID вопросов.

## ASMT-004 — Стратегия частичного оценивания

- **Приоритет:** P1
- **Трудоёмкость:** L
- **Статус:** РЕШЕНИЕ

Ревизия сохраняет снимок стратегии и версии оценивания.

## ASMT-005 — Отрицательные баллы

- **Приоритет:** P2
- **Трудоёмкость:** M/L
- **Статус:** РЕШЕНИЕ

Требует явной политики минимального балла и округления.

## ASMT-006 — Числовой ответ

- **Приоритет:** P1
- **Трудоёмкость:** L
- **Статус:** РЕШЕНИЕ

Нужны правила допуска и нормализации.

## ASMT-007 — Автосопоставление короткого текста

- **Приоритет:** P2
- **Трудоёмкость:** L
- **Статус:** РЕШЕНИЕ

Сложность нормализации и локализации.

## ASMT-008 — Ручная проверка свободного текста

- **Приоритет:** P1/P2
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

Требует нового жизненного цикла проверки попытки и прав записи для рецензента.

## ASMT-009 — Вопрос на упорядочивание

- **Приоритет:** P2
- **Трудоёмкость:** L
- **Статус:** РЕШЕНИЕ

## ASMT-010 — Вопрос на сопоставление

- **Приоритет:** P2
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

## ASMT-011 — Attachments/images

- **Приоритет:** P2
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

Требует объектного хранилища, сканирования, подписанного доступа и контроля безопасности контента.

## ASMT-012 — Форматированный текст и Markdown

- **Приоритет:** P1/P2
- **Трудоёмкость:** M/L
- **Статус:** РЕШЕНИЕ

Требует политики рендеринга и санитизации во фронтенде.

---

# I. Интерфейс и жизненный цикл попытки

## ATT-010 — DTO представления попытки

- **Приоритет:** P1 when frontend starts
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

`GET /api/v1/attempts/{id}/presentation` возвращает `AttemptPresentationView`: вопросы/варианты из immutable revision привязанной попытки, сохранённые ответы студента, `status`/`deadlineAt`/`serverTime`. Признака корректности нет ни на одном уровне DTO — граница закреплена тестом на сериализованном HTTP-ответе, а не только по полям (ADR-028). Существующий `GET /attempts/{id}` не менялся, чтобы не ломать v1 contract.

## ATT-011 — Возобновление активной попытки

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

`GET /api/v1/assignments/{id}/attempts/active` отдаёт попытку студента в статусе `InProgress` для этого assignment или `404`, если возобновлять нечего. Клиент, потерявший `attemptId`, возобновляет работу вместо старта новой попытки (что израсходовало бы attempt limit). Завершённые попытки через resume не отдаются — они читаются через `/result`.

## ATT-012 — Явные метаданные старта попытки

- **Приоритет:** P2
- **Трудоёмкость:** S/M
- **Статус:** РЕШЕНИЕ

Метаданные клиента и устройства — только при наличии требований приватности или бизнеса.

## ATT-013 — Пауза и возобновление отсчёта

- **Приоритет:** P3
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

Несовместимо с текущей простой семантикой дедлайна без перепроектирования домена.

## ATT-014 — Пакетное автосохранение

- **Приоритет:** P2
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

Текущий PUT на каждый вопрос уже допускает повтор за счёт параллельного доступа к агрегату, но не имеет ключа идемпотентности.

## ATT-015 — Отказ от попытки

- **Приоритет:** P2
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

Нужно явно определить влияние на лимит попыток и статистику результатов.

---

# J. Возможности назначений

## ASN-010 — Шаблон назначения

- **Приоритет:** P1
- **Трудоёмкость:** L
- **Статус:** РЕШЕНИЕ

Переиспользуемая конфигурация ревизии, окна, лимита и целей.

## ASN-011 — Сущность кампании или пакета

- **Приоритет:** P1/P2
- **Трудоёмкость:** L
- **Статус:** РЕШЕНИЕ

Полезно, если массовым назначениям нужен единый жизненный цикл и отчётность.

## ASN-012 — Отложенные назначения на будущее

- **Приоритет:** P1
- **Трудоёмкость:** S/M
- **Статус:** ЗАПЛАНИРОВАНО

Текущее поле `AvailableFrom` уже поддерживает доступность в будущем; фича добавляет главным образом семантику интерфейса, запросов, фильтрации и планирования.

## ASN-013 — Политика предотвращения дублей назначений

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

Сейчас система допускает несколько назначений одной ревизии на одну цель с разными ID. Нужно решить, намеренно ли это.

## ASN-014 — Семантика членства в группе

- **Приоритет:** P1
- **Трудоёмкость:** L
- **Статус:** РЕШЕНИЕ

Choose:

- динамическое на момент доступа (текущее);
- снимок членства на момент назначения.

## ASN-015 — Повторное назначение после завершения

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** РЕШЕНИЕ

Уточнить, что считается каноническим: новое назначение или изменение лимита попыток.

## ASN-016 — Метаданные напоминаний по назначению

- **Приоритет:** P2
- **Трудоёмкость:** M
- **Зависимости:** notification integration

---

# K. Reporting/analytics

## REP-010 — Сводный дашборд по тесту

- **Приоритет:** P1
- **Трудоёмкость:** L
- **Статус:** ЗАПЛАНИРОВАНО

Metrics:

- attempts;
- доля завершения;
- доля прохождения;
- средний балл;
- средняя длительность.

## REP-011 — Сравнение ревизий

- **Приоритет:** P2
- **Трудоёмкость:** L
- **Статус:** РЕШЕНИЕ

Никогда не смешивать результаты разных ревизий без явной группировки.

## REP-012 — Сложность вопроса

- **Приоритет:** P1/P2
- **Трудоёмкость:** L
- **Статус:** ЗАПЛАНИРОВАНО after sufficient data

Группировка по `(RevisionId, QuestionId)`.

## REP-013 — Распределение ответов

- **Приоритет:** P2
- **Трудоёмкость:** L
- **Статус:** ЗАПЛАНИРОВАНО

Только для рецензента и администратора; раскрытие правильности требует осторожности.

## REP-014 — Экспорт результатов в CSV

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ЗАПЛАНИРОВАНО

Авторизация + аудит + потоковый ответ.

## REP-015 — Проекционные таблицы аналитики

- **Приоритет:** P2
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

Только после измерений запросов и нагрузки.

## REP-016 — Внешнее хранилище данных

- **Приоритет:** P3
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

Требует каталога интеграционных событий и реальной потребности в масштабе аналитики.

---

# L. Notifications/integrations

## NOTIF-001 — Контракт уведомления о создании назначения

- **Приоритет:** P1/P2
- **Трудоёмкость:** M
- **Зависимости:** EVT-002
- **Статус:** РЕШЕНИЕ

## NOTIF-002 — Планировщик напоминаний о дедлайне

- **Приоритет:** P2
- **Трудоёмкость:** L
- **Статус:** РЕШЕНИЕ

Должен создавать событие или задание, а не отправлять письмо внутри транзакции.

## NOTIF-003 — Уведомление о завершении и результате

- **Приоритет:** P2
- **Трудоёмкость:** M/L
- **Зависимости:** EVT-003

## INT-001 — Адаптер доставки webhook

- **Приоритет:** P2
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

Только если внешние потребители не могут читать RabbitMQ.

## INT-002 — Подписи и повторы webhook

- **Приоритет:** P2
- **Трудоёмкость:** L
- **Зависимости:** INT-001

---

# M. Workspace/multi-tenancy

## TEN-001 — Агрегат Workspace

- **Приоритет:** P1/P2 depending deployment
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

## TEN-002 — Членство в Workspace

- **Приоритет:** same
- **Трудоёмкость:** XL
- **Зависимости:** TEN-001, ID-001 likely

## TEN-003 — Роли в области Workspace

- **Приоритет:** same
- **Трудоёмкость:** L

## TEN-004 — Изоляция данных Workspace

- **Приоритет:** P0 if multi-tenant
- **Трудоёмкость:** XL

Каждый запрос чтения и записи обязан учитывать workspace и быть проиндексирован.

## TEN-005 — Аудит и Outbox с учётом арендатора

- **Приоритет:** P1
- **Трудоёмкость:** L

---

# N. Готовность фронтенда и клиентов

## UX-001 — API представления теста для студента

- **Приоритет:** P1
- **Трудоёмкость:** M/L
- **Статус:** ГОТОВО — закрыт вместе с ATT-010/ATT-011

Student-safe question/options DTO + текущие ответы + deadline реализованы; см. ATT-010, ATT-011 и ADR-028.

## UX-002 — Эргономика API редактирования для автора

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ЗАПЛАНИРОВАНО

Может включать пакетное переупорядочивание и обновление, чтобы снизить болтливость интерфейса.

## UX-003 — Генерация типизированного клиентского SDK

- **Приоритет:** P2
- **Трудоёмкость:** M
- **Зависимости:** stable enriched OpenAPI

## UX-004 — Фронтенд-приложение

- **Приоритет:** product-dependent
- **Трудоёмкость:** XL
- **Статус:** РЕШЕНИЕ

Не входит в текущий baseline репозитория.

---

# O. Опыт разработчика

## DEV-001 — Централизованное управление пакетами

- **Приоритет:** P2
- **Трудоёмкость:** S/M
- **Статус:** ЗАПЛАНИРОВАНО

Ввести `Directory.Packages.props`, если число пакетов продолжит расти.

## DEV-002 — Стандартный инструментарий миграций EF и снимок модели

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО

Repeatable `dotnet ef` workflow реализован через `AppDbContextDesignFactory`; PostgreSQL baseline имеет designer metadata и `AppDbContextModelSnapshot`.

## DEV-003 — Тесты архитектурных зависимостей

- **Приоритет:** P1
- **Трудоёмкость:** M
- **Статус:** ГОТОВО — assembly reference direction baseline

Автоматизировать запрет ссылок Domain на EF/API и ограничения ссылок между слоями.

## DEV-004 — CI форматирования и анализаторов

- **Приоритет:** P2
- **Трудоёмкость:** S/M
- **Статус:** ЗАПЛАНИРОВАНО

## DEV-005 — Changelog и release notes по соглашению

- **Приоритет:** P1 before 1.0
- **Трудоёмкость:** S
- **Статус:** ГОТОВО

`CHANGELOG.md` (repo root) реализован по формату Keep a Changelog + SemVer, с явно задокументированным отклонением: до первого git-тега версии совпадают с фазами `docs/ROADMAP.md` (`0.8.x` … `0.9.4`), а не с датами. Записи покрывают Phase A–D7 (включая все `STAB-001..008` и `API-009`), плюс открытый `[Unreleased]` раздел с оставшимися Phase E gaps. Conventional Commits prefixes рекомендованы (не обязательны) начиная с `1.0.0`.

Отдельно от этой задачи остаётся rollback/forward-fix release runbook (ROADMAP.md item 10, вторая половина) — он требует staging rehearsal и не закрывается документацией changelog.

---

# P. Рекомендуемый порядок реализации

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

- актор и use case;
- бизнес-результат;
- область авторизации;
- владение агрегатом и изменения состояния;
- контракт API;
- влияние на хранилище;
- семантика повторов, идемпотентности и параллельного доступа;
- влияние на чувствительные данные;
- план тестирования;
- migration/backfill plan, если schema меняется.

# R. Definition of Done для feature

- code реализован по layer boundaries;
- доменные инварианты покрыты модульными тестами;
- PostgreSQL integration tests при persistence change;
- сквозной HTTP-сценарий: позитивный и негативный по авторизации;
- concurrency/idempotency test если применимо;
- student correctness boundary проверен;
- OpenAPI актуален;
- `docs/` обновлены;
- полный CI зелёный;
- production-образ собирается;
- production-образ успешно выполняет `--migrate`;
- feature status в этом файле изменён на `DONE`.
