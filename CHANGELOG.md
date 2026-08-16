# Changelog

Все заметные изменения TestApp фиксируются в этом файле.

Формат основан на [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), версионирование — на [Semantic Versioning](https://semver.org/). Один нюанс: до первого тегированного релиза версии здесь совпадают с этапами `docs/ROADMAP.md` (`0.8.x` … `1.0.0`), а не с фактическими датами публикации — тегов `git` для них пока не создавалось, поэтому вместо даты у каждой версии указана фаза дорожной карты. С `1.0.0` версии становятся полноценными release-тегами с датой.

Коммиты в этой ветке исторически не следовали Conventional Commits. Начиная с `1.0.0` рекомендуется префиксовать commit-сообщения (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`) — это не обязательное правило CI, но облегчает будущее ведение этого файла.

## [Unreleased] — на пути к `1.0.0` (Phase E)

Phase D7 (`0.9.4`, correctness/stabilization) полностью закрыта, `0.9.5` закрыл coverage/review pass, `0.9.6` — student presentation/resume (Phase F0 досрочно). Открытые пункты 1.0 release gate (`docs/ROADMAP.md` Phase E) вне этой ветки кода:

- staging restore drill с измеренным RTO;
- маршрутизация алертов в actionable destination (Alertmanager + pager/chat) и alert drill против staging;
- активный readiness prober для `/health/ready` (например `blackbox_exporter`);
- rollback/forward-fix release rehearsal;
- staging capacity target;
- формальная фиксация API v1 contract freeze.

### Added

- `CHANGELOG.md` (этот файл) — `DEV-005`.
- Prometheus + Grafana в `compose.yaml` как local/CI observability backend позади OTel Collector: provisioned dashboard (`TestApp Overview`) и alert rule expressions, реализующие весь технически выразимый контракт `docs/SLO_ALERTS.md` §4/§6 — `OBS-012`, `OBS-013`, ADR-027. Новый CI workflow `observability` и `scripts/validate-observability-stack.sh` держат стек в проверенном состоянии.

## [0.9.6] — Student-safe attempt presentation и resume

### Added

- `GET /api/v1/attempts/{id}/presentation` — всё, что нужно студенту для прохождения попытки: вопросы и варианты из **immutable revision**, к которой привязана попытка, собственные сохранённые ответы, `status`/`deadlineAt`/`completedAt`, `passingPercentage`/`timeLimitMinutes` и `serverTime` для обратного отсчёта без зависимости от часов клиента (`ATT-010`, `UX-001`).
- `GET /api/v1/assignments/{id}/attempts/active` — попытка студента в статусе `InProgress` для этого assignment или `404`. Клиент, потерявший `attemptId`, возобновляет работу вместо старта новой попытки, которая израсходовала бы attempt limit (`ATT-011`).

Оба endpoint'а — новые ресурсы; форма существующего `GET /api/v1/attempts/{id}` не менялась, чтобы не ломать v1 contract перед его заморозкой.

### Security

- Presentation DTO не содержит признака правильности ни на одном уровне. Это **свойство проекции, а не хранилища**: revision хранит `IsCorrect` в том же `jsonb`-столбце, который читается для presentation, поэтому граница закреплена тестом на сериализованных байтах HTTP-ответа (текстовая и структурная проверка), а не только assert'ами по полям DTO. Тест верифицирован красным — временное добавление `IsCorrect` в student DTO его роняет. См. ADR-028.
- Presentation и resume доступны только владельцу попытки; чужая попытка отвечает `404`, а не `403`, чтобы не подтверждать её существование.

Документация: `docs/API.md` §14, `docs/USER_GUIDE.md` §4.3/§4.3.1 (раздел «ограничение реализации» заменён рабочей инструкцией), ADR-028.

## [0.9.5] — Test coverage and review pass before 1.0 RC

### Fixed

- `ReorderQuestion`/`ReorderAnswerOption` возвращали `409 order_duplicate` вместо `404 not_found`, если переупорядочиваемый ID не существует, а целевой порядок занят — проверка существования теперь выполняется первой.
- Смена типа вопроса `MultipleChoice -> SingleChoice` больше не оставляет несколько правильных вариантов: это состояние уже запрещено при добавлении/изменении варианта, а теперь отклоняется и на пути смены типа (`409 test.single_choice.multiple_correct`), а не всплывает позже при публикации.
- Audit trail pagination (`GET /api/v1/operations/audit`) получил детерминированный tie-breaker (`OccurredAt`, затем `Id`) — тот же дефект, что `STAB-006` закрыл в read models, но пропущенный в audit. Записи могли дублироваться или пропадать между страницами при равных timestamp.
- `Result.TryGetError` аннотирован `[MaybeNullWhen(false)]`: out-параметр объявлен non-nullable, но на success равен `null`, из-за чего неправильное использование не ловилось анализом nullable-состояния.

### Changed

- Роль, определяющая global vs owner-scoped видимость данных, была продублирована в четырёх местах (три — hardcoded string literal). Консолидирована в `TestApp.Application/Common/ActorScope.cs`: частичное переименование роли теперь невозможно — оно бы молча расширило или сузило доступ автора к чужим данным.

### Added

- Новый test project `tests/TestApp.Core.Tests` для `Result<TSuccess, TFailure>` — failure contract, через который выражены все domain/application правила.
- Domain suites: `TestAuthoringInvariantTests`, `AttemptLifecycleTests`, `AssignmentLifecycleTests`, `PublishedRevisionScoringTests`.
- Application suites: `AuthoringCommandTests`, `AssignmentAndAttemptCommandTests` (ownership, If-Match precondition, отсутствие commit при отклонённой команде).
- Integration suites: `StudentReadModelQueryTests` (`/api/v1/me/*` и attempt detail/result — были 0%), `AuditTrailPaginationTests`.
- `coverlet.collector` во всех test projects, чтобы покрытие было воспроизводимо.

Итог: 98 -> 267 тестов, line coverage 81.1% -> 90.3% (Core 47.5% -> 96.7%, Domain 75.8% -> 93.8%, Application 67.4% -> 90.3%). Подробности и оставшиеся gaps — `docs/TESTING.md` §2 и §14.

## [0.9.4] — Phase D7: Correctness/stabilization fixes

### Fixed

- `StartAttempt` теперь ищет уже созданную попытку данного actor/key до повторной проверки изменяемых условий назначения (окно доступности, членство в группе) — повтор того же пользователя с тем же ключом идемпотентности корректно возвращает прежнюю попытку после отмены/истечения назначения или изменения группы (`STAB-001`).
- Audit-запись сохраняет итоговый HTTP-статус после обработки исключения (`400`/`409`/...), а не промежуточный `500` (`STAB-002`).
- `OutboxProcessor` и `OverdueAttemptProcessor` переживают отдельный сбой одного poll-цикла (например, временная недоступность БД) — сбой логируется, и воркер продолжает работу на следующем тике вместо падения всего API-процесса (`STAB-003`).
- `--migrate` больше не требует Keycloak/RabbitMQ/CORS/rate-limit конфигурацию — загружается только database-конфигурация, так что job миграции не должен получать лишние секреты (`STAB-005`).
- Все paginated read model (assignments, attempts, reviewer results) используют детерминированный порядок (`timestamp`, затем `Id`) — устраняет риск дублей/пропусков при постраничном обходе записей с одинаковым timestamp, например после bulk-назначения (`STAB-006`).
- Reviewer/admin запросы (`GetAssignmentsAsync`, `GetReviewerResultsAsync`) больше не загружают в память весь набор подходящих ID перед фильтрацией — testId/ownerId выражены как SQL `EXISTS`, revisionId сравнивается напрямую с колонкой (`STAB-007`).
- Ошибки валидации домена (`Test`, `TestAssignment`) больше не могут утечь как сырое CLR-исключение с текстом вида `"... (Parameter 'x')"` в публичный `ProblemDetails.detail` — все достижимые через API инварианты единообразно возвращаются как `Result<T, DomainError>` (`STAB-008`, см. `docs/DECISIONS.md` ADR-026).

### Added

- Publish/start-attempt/submit-attempt принимают настоящий zero-length HTTP body при валидном заголовке `Idempotency-Key` — легаси JSON-тело больше не обязательно (`API-009`).

## [0.9.3] — Phase D: Operational reliability

### Added

- Logical PostgreSQL backup/restore с verification в изолированной базе, CI restore drill (D1).
- Типизированная retention-конфигурация для audit/idempotency/processed Outbox записей с bounded batch delete (D2).
- Admin-only dead-letter management: safe detail, explicit requeue/discard с обязательной причиной и audit (D3).
- `TestApp.Operations` метрики: Outbox publish/lag, dead-letter actions, attempt expiration, retention — плюс `docs/SLO_ALERTS.md` с первым alerting contract (D4).
- Отдельный `security` CI workflow: secret scan, HIGH/CRITICAL сканирование production-образа, CycloneDX SBOM (D5).
- `performance` CI workflow: authenticated k6-сценарии против production-shaped stack (API + PostgreSQL + RabbitMQ + Keycloak), пороги p95 по HTTP-сценариям и by worker recovery (expiration storm, Outbox backlog) (D6).

## [0.9.2] — Phase C: API contract maturity

### Added

- Единый contract `Idempotency-Key` header с legacy body-совместимостью, header/body mismatch → `409`, SHA-256 request fingerprint для повторного запроса с другим payload → `409 idempotency.key_reused`.
- HTTP optimistic concurrency для `Test`: `ConcurrencyVersion` + strong `ETag`, обязательный `If-Match` на мутациях (`428`/`400`/`412`).
- Единая транспортная валидация: `400 request.invalid` для malformed input, `400 request.validation` со структурированными ошибками для DataAnnotations/enum нарушений.
- Обогащённый OpenAPI: Bearer security scheme, стабильные operation ID, ProblemDetails-схемы, enum/examples, serialized contract test.
- Управляемый lifecycle legacy `/api/*`: enable/disable, `Deprecation`/`Sunset` заголовки, `410 api.version.retired` после retirement.

## [0.9.1] — Phase B: Resource ownership

### Added

- Обязательный immutable `Test.OwnerId` из Keycloak `sub`.
- Owner-scoped SQL-фильтрация каталога/редактора/revision list/reviewer результатов для `test-author`; global scope для `test-admin`.
- Safe legacy backfill (`__legacy_admin_only__`) для существующих записей при миграции модели владения.

## [0.9.0] — Phase A: Production configuration & edge security

### Added

- Fail-fast production-конфигурация: обязательный `ConnectionStrings:Database` вне Development, запрет startup-миграций вне Development, `--migrate` как отдельный режим.
- Explicit CORS allow-list (без wildcard), настраиваемая HTTPS/HSTS политика, security headers, отключённый Kestrel server banner.
- Class-based rate limiting (general/student-write/privileged-read/operations).
- Production OpenAPI отключён по умолчанию / опционально защищён ролью.
- High/critical NuGet audit gate в CI.

## [0.8.x] — Core architecture baseline

### Added

- Modular monolith на .NET / DDD / Clean Architecture / CQRS.
- `Test` aggregate: авторинг, `SingleChoice`/`MultipleChoice`, публикация с валидацией, exact-set scoring.
- Immutable `PublishedTestRevision`.
- User/group assignments: окно доступности, лимит попыток, bulk assignment.
- Attempt lifecycle: start/answer/clear/submit/timeout.
- Reviewer/admin/student read model.
- Транзакционный Outbox + RabbitMQ transport.
- Keycloak authentication/roles/groups.
- Docker/Compose с migration-only режимом запуска.
- Интеграционные тесты против реальных PostgreSQL/RabbitMQ.

[Unreleased]: https://github.com/hhypest/TestApp/compare/beta-ddd...HEAD
