# Текущее состояние проекта

> Статус: **Implemented snapshot** ветки `beta-ddd`, 2026-08-20. Phase A–D7 полностью реализованы (STAB-001..009, API-009, PostgreSQL migration, EF Core 10 upgrade), плюс observability backend и pre-RC release/staging tooling. Консервативный exact-head baseline на 341 тесте — 90.58% line coverage; workflow `dotnet` требует не менее 90.00%. Exact-head evidence определяется последними GitHub Actions runs ветки.

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
- неизменяемая история ревизий;
- архивирование;
- owner-scoped catalog, editor и revision list;
- глобальная область видимости администратора.

### 2.2 Модель публикации

- `Draft -> Published`;
- изменение опубликованного working definition возвращает его в `Draft`;
- PublishedTestRevision неизменяема;
- повторная публикация без изменений запрещена;
- Archived test заблокирован для изменения/публикации;
- revision snapshot содержит title/settings/questions/options/correctness.

### 2.3 Граница владения

`Test.OwnerId` является обязательным non-null external user ID.

Для `test-author` SQL/read и Application/write boundary ограничены owner:

- catalog;
- editor;
- список ревизий;
- rename/settings/questions/options;
- publish/archive;
- список и детали результатов для рецензента.

`test-admin` имеет global scope.

Владелец задаётся только из `sub` вызывающего. Специальный owner `__legacy_admin_only__` для существующих записей применялся в миграциях до их схлопывания в единственную baseline-миграцию; сейчас `OwnerId` — обязательная колонка без значения по умолчанию, поэтому запись с таким владельцем не может появиться. Удерживается тестом `CrossAuthorIsolationTests.No_owner_can_exist_that_the_application_never_issued`.

### 2.4 Assignments

- назначение конкретной immutable revision;
- target user или group;
- окно доступности;
- необязательный лимит попыток;
- аудит отмены;
- изменение window/attempt-limit;
- bulk assignment до 500 targets;
- дедупликация целей;
- список, детали и статистика для администратора;
- student visibility по direct target или group claims.

### 2.5 Attempts

- проверка цели, доступности и лимита попыток;
- идемпотентный старт;
- deadline из revision snapshot;
- answer/clear/submit;
- владение попыткой;
- фоновое истечение;
- `Submitted`/`TimedOut`;
- `Passed`/`Failed`;
- оценивание по точному совпадению набора;
- детализация правильности для рецензента;
- student-safe result без correct flags;
- student-safe presentation попытки (вопросы/варианты из immutable revision + собственные ответы + `serverTime`) и resume активной попытки — `ATT-010`/`ATT-011`/`UX-001`, ADR-028.

## 3. Persistence и consistency

- PostgreSQL **18** как baseline для runtime и CI;
- EF Core 10.0.11 + провайдер Npgsql EF 10.0.3;
- сгенерированная базовая миграция PostgreSQL + снимок модели;
- production `--migrate` mode;
- startup migration разрешена только Development;
- optimistic concurrency через `ConcurrencyVersion`;
- `DbUpdateConcurrencyException -> ConcurrencyConflictException`;
- нормализованные ответы попытки;
- неизменяемый JSON-снимок вопросов ревизии;
- session-level PostgreSQL advisory locks для common idempotency, Outbox и retention.

## 4. Оптимистичный контроль параллельного доступа по HTTP

Для mutable authoring resource `Test` реализован HTTP precondition contract.

`GET /api/v1/tests/{id}/editor`:

- возвращает `ConcurrencyVersion`;
- выставляет strong `ETag`, например `"3"`.

Mutating endpoints существующего `Test` требуют `If-Match`:

- rename/settings;
- CRUD и переупорядочивание вопросов и вариантов;
- publish;
- archive.

Semantics:

- отсутствует `If-Match` -> `428 concurrency.precondition_required`;
- некорректный, слабый или wildcard-валидатор -> `400 concurrency.if_match`;
- устаревший ETag -> `412 concurrency.precondition_failed`;
- valid current ETag -> use case выполняется;
- EF concurrency token остаётся финальным race guard между precondition check и commit.

