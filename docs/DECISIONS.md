# Архитектурные решения TestApp

> Формат: компактный ADR register. `Accepted` означает действующее решение; `Transitional` — временный совместимый контракт; `Superseded` — историческое решение, заменённое новым текущим contract.

## ADR-001 — Modular monolith вместо микросервисов

**Статус:** принято.

**Решение:** единый deployable backend с внутренними проектными/модульными границами.

**Обоснование:** текущий domain тесно связан транзакциями publication/assignment/attempt; нет независимых команд/scale profiles, оправдывающих distributed complexity.

**Последствия:**

- одна PostgreSQL transactional boundary;
- проще consistency/migrations/testing;
- modules должны сохранять dependency discipline;
- service extraction только по реальному organizational/operational pressure.

## ADR-002 — Чистое направление зависимостей

**Статус:** принято.

**Решение:** Domain не зависит от Infrastructure/API. Application зависит от Domain abstractions, Infrastructure реализует adapters, API является composition/transport boundary.

**Последствия:**

- Keycloak/EF/RabbitMQ не проникают в Domain;
- domain tests быстрые;
- transport можно менять без изменения invariants.

## ADR-003 — DDD aggregates как write boundary

**Статус:** принято.

Агрегаты:

- Test;
- PublishedTestRevision;
- TestAssignment;
- TestAttempt.

**Решение:** business state transitions выполняются через aggregate methods, а не arbitrary EF property setters/CRUD service.

**Последствия:** invariants централизованы; optimistic concurrency привязана к aggregate mutation.

## ADR-004 — Неизменяемая PublishedTestRevision

**Статус:** принято.

**Решение:** publication создаёт immutable snapshot. Assignment/Attempt ссылаются на revision ID, не на mutable Test.

**Обоснование:** historical result должен воспроизводиться после дальнейшего редактирования test definition.

**Последствия:**

- correctness history сохраняется;
- editing Published Test возвращает working definition в Draft;
- storage содержит JSON snapshot questions/options.

## ADR-005 — Exact-set scoring для choice questions

**Статус:** текущее поведение принято; продуктовое расширение допускается позже.

**Решение:** question points начисляются только при полном совпадении selected set и correct set.

**Последствия:** simple deterministic scoring; partial credit отсутствует.

При добавлении partial scoring нужна новая явная scoring strategy, а не silent изменение исторической semantics.

## ADR-006 — Keycloak является source of truth identity

**Статус:** принято.

**Решение:** TestApp не создаёт локальных users/passwords. Domain хранит внешние identity IDs.

**Последствия:**

- authentication делегирована OIDC/Keycloak;
- system roles приходят claims;
- business authorization остаётся Application/Domain;
- multi-realm требует будущего `(issuer, subject)` identity key.

## ADR-007 — PostgreSQL как production persistence

**Статус:** принято.

**Решение:** PostgreSQL 18 как baseline для runtime и CI.

**История:** SQLite development provider был удалён при переходе на MariaDB; затем MariaDB contract заменён PostgreSQL, а migrations rebased на PostgreSQL baseline.

**Последствия:**

- persistence tests выполняются на real PostgreSQL;
- используются PostgreSQL advisory locks;
- CI проверяет `server_version_num >= 180000`;
- старые MariaDB volumes не удаляются и требуют отдельного ETL, если содержат значимые данные.

## ADR-008 — EF Core 10/Npgsql 10 при net10.0 application

**Статус:** принято.