## 5. Idempotency

### 5.1 HTTP-контракт

Основной public contract — header:

```text
Idempotency-Key: <UUID>
```

Legacy body `idempotencyKey` временно поддерживается.

Если оба заданы и различаются:

```text
400 idempotency.key_mismatch
```

### 5.2 Отпечаток запроса

Persistent idempotency rows содержат SHA-256 fingerprint логического request payload.

- одинаковый key + тот же request -> replay прежнего result;
- одинаковый key + другой payload -> `409 idempotency.key_reused`;
- historical rows с NULL fingerprint сохраняют compatibility replay;
- publish operation resource-scoped по TestId.

Fingerprint используется publish/single assignment/bulk assignment/submit. Start attempt дополнительно защищён DB unique key `(AssignmentId, UserId, StartRequestId)` и PostgreSQL advisory lease на `(AssignmentId, UserId)` вокруг replay/count/insert transaction.

Текущий transport resolver принимает key из header и legacy body. Publish/start/submit принимают настоящий zero-length body (Minimal API request DTO параметр nullable), когда key находится только в header.

Actor-scoped lookup уже созданного start attempt выполняется до загрузки assignment и mutable availability/group-membership checks. Поэтому retry того же пользователя с тем же key возвращает прежний attempt ID после cancellation, expiry или изменения group claim. Новый key по-прежнему проходит все актуальные eligibility checks; транзакционная повторная проверка в repository сохраняет race safety.

## 6. Authentication/authorization

- JWT Bearer через Keycloak;
- `MapInboundClaims=false`;
- `sub` — внешняя идентичность;
- `roles` — claim ролей;
- `groups` — членство в группах;
- policies `tests:write`, `tests:publish`, `tests:assign`, `results:review`, `operations:read`;
- coarse role checks дополняются owner/attempt/assignment business authorization в Application/read side.

## 7. Production-конфигурация и защита периметра

Phase A реализована.

### Конфигурация с ранним отказом

Вне окружения Development:

- `ConnectionStrings:Database` обязателен;
- startup migrations запрещены;
- Keycloak config обязателен для HTTP host;
- HTTPS metadata ожидается по умолчанию;
- RabbitMQ config валидируется при enabled transport;
- attempt expiration/outbox/rate limits валидируются;
- HTTPS redirect/HSTS deployment decision должен быть явным.

Migration-only `--migrate` загружает только database configuration (`RuntimeConfiguration.LoadDatabase`); Keycloak/RabbitMQ/worker/CORS/rate-limit/OpenAPI/transport-security/reverse-proxy loaders и связанные DI-регистрации пропускаются, так что независимый production migration job не должен получать эти secrets/config.

### Обратный прокси

- ForwardedHeaders включаются явно;
- explicit `KnownProxies/KnownNetworks`;
- ForwardLimit;
- untrusted X-Forwarded-* игнорируется.

### CORS и транспортные заголовки

- CORS только explicit allow-list;
- wildcard origin запрещён;
- необязательная передача учётных данных;
- настраиваемые перенаправление на HTTPS и HSTS;
- Kestrel server header выключен;
- базовые заголовки безопасности: `nosniff`, `DENY`, `no-referrer`, Permissions-Policy, ограничительный CSP.

### Ограничение частоты запросов

Классы, задаваемые конфигурацией:

- general;
- student-write;
- privileged-read;
- operations.

Partition key = authenticated `sub`, иначе trusted remote IP.

### Публикация OpenAPI

- Development: enabled/public по умолчанию;
- Production: disabled по умолчанию;
- если Production OpenAPI включён с `AllowAnonymous=false`, требуется `operations:read`.

## 8. Audit/correlation/observability

- канонический `/api/v1/*` + переписывание легаси-путей;
- configurable legacy lifecycle с `Deprecation`, optional `Sunset` и `410 api.version.retired`;
- ProblemDetails;
- `X-Correlation-ID`;
- durable audit для state-changing HTTP methods без body/query/secrets;
- OpenTelemetry для ASP.NET Core/HttpClient/runtime;
- custom `TestApp.Operations` metrics для Outbox, expiration, retention и bounded API exception categories;
- необязательный экспортер OTLP;
- Prometheus + Grafana в `compose.yaml` как local/CI metrics backend позади OTel Collector: dashboard (`TestApp Overview`) и alert rule expressions (`deploy/prometheus/alerts.yml`, все технически выразимые правила `docs/SLO_ALERTS.md` §4) provisioned и CI-validated (ADR-027, `OBS-012`/`OBS-013`);
- `/health/live`;
- `/health/ready` PostgreSQL + RabbitMQ при enabled transport.

`CorrelationAuditMiddleware` оборачивает exception handler и сохраняет финальный status state-changing response. Контракт покрыт real HTTP/PostgreSQL regression tests для `400`, `409`, `412` и `500`.

## 9. Events / Outbox / RabbitMQ

- `IDomainEvent` — внутреннее уведомление;
- только explicit `IIntegrationEvent` сохраняется в Outbox;
- транспорт RabbitMQ включается явно;
- подтверждения публикации;
- durable topic-обменник и устойчивые сообщения;
- EventId -> MessageId;
- доставка at-least-once;
- экспоненциальные повторы с задержкой;
- состояние dead-letter;
- admin-only safe dead-letter detail/requeue/discard с mandatory reason и action audit;
- advisory-блокировка PostgreSQL на каждое сообщение;
- эксплуатационный эндпоинт мониторинга Outbox;
- готовность RabbitMQ.

Production-каталог интеграционных событий явно заморожен пустым до появления реального
потребителя (ADR-033, `IntegrationEventCatalogTests`). Готовность транспорта не означает
автоматическую публикацию domain events и не является обязательством по будущей схеме.

Per-message publish failures обрабатываются, и cycle-level failures (batch query/advisory lock/failure-state persistence) больше не могут вывести `BackgroundService` из `PeriodicTimer` loop: `OutboxProcessor`/`OverdueAttemptProcessor` логируют cycle failure и продолжают на следующий poll tick (`RunCycleAsync`).

## 10. Deployment / CI

- многоэтапный production Docker-образ;
- запуск не от root;
- Compose: API, PostgreSQL 18, RabbitMQ, Keycloak, контейнер миграций, OTEL Collector, Prometheus, Grafana;
- CI использует реальные PostgreSQL/RabbitMQ;
- проверка версии PostgreSQL 18 во время выполнения;
- restore/build/tests;
- гейт уязвимостей NuGet уровня high/critical;
- валидация Compose;
- сборка production-образа;
- `--migrate` из production image;
- логическая резервная копия + проверка восстановления в изолированной базе;
- сканирование репозитория на секреты;
- сканирование production-образа на уязвимости HIGH/CRITICAL;
- артефакт SBOM в формате CycloneDX;
- importable Postman Collection v2.1 с real Keycloak author/admin/student flow;
- Newman API contract workflow с JUnit artifact;
- аутентифицированный k6 + workflow ёмкости истечения попыток и Outbox.
- триггеры пяти workflow покрывают `beta-ddd`, `master` и — для `dotnet`/`security` — теги `v*`; три path-фильтрованных workflow запускаются на релизном коммите вручную, порядок сбора run ID см. `docs/OPERATIONS.md` §16.1.

## 11. Главные оставшиеся ограничения

### Stabilization/correctness

Все P0/P1 stabilization findings текущего backlog (STAB-001..009) закрыты: business rules, достижимые через application use case, возвращают `Result<T, DomainError>` (ADR-026), а value objects валидируют себя сами — strong identifiers отвергают `Guid.Empty`, `AttemptScore` требует `0 <= Earned <= Maximum`, `TestAttempt.Timeout` требует наступившего deadline (ADR-029, ADR-030).