**Решение:** Infrastructure использует EF Core 10.0.11 + `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3; integration tests используют Npgsql 10.0.3; приложение таргетирует .NET 10.

**Обоснование:** это актуальная stable major line, соответствующая .NET 10. Microsoft EF Core packages и `dotnet-ef` выровнены на одной patch-версии; provider и driver используют последнюю стабильную версию своей 10.x line. Preview EF/Npgsql 11 исключены из production baseline.

**Гейт совместимости:** repository проверяет отсутствие pending model changes, применение существующего baseline к пустой PostgreSQL 18 database, full integration suite и production-image migration path.

**Правило обновления:** следующий provider/EF major обновляется отдельным compatibility step с review breaking changes и full migration/integration suite.

## ADR-009 — CQRS без отдельной read database

**Статус:** принято.

**Решение:** commands работают через aggregates/repositories; queries используют отдельные read DTO/projections, но читают ту же PostgreSQL.

**Последствия:**

- CQRS separation без distributed consistency;
- `AsNoTracking`, фильтрация и постраничный вывод на стороне SQL;
- отдельный read store вводится только при измеренной необходимости.

## ADR-010 — Optimistic concurrency через aggregate version

**Статус:** принято.

**Решение:** `ConcurrencyVersion` — EF concurrency token, `Touch()` на mutation.

**Последствия:** lost update -> conflict/HTTP 409; клиент перечитывает state.

## ADR-011 — Идемпотентность на основе базы данных

**Статус:** принято.

**Решение:** retry-safe unsafe operations не полагаются на in-memory locks/cache.

Общие операции:

- устойчивая запись результата;
- session-level PostgreSQL `pg_try_advisory_lock` distributed lease с explicit `pg_advisory_unlock`;
- повторная проверка кэша внутри аренды.

Старт попытки:

- доменно-специфичный уникальный ключ запроса;
- PostgreSQL advisory lease на assignment/user + transaction для replay/count/insert.

**Последствия:** несколько API replicas имеют общую retry semantics.

## ADR-012 — Transactional Outbox только для explicit integration events

**Статус:** принято.

**Решение:** `IDomainEvent` не публикуется наружу автоматически. Только `IIntegrationEvent` сохраняется в Outbox.

**Обоснование:** external event schema является API contract и не должна случайно совпадать с internal domain notification.

**Последствия:** transport может быть готов до определения внешнего event catalog; это допустимо.

## ADR-013 — RabbitMQ и at-least-once delivery

**Статус:** принято.

**Решение:** транспорт RabbitMQ включается явно; гарантия доставки — at-least-once.

Механизмы:

- подтверждения публикации;
- устойчивые сообщения;
- EventId -> MessageId;
- retry/backoff/dead-letter;
- advisory-блокировки Outbox.

**Последствия:** consumer обязан дедуплицировать EventId. Exactly-once не обещается.

## ADR-014 — No-op Outbox publisher запрещён как success path

**Статус:** принято.

**Решение:** отсутствие configured transport означает отсутствие OutboxProcessor, а не `Publish => success`.

**Обоснование:** no-op success помечал бы событие delivered, хотя оно потеряно.

## ADR-015 — Отложенная фоновая обработка таймаутов

**Статус:** принято.

**Решение:** дедлайн обеспечивается:

- синхронно на answer/submit;
- background scan с default 30 sec cadence.

**Последствия:** Attempt может оставаться `InProgress` небольшой интервал после deadline, но писать/submit после deadline нельзя; worker eventual фиксирует terminal state.

## ADR-016 — OpenTelemetry через стандартный OTLP

**Статус:** принято.

**Решение:** instrumentation остаётся vendor-neutral; exporter включается только при OTLP endpoint.

**Последствия:** backend можно менять без Domain/Application changes.

## ADR-017 — Production migrations отдельным job

**Статус:** принято.

**Решение:** production API replicas не должны автоматически конкурировать за schema migrations. Используется тот же image с `--migrate`.

**Последствия:** deployment pipeline содержит explicit migration gate.

Migrate-only composition загружает только database configuration; Keycloak/RabbitMQ/CORS/rate-limit/proxy options не загружаются, так что job остаётся database-only.

## ADR-018 — Канонический путь API `/api/v1`

**Статус:** принято.

**Решение:** новые clients должны использовать `/api/v1`.

Legacy `/api/*` rewrite сохранён временно.

## ADR-019 — Legacy `/api/*` rewrite

**Статус:** переходное.

**Решение:** старые routes переписываются в canonical v1 до routing.

**Current lifecycle:** enable/disable configuration, `Deprecation`, optional `Sunset`, configured retirement -> `410 api.version.retired` реализованы.

**Exit criteria:** telemetry/client migration и объявленная дата удаления rewrite.

## ADR-020 — Ключ идемпотентности в теле запроса

**Статус:** заменено стандартным контрактом заголовка `Idempotency-Key`.

**Historical decision:** commands первоначально принимали `idempotencyKey` в JSON request body.

**Current decision:** primary contract — `Idempotency-Key` header; legacy body field временно поддерживается, header/body mismatch валидируется, common operations используют request fingerprint. Publish/start/submit принимают настоящий zero-length body, когда key передан только в header.

## ADR-021 — Role authorization не заменяет resource ownership

**Статус:** принято; текущая реализация `OwnerId` полна для области одной организации.

**Решение:** Keycloak roles — coarse permission. Resource ownership/eligibility должен проверяться отдельно.

**Текущая реализация:** `Test.OwnerId = текущий Keycloak sub`, чтение/запись автора и рецензента ограничены владельцем, глобальный `test-admin`, безопасное заполнение легаси-записей.

**Remaining decision:** Workspace/Tenant/ACL и `(issuer, subject)` вводятся только при multi-organization/multi-realm requirement.

## ADR-022 — Не добавлять Event Sourcing без отдельной причины

**Статус:** принято.

**Решение:** current state persistence + domain events + Outbox достаточны.

Event Sourcing не нужен для:

- revision history (она уже immutable snapshot);
- аудит HTTP-операций;
- доставка интеграционных событий.

## ADR-023 — Не добавлять Redis/cache заранее

**Статус:** текущая политика принята.

**Решение:** кеш/Redis вводится после profiling конкретного read/coordination bottleneck.

PostgreSQL уже обеспечивает consistency, idempotency locks и indexed read queries.

## ADR-024 — Не вводить отдельный broker кроме RabbitMQ без consumer requirement

**Статус:** принято.

Kafka/другой transport не добавляется «для универсальности». Event contracts должны зависеть от business boundary, а adapter к другому broker можно добавить позже.

## ADR-025 — Документация входит в Definition of Done

**Статус:** принято с созданием `docs/`.

## ADR-026 — Инварианты бизнес-правил возвращаются как `Result<T, DomainError>`, а не как сырые исключения (STAB-008)

**Статус:** принято.

**Решение:** если invariant реально достижим через application-level use case (создание/изменение aggregate по command, вызванному из HTTP API), нарушение должно возвращаться как `Result<T, DomainError>` — как и остальные business rules того же метода — а не как брошенное `ArgumentException`/`ArgumentOutOfRangeException`.

**Почему это стало отдельным решением:** до STAB-008 `Test.Normalize`/`NormalizeAnswerOptionText` и приватный конструктор `TestAssignment` кидали исключения для длины текста и порядка availability window/attempt limit — при этом те же самые правила уже были правильно реализованы как `Result`-возвращающие проверки в соседних методах того же агрегата (`TestAssignment.ChangeAvailability`/`ChangeAttemptLimit`). Это привело к трём наблюдаемым проблемам:

1. `ex.Message` встроенного `ArgumentException` содержит CLR-сгенерированный суффикс `" (Parameter 'x')"`, который утекал напрямую в `ProblemDetails.detail` через `Error.Validation(code, ex.Message)` в шести Application handler'ах (`CreateTest`, `RenameTest`, `AddQuestion`, `UpdateQuestion`, `AddAnswerOption`, `UpdateAnswerOption`) — leaky abstraction в публичном HTTP contract.
2. `AssignTestCommandHandler`/`BulkAssignTestsCommandHandler` дублировали bespoke availability-window/attempt-limit проверки только для того, чтобы никогда не дойти до кидающего конструктора `TestAssignment.Create` — реальный business rule существовал в двух местах одновременно.
3. Один и тот же метод (`Test.AddQuestion` и др.) возвращал `Result` для одних invariants и кидал исключение для других — несогласованный contract внутри одной сигнатуры.

**Что осталось exceptions (сознательно, не рефакторится):** guard-инварианты, недостижимые при корректно сформированном caller — `ArgumentNullException` для `null` Id (`Entity.cs`), unreachable switch-default для exhaustive enum (`TestAssignment.cs`), length-проверки `ExternalUserId`/`ExternalGroupId`/`ValidateTargetId`, уже полностью покрытые `[Required]`/`[StringLength]` на HTTP DTO и не имеющие workaround-кода (try/catch или дублированной проверки) в вызывающем Application-коде. Критерий — не «достижимо ли сегодня через конкретный DTO», а «существует ли уже bridging-код (try/catch/дублированная проверка) вокруг этого throw», что и является надёжным сигналом накопленного technical debt.

**Источник:** источник этого пункта — `docs/ROADMAP.md` Phase E, пункт 15 («domain/application error и value-object invariants имеют единый ожидаемый failure contract»), заведён как `STAB-008` в `docs/FEATURE_PLAN.md`.

## ADR-027 — Prometheus + Grafana как local/CI backend метрик вместо Jaeger (`OBS-012`/`OBS-013`)

**Статус:** принято.

**Решение:** в `compose.yaml` добавляются сервисы `prometheus` и `grafana` позади существующего OTel Collector. В pipeline `metrics` коллектора добавляется экспортер `prometheus` (`0.0.0.0:8889`) в дополнение к `debug`; `traces` остаётся только с `debug`. Prometheus собирает метрики с коллектора и вычисляет правила алертов из `deploy/prometheus/alerts.yml`; Grafana разворачивается с источником данных Prometheus и дашбордом `TestApp Overview` (`deploy/grafana/dashboards/testapp-overview.json`).

**Почему Prometheus/Grafana, а не Jaeger:** `docs/SLO_ALERTS.md` уже определял конкретные пороги (§4) и "dashboard minimum" (§6) до этого решения — то, что было не готово, это не список сигналов, а *место, куда их положить и с чем сравнивать*. Jaeger — backend для distributed tracing одного вида телеметрии (spans); он не даёт ни time-series хранилище для метрик, ни alerting engine, ни dashboarding. TestApp — modular monolith в одном процессе, поэтому distributed tracing сейчас имеет низкую практическую ценность (нет сложного multi-service call graph, который trace waterfall обычно объясняет), а alert/dashboard контракт из `SLO_ALERTS.md` уже написан в терминах метрик и порогов, не spans. Prometheus/Grafana закрывает именно этот, реально задокументированный пробел.

**Trade-off, принятый осознанно:** Prometheus добавляет ещё один stateful-компонент (TSDB, `testapp-prometheus` volume) со своей retention-политикой, которой раньше не было в стеке. Взамен `deploy/prometheus/alerts.yml` реализует все алерты `SLO_ALERTS.md` §4, которые технически выразимы через уже существующие метрики (шесть page-правил, шесть warning-правил), кроме одного: readiness-проба `/health/ready` не реализована как отдельное правило, потому что для активного HTTP-пробинга нужен `blackbox_exporter` — отдельный компонент, не входящий в объём "добавить Prometheus и Grafana"; текущее `TestAppMetricsPipelineDown` правило проверяет только доступность OTel Collector metrics endpoint, а не реальную readiness API, и это явно задокументировано как известное ограничение, а не выдаётся за готовое покрытие.

**Что не решено этим ADR:** маршрутизация алертов в pager/chat (нужен Alertmanager + получатель — deployment-owned, `docs/ROADMAP.md` Phase E пункт 12), staging alert drill, и backend для distributed tracing (если/когда появится измеренная потребность — тот же OTel Collector может получить второй metrics-совместимый exporter для traces, например Tempo, без изменения Domain/Application, см. `docs/ARCHITECTURE.md` §11).

**Верификация:** метрика-по-метрике naming (dots→underscores, unit suffixes, `_total` для counters) проверена эмпирически — реальный `otel/opentelemetry-collector-contrib:0.157.0` прогнан с synthetic OTLP payloads, зеркалирующими точные instrument definitions из `TestApp.Infrastructure.Observability.OperationalMetrics`, а .NET runtime/ASP.NET Core metric names — реальным `net10.0` пробным приложением через актуальные `OpenTelemetry.Instrumentation.Runtime`/`AspNetCore` 1.17.0 пакеты, а не по памяти. `scripts/validate-observability-stack.sh` (CI workflow `observability`) держит это в проверенном состоянии на каждый релевантный push/PR: `promtool check config/rules`, Prometheus target health, наличие ожидаемых metric families, здоровье Grafana datasource и присутствие provisioned dashboard.

## ADR-028 — Безопасное для студента представление — граница проекции, а не хранилища (ATT-010/011, UX-001)

**Статус:** принято.

**Решение:** студенческий presentation contract (`GET /api/v1/attempts/{id}/presentation`, `GET /api/v1/assignments/{id}/attempts/active`) строится отдельным DTO-семейством (`AttemptPresentationView`/`AttemptQuestionView`/`AttemptAnswerOptionView`), у которого **нет** члена корректности. Reviewer-контракт (`ReviewerAnswerOptionView` с `IsCorrect`) остаётся отдельным типом; эти два семейства нельзя переиспользовать одно вместо другого.

**Почему это ADR, а не деталь реализации:** `PublishedTestRevision.Questions` хранится как один `jsonb`-столбец `questions_json`, включающий `IsCorrect` каждого варианта. Значит EF материализует **весь answer key** в память при любом чтении revision, и никакая SQL-проекция не может его отфильтровать. Отсутствие корректности в ответе студенту — свойство ровно одного метода (`ReadModelQueries.ProjectAsync`), а не схемы БД и не типа запроса. Это принципиально слабее, чем защита на уровне хранилища: одна невнимательная правка DTO возвращает answer key в публичный ответ, и ничто в инфраструктуре этому не помешает.

**Что из этого следует для тестов:** граница закреплена тестом на **сериализованных байтах HTTP-ответа** (`AttemptPresentationTests.The_serialized_student_payload_contains_no_correctness_information`), а не только assert'ами по полям DTO. Тест проверяет и текстово (нет `isCorrect`/`correctOption`/`answerKey`), и структурно (ни у одного option нет boolean-свойства), при этом требует наличие текста правильного варианта — студент обязан видеть его как вариант выбора. Тест верифицирован красным: временное добавление `IsCorrect` в `AttemptAnswerOptionView` роняет его.

**Сознательные решения контракта:**

- **Источник — immutable revision, не живой `Test`.** Редактирование теста после публикации не меняет попытку в полёте; закреплено отдельным тестом.
- **Новый ресурс, а не расширение `GET /attempts/{id}`.** Форма существующего v1-ответа не менялась — contract freeze (`docs/ROADMAP.md` Phase E пункт 1) остаётся достижимым без breaking change.
- **`ServerTime` в ответе.** Отсчёт до дедлайна считается от серверного времени, а не от часов клиента, которые могут быть смещены.
- **Read side не мутирует.** Между истечением дедлайна и проходом expiration worker студент увидит `Status=InProgress` при `DeadlineAt < ServerTime`; GET не завершает попытку. Любая запись в этом окне отвечает `409 attempt.expired` — это поведение уже существовало.
- **Resume отдаёт только `InProgress`.** Завершённая попытка — история, она читается через `/result`; предлагать её к возобновлению нельзя.
- **Ownership строго по студенту.** Admin/author смотрят ту же попытку через reviewer API, который намеренно содержит корректность.

**Что не вошло:** перемешивание вопросов/вариантов, пагинация вопросов внутри попытки, autosave batching (`ATT-014`) и pause/resume clock (`ATT-013`) — отдельные пункты плана со своими решениями.

## ADR-029 — Value objects валидируют себя в конструкторе, а не в статической фабрике (`STAB-009`)

**Статус:** принято.

**Решение:** strong identifiers (`TestId`, `QuestionId`, `AnswerOptionId`, `TestAssignmentId`, `TestAttemptId`, `PublishedTestRevisionId`) отвергают `Guid.Empty`, `ExternalUserId`/`ExternalGroupId` отвергают пустую строку и превышение `MaxIdentifierLength`, `AttemptScore` требует `0 <= Earned <= Maximum`. Проверка живёт **в конструкторе**. Именованные фабрики (`FromSubject`, `FromExternalId`) сохранены как intent-revealing точки входа и делегируют конструктору.

**Почему не «приватный конструктор + статическая фабрика»,** как обычно рекомендуют для self-validating value object: `PublishedTestRevision.Questions` хранится одним `jsonb`-столбцом, поэтому `QuestionId`/`AnswerOptionId` внутри `PublishedQuestion` материализуются `System.Text.Json` при каждом чтении ревизии, а все identifiers дополнительно материализуются EF-конвертерами значений. Обоим путям нужен доступный параметризованный конструктор. Приватный конструктор потребовал бы отдельного `JsonConverter` на каждый тип и отдельного «доверенного» пути для EF — то есть ровно того второго, невалидирующего входа, который эта работа и закрывает. Конструктор, который валидирует, даёт **один** контракт для всех путей построения, включая материализацию.

**Практическое подтверждение того, что это не теоретический риск:** первая версия правки объявила `Value` как get-only свойство. Сборка прошла, все 177 unit-тестов прошли — и при этом `System.Text.Json` молча отдавал `Guid.Empty` для каждого идентификатора внутри `jsonb`-payload, потому что не мог ни вызвать конструктор, ни записать свойство. Дефект поймал round-trip-тест (`ValueObjectInvariantTests.The_published_question_payload_round_trips_through_system_text_json`), написанный именно под этот путь; исправлено атрибутом `[JsonConstructor]` на конструкторах identifiers. Это единственная причина, по которой `System.Text.Json.Serialization` появился в `TestApp.Domain` — BCL-атрибут, а не инфраструктурная зависимость; ограничения `ARCHITECTURE.md` §12 (EF/HTTP/broker) не затронуты.

**Граница транспорта — обязательная часть решения, а не отдельная задача.** Как только identifiers отказываются строиться из нулевого GUID, любой клиент, приславший `00000000-0000-0000-0000-000000000000`, превращал бы domain guard в `500`. Поэтому `RequestValidationFilter` отвечает раньше конструктора: нулевой GUID в **route-сегменте** — это `404` (сегмент именует ресурс, которого не может существовать; ровно то, что endpoint возвращал и до валидации), нулевой GUID в **query-фильтре** — `400` (это входное значение), а `NotEmptyGuidAttribute` закрывает GUID-поля **тела** запроса. Критерий ADR-026 при этом соблюдён: guard остаётся исключением именно потому, что корректный caller до него не доходит, и вокруг него нет bridging-кода.

**Известное ограничение, которое эта работа не закрывает:** `default(TestId)` и `new TestId()` остаются доступными — C# всегда оставляет неявный parameterless конструктор структуры достижимым, и запретить его нельзя. Инвариант закрывает *явные* пути построения; путь через `default` закрывается тем, что ни одно место в коде не создаёт identifier таким образом, и тем, что граница транспорта отвергает нулевой GUID до попадания в модель. Аналогично приватный parameterless конструктор `AttemptScore` существует только для EF-материализации: строка, записанная до появления инварианта, должна читаться, а не бросать исключение при загрузке.

## ADR-030 — Deadline — authority для `Timeout`, оператор — для `ForceTimeout` (`STAB-009`)

**Статус:** принято.

**Решение:** `TestAttempt.Timeout(...)` требует, чтобы дедлайн уже наступил, и возвращает `attempt.not_expired` (409) в противном случае — включая попытку без дедлайна вовсе. Ручное административное завершение получило отдельный метод `TestAttempt.ForceTimeout(...)`, который дедлайна не требует. `TimeoutAttemptCommandHandler` (эндпоинт `POST /api/v1/attempts/{id}/timeout`, `tests:assign`) — единственный вызывающий `ForceTimeout`; автоматические пути (`ExpireAttemptCommandHandler` и timeout при answer/clear/submit по истёкшему дедлайну) вызывают `Timeout`.

**Почему это потребовало продуктового решения, а не только кода.** `DOMAIN_MODEL.md` §11 фиксировал пробел так: «admin `Timeout` принимает вычисленный caller score/outcome и не требует, чтобы deadline уже наступил». Прочитать это можно двумя способами. Если считать поведение багом — надо просто добавить проверку, но тогда ломается задокументированная возможность (`API.md` §14 «Ручной таймаут администратором») и исчезает единственный способ закрыть попытку по тесту без ограничения времени: у такой попытки дедлайна нет, и наступить он не может никогда. Если считать поведение осознанным — проверку добавлять нельзя, и тогда правило «попытку завершает дедлайн» перестаёт быть инвариантом агрегата и живёт в вызывающих хендлерах. Оба прочтения защитимы, и выбор между ними — продуктовый: существует ли у администратора право закрыть живую попытку.

**Выбранный ответ: да, существует, но оно должно быть названо.** Отсюда два метода вместо одного флага или одного разрешающего метода: call site обязан написать, чьей властью он завершает попытку — часов или оператора. Разрешающий `Timeout` этого различия не выражал, поэтому четыре автоматических вызова и один административный выглядели одинаково, а фактическую защиту студента давал только `if (attempt.IsExpiredAt(now))` в хендлере.

**Последствия, принятые осознанно:**

- **Оба пути дают один и тот же результат для читателя:** статус `TimedOut` и событие `AttemptTimedOut`. Событие намеренно **не** получило дискриминатор «forced/expired»: `AttemptTimedOut` уходит в Outbox, а каталог интеграционных событий ещё не заморожен (`ROADMAP.md` Phase E пункт 9) — менять форму опубликованного события ради внутреннего различия рано. Кто именно завершил попытку, видно в audit trail.
- **Проверка в `ExpireAttemptCommandHandler` сохранена** как early-out: она отвечает `attempt.not_expired` до загрузки ревизии. Это не дублирование правила в смысле ADR-026 — правило принадлежит агрегату и возвращает тот же код; хендлер лишь спрашивает агрегат `IsExpiredAt`, чтобы не делать лишний запрос.
- **`ForceTimeout` сохраняет остальные инварианты:** попытка должна быть `InProgress`, а время завершения не может быть раньше старта. Снята ровно одна проверка — про дедлайн.

Изменение public API/domain/schema/security/runtime semantics обязано обновлять соответствующий документ в том же change set.