Coverage/review pass `2026-08-16` (`0.9.5`) поднял line coverage 81.1% -> 90.3% на тогдашнем наборе из 267 тестов и закрыл четыре дефекта, найденных при чтении непокрытых участков: порядок проверок в `ReorderQuestion`/`ReorderAnswerOption` (404 вместо 409), смена типа вопроса в обход инварианта single-choice, отсутствие tie-breaker в audit pagination (пропуск STAB-006) и nullable-аннотация `Result.TryGetError`. После расширения набора до 341 теста четыре exact-head Cobertura-отчёта объединены без двойного подсчёта: 5807/6411 строк, 90.58%. CI закрепляет floor 90.00% и падает при отсутствии отчёта. Роль, определяющая owner-scope, консолидирована в одном месте. Детали — `docs/TESTING.md` §2/§14 и `CHANGELOG.md`.

### Эксплуатационная надёжность

Реализованы repository-level D1–D6: logical backup/restore CI, retention cleanup, dead-letter management, metrics/SLO contract, security/SBOM workflow и PostgreSQL/RabbitMQ capacity gate. Restore CI публикует fail-closed JSON с SHA-256 архива, DB-level timing и результатами проверки; для полного staging RTO подготовлен изолированный application recovery harness: тот же immutable API image, чистые PostgreSQL/RabbitMQ volumes, readiness + business/idempotency/Outbox/audit/advisory-lock probes и публикация evidence только после cleanup. Это готовый инструмент, не свидетельство реального прогона. D6 evidence на `9916b98`: 8771/8771 checks, HTTP failure rate 0, expiration 1247 -> 0 за 14 s, Outbox 100 -> 0 за 1 s. Дополнительно реализован и CI-validated local/CI observability backend — Prometheus + Grafana в `compose.yaml` (dashboard + alert rule expressions, `OBS-012`/`OBS-013`, ADR-027). Все внешние Actions и container inputs исполняются по immutable SHA/digest; regression guard входит в `dotnet`.

Не завершены:

- реальный запуск staging/platform restore drill с измеренным RTO и приложенным evidence;
- drill доставки алертов на контуре с измеренным time-to-alert: пробер готовности и
  маршрутизация в Alertmanager настроены (ADR-034), но реальный receiver ещё не подтверждён
  и ни один page-алерт не доказан доставленным;
- хранилище секретов на стороне развёртывания, расписание резервных копий, PITR и политика внешнего хранения;
- репетиция релиза с откатом и исправлением вперёд.

### Идентичность и продуктовая широта

Не реализованы:

- multi-realm `(Issuer, Subject)`;
- модель Workspace/мультиарендности;
- frontend application (backend presentation/resume contract готов — `ATT-010`/`ATT-011`);
- типы вопросов, отличные от выбора варианта;
- частичное и настраиваемое оценивание;
- ручная проверка;
- бизнес-события уведомлений и их потребители;
- расширенная аналитика.

## 12. Текущий архитектурный уровень

Проект уже является production-oriented modular monolith core, а не CRUD prototype: domain invariants, immutable revisions, owner isolation, HTTP/DB concurrency, distributed idempotency, real infrastructure tests, durable Outbox и deployment path реализованы.

Точечные correctness/stability fixes (Phase D7, `STAB-001..008`) закрыты; release notes/changelog (`DEV-005`) и local/CI dashboard+alert rule expressions (`OBS-012`/`OBS-013`) тоже. До 1.0 остаётся прежде всего **deployment rehearsal**: выполнить уже автоматизированный staging restore drill и зафиксировать RTO, подтвердить реальный Alertmanager receiver + alert drill, повторить rollback/forward-fix. Student-safe attempt presentation/resume contract (первая продуктовая вертикаль) уже реализован досрочно — `ATT-010`/`ATT-011`/`UX-001`, ADR-028. Дальше по плану идут authoring productivity (`AUTHOR-001/002/003`, tags/catalog, versioned import/export); расширенные типы вопросов следует добавлять позже, по одной versioned vertical slice.
