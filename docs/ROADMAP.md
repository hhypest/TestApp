# Дорожная карта развития TestApp

> Baseline: `beta-ddd`, 2026-08-16. Roadmap задаёт последовательность и acceptance gates, а exact-head evidence определяется GitHub Actions ветки.

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

# 1. `0.8.x` — Базовая архитектура ядра

**Статус: ГОТОВО**

Реализовано:

- модульный монолит / Clean Architecture / DDD / CQRS;
- Test aggregate и controlled authoring;
- неизменяемая `PublishedTestRevision`;
- валидация публикации для `SingleChoice` / `MultipleChoice`;
- оценивание по точному совпадению набора;
- назначения на пользователя/группу, окна доступности, лимиты попыток, массовое назначение;
- старт/ответ/очистка/отправка/таймаут попытки;
- модели чтения для рецензента/администратора/студента;
- PostgreSQL 18 как baseline для runtime и CI;
- аутентификация, роли и группы Keycloak;
- транзакционный Outbox + транспорт RabbitMQ;
- Docker/Compose + запуск в режиме только миграций;
- интеграционные тесты против реальных PostgreSQL/RabbitMQ.

---

# 2. `0.9.0` — Фаза A: production-конфигурация и защита периметра

**Приоритет: P0**
**Статус: ГОТОВО**

Реализовано:

- production-конфигурация с ранним отказом;
- отсутствие production fallback DB credentials;
- `--migrate` вместо startup migrations outside Development;
- конфигурация доверенного обратного прокси;
- явный список разрешённых источников CORS;
- настраиваемая политика HTTPS/HSTS;
- заголовки безопасности + отключённый баннер сервера Kestrel;
- классовое ограничение частоты запросов;
- OpenAPI в production выключен по умолчанию / опционально защищён ролью администратора;
- гейт аудита NuGet по уязвимостям high/critical.

**Выходной гейт: ПРОЙДЕН.**

---

# 3. `0.9.1` — Фаза B: владение ресурсами

**Приоритет: P0**
**Статус: ГОТОВО**

Текущая single-organization модель:

```text
Test.OwnerId = Keycloak sub создавшего автора
```

Реализовано:

- неизменяемый обязательный `OwnerId`;
- owner checks в Application write boundary;
- owner SQL filtering для catalog/editor/revisions/reviewer list/detail;
- глобальная область видимости для `test-admin`;
- безопасное заполнение легаси-записей значением `__legacy_admin_only__`;
- cross-author E2E, включая reviewer correctness isolation.

**Выходной гейт: ПРОЙДЕН.**

---

# 4. `0.9.2` — Фаза C: зрелость контракта API

**Приоритет: P0/P1**  
**Статус: ГОТОВО**

## C1. Стандартный Idempotency-Key — ГОТОВО

- основной контракт повтора — заголовок `Idempotency-Key`;
- переходная совместимость с ключом в теле запроса;
- расхождение заголовка и тела -> `400 idempotency.key_mismatch`;
- канонический SHA-256 отпечаток запроса;
- тот же ключ + другой логический запрос -> `409 idempotency.key_reused`;
- распределённая advisory-блокировка PostgreSQL;
- покрыты публикация, одиночное и массовое назначение, отправка попытки.

Phase C завершила header resolution/fingerprint contract. Настоящий zero-length body для no-payload publish/start/submit закрыт в `0.9.4` (API-009): request DTO параметр nullable, key только в header достаточен.

## C2. HTTP optimistic concurrency — ГОТОВО

- редактор возвращает `ConcurrencyVersion` + строгий `ETag`;
- изменения `Test` требуют `If-Match`;
- missing -> `428`;
- malformed/weak/wildcard -> `400`;
- stale -> `412`;
- гонка на уровне БД остаётся защищена токеном параллельного доступа EF -> `409`.

## C3. Нормализация валидации — ГОТОВО

- некорректные путь/строка запроса/тело -> стабильный `400 request.invalid`;
- валидация границы DTO на основе DataAnnotations;
- защита от неопределённых значений перечислений;
- стабильный `400 request.validation` + структурированные ошибки;
- доменные инварианты длины и типов согласованы с хранилищем.

## C4. Качество OpenAPI — ГОТОВО

- схема безопасности Bearer;
- стабильные идентификаторы операций, краткие и подробные описания;
- требования авторизации;
- задокументированные `Idempotency-Key` / `If-Match` / `ETag`;
- ответы в формате ProblemDetails;
- метаданные перечислений и примеров;
- контрактный тест сериализованного документа OpenAPI.

## C5. Жизненный цикл версий API — ГОТОВО

Канонический путь:

```text
/api/v1/*
```

Legacy `/api/*` compatibility является управляемым lifecycle layer:

- настраиваемое включение/отключение;
- RFC-style `Deprecation` header;
- optional `Sunset`;
- retirement -> `410 api.version.retired`;
- сквозные тесты границы жизненного цикла.

**Выходной гейт фазы C: ПРОЙДЕН.**

---

# 5. `0.9.3` — Фаза D: эксплуатационная надёжность

**Приоритет: P0/P1**  
**Статус: ГОТОВО — D1..D6 ПРОЙДЕНЫ**

## D1. Резервное копирование и восстановление — ГОТОВО

Реализовано:

- скрипт логического резервного копирования PostgreSQL;
- архив PostgreSQL в custom-формате + SHA-256;
- restore verification в отдельной database;
- проверка схемы, количества таблиц и истории миграций EF;
- проверка маркера прикладных данных;
- учебное восстановление в CI;
- fail-closed application recovery harness для staging: clean volumes, тот же immutable
  image digest, readiness/business/idempotency/Outbox/audit/advisory-lock probes;
- задокументированные базовые ожидания RPO/RTO.

Logical backup является portability/recovery baseline; более жёсткий production RPO требует
provider-native snapshot/WAL/PITR. Repository-контракт D1 готов, но release gate `#18`
закрывается только фактическим запуском harness на засеянном staging и приложенным evidence.

## D2. Сроки хранения и очистка — ГОТОВО

Реализовано:

- типизированная конфигурация сроков хранения;
- удаление ограниченными пакетами;
- advisory-блокировка в области базы данных;
- срок хранения аудита;
- срок хранения записей идемпотентности;
- срок хранения обработанных записей Outbox;
- необходимые индексы для очистки;
- регрессионные тесты безопасности на PostgreSQL.

**Не удаляются автоматически:** pending, retrying и active dead-letter Outbox rows.

## D3. Управление dead-letter — ГОТОВО

Реализовано:

- admin-only safe detail без payload;
- явная повторная постановка в очередь;
- explicit discard как terminal state, не physical delete;
- обязательная причина;
- атомарный аудит менеджера: актор/действие/причина/корреляция;
- shared lock boundary с publisher;
- API/PostgreSQL/OpenAPI E2E.

## D4. SLO и эксплуатационные метрики — ГОТОВО

Реализовано:

- `TestApp.Operations` Meter;
- результаты публикации Outbox и задержка доставки;
- действия над активными dead-letter;
- gauge-метрики Outbox: необработанные, dead-letter, возраст самого старого;
- gauge-метрики просроченных попыток и отставания;
- результаты истечения попыток;
- счётчик записей, удалённых по сроку хранения;
- ограниченный набор категорий API-исключений;
- регистрация в OpenTelemetry;
- `docs/SLO_ALERTS.md` с начальным alerting contract.

## D5. CI безопасности и цепочки поставок — ГОТОВО

Отдельный `security` workflow:

- сканирование репозитория на секреты;
- гейт уязвимостей HIGH/CRITICAL в production-образе;
- SBOM образа в формате CycloneDX;
- хранение артефакта SBOM;
- существующий NuGet high/critical gate остаётся в основном pipeline.

## D6. Регрессия нагрузки и ёмкости — ГОТОВО

Добавлено:

- `.github/workflows/performance.yml`;
- authenticated k6 через реальный local Keycloak;
- стек, приближенный к production: API + PostgreSQL 18 + RabbitMQ + Keycloak;
- повышенные потолки rate limit только для нагрузочных прогонов;
- scenario-specific p95 gates;
- artifact `testapp-capacity-results`.

Критичные HTTP-сценарии:

1. одновременные старты попыток;
2. всплеск записи ответов;
3. массовое создание назначений;
4. постраничный обход результатов рецензентом.

Критичные сценарии воркеров:

5. expiration storm — overdue backlog должен drain до zero <= 30 s;
6. Outbox backlog recovery — 100 synthetic messages должны пройти real RabbitMQ transport и стать processed <= 30 s.

Подробности и thresholds: `docs/PERFORMANCE.md`.

**D6 exit gate:** первый полный `performance` workflow green на exact head + сохранённые artifacts.

**Phase D exit gate:** D6 green + основной `dotnet` и `security` workflows green на совместимом head.

### Свидетельства проверки

PostgreSQL implementation commit `9916b98` прошёл полный `performance` run #9: 8771/8771 checks, HTTP failure rate 0, все scenario p95 ниже thresholds, expiration 1247 -> 0 за 14 s и Outbox 100 -> 0 за 1 s. `dotnet` и `security` на том же commit также green; D6 и Phase D exit gates выполнены.

---

# 6. `0.9.4` — Фаза D7: исправления корректности и стабилизация

**Приоритет: P0**
**Статус: ГОТОВО**

Перед 1.0 RC необходимо было закрыть findings текущего exact-head review:

1. **DONE:** actor-scoped `StartAttempt` replay до повторной проверки mutable assignment availability/group membership, с regression tests для cancellation/expiry/group change и нового key;
2. **DONE:** audit сохраняет итоговый HTTP status после exception mapping, включая handled `400/409`, а не промежуточный `500`;
3. **DONE:** Outbox/expiration hosted workers переживают transient cycle-level DB/query/lock failures;
4. **DONE:** publish/start/submit принимают настоящий zero-length body при валидном `Idempotency-Key` header;
5. **DONE:** `--migrate` загружает только database-required configuration;
6. **DONE:** RabbitMQ 4.3-compatible diagnostic capacity probe и полный green D6 run;
7. documentation source of truth синхронизирована с code/tests/CI и должна оставаться такой (ongoing discipline, не одноразовый gate).

Дополнительно за рамками исходного findings review закрыт STAB-006/007/008 (deterministic pagination, SQL-side reviewer/admin queries, unified domain/application failure contract — см. `docs/DECISIONS.md` ADR-026).

**Выходной гейт: ПРОЙДЕН.** Regression tests для пунктов 1–6 существуют, `dotnet`/`security`/`performance` green на HEAD `aa0752f`, D6 artifact пересобран на этом же head, открытых P0 correctness findings нет.

---

# 7. `1.0.0` — Фаза E: стабилизация / кандидат в production-релиз

**Приоритет: P0**  
**Статус: В РАБОТЕ — фаза D7 закрыта, пункты 13–16 готовы, из пункта 10 готова часть про release notes (DEV-005), из пункта 12 готова local/CI-часть (OBS-012/013, ADR-027), остальное из 1–12 открыто**

> Пункт 15 до `2026-08-16` формулировался шире, чем закрывал: он объединял application-level failure contract (действительно закрытый STAB-008) и self-validating value objects, которые ADR-026 сознательно оставлял открытыми, — и при этом был помечен `DONE`, тогда как `DOMAIN_MODEL.md` §11 продолжал перечислять те же пробелы как открытые. Расхождение нашёл независимый аудит ветки на `0b94db3`. Пункт разделён на 15 и 16; оба закрыты, 16 — через STAB-009.

До freeze 1.0 требуется:

1. заморозка публичного контракта API v1 (**ГОТОВО:** заморозка объявлена окончательной — ADR-035. Снимок `docs/openapi/v1.json` делает видимым любое изменение, `docs/openapi/v1-frozen-surface.json` и `OpenApiFrozenSurfaceTests` запрещают исчезновение эндпоинта, кода ответа, поля тела и свойства DTO. Политика совместимости и критерии удаления legacy-возможностей — `docs/API.md` §1. Принятый риск: фронтенд роли `test-admin` happy path не проходил, поэтому его правки контракта возможны только дополнением, v2 или задокументированным ломающим изменением);
2. ноль открытых дефектов P0 по безопасности и изоляции данных (**ЧАСТИЧНО:** чеклист с указанием проверки для каждого утверждения — `docs/SECURITY.md` §18.1; открытой остаётся одна строка, требующая контура: отсутствие отладочных креденшелов на самой машине);
3. повторная проверка изоляции по владельцу (**ГОТОВО:** `CrossAuthorIsolationTests` — перебор всех мутирующих эндпоинтов чужим автором со сверкой состояния после отказа, изоляция поиска и `TotalCount`, разделение представлений попытки и рецензента; легаси-владелец `__legacy_admin_only__` в репозитории отсутствует вместе с возможностью его появления, см. `docs/SECURITY.md` §18.1);
4. проверка задания миграций (**ГОТОВО В CI:** применение baseline на чистой базе production-образом, идемпотентность повторного прогона по отпечатку схемы и отказ автоприменения на старте вне Development — `scripts/verify-migration-job.sh`, политика изменений схемы с 1.0 в `docs/PERSISTENCE.md`. Пункт переформулирован осознанно: предыдущей поддерживаемой схемы не существует — история схлопнута в единственную baseline-миграцию, и мигрировать не с чего. «Предыдущая схема» становится содержательным понятием с 1.1. Остаётся открытым — целостность данных после применения на засеянной базе контура, issue #25);
5. отсутствие в production отладочных учётных данных и запасных значений (**ЧАСТИЧНО:** sweep выполнен — `docs/SECURITY.md` §18.1; в приложении нет ни одного `appsettings*.json`, отладочные креденшелы существуют только в local-dev артефактах; проверка на самом контуре — issue #16);
6. продемонстрированное резервное копирование и восстановление;
7. повторно зелёные пайплайны `dotnet`, `security`, `performance`;
8. определённые и достигнутые целевые показатели ёмкости на staging;
9. **DONE:** явно задокументированный каталог интеграционных событий — на 1.0 он пуст намеренно, состояние закреплено `IntegrationEventCatalogTests` (ADR-033, issue #21);
10. release notes (**ГОТОВО:** `CHANGELOG.md`, DEV-005) + runbook отката/исправления вперёд (открыто — требует репетиции релиза на staging);
11. **DONE:** свидетельства по зависимостям, контейнеру и SBOM приложены к процессу релиза — workflow `release` публикует образ в GHCR по immutable digest и прикладывает к GitHub Release SBOM, замороженный контракт v1 и список доказательств (issue #17, `docs/OPERATIONS.md` §16.2);
12. дашборды и алерты SLO отработаны против staging (**ГОТОВО локально/в CI:** Prometheus + Grafana в `compose.yaml`, дашборд и выражения всех правил §4 проверяются в CI — OBS-012/OBS-013, ADR-027; активный пробер готовности `blackbox_exporter` закрывает page-правило 1 — ADR-034; маршрутизация в Alertmanager настроена на контуре. Остаётся открытым — сам drill на staging с измеренным time-to-alert: настроенный приёмник и работающий приёмник не одно и то же, см. `docs/SLO_ALERTS.md` §7);
13. **DONE:** deterministic pagination order (`timestamp + ID`) на всех paged read models;
14. **DONE:** reviewer/admin hot queries выполняют joins/aggregates в SQL без high-cardinality materialization;
15. **DONE:** domain/application error contract единый — business rules, достижимые через application use case, возвращают `Result<T, DomainError>`, а не сырые исключения (STAB-008, ADR-026);
16. **DONE:** value objects валидируют себя сами — strong identifiers отвергают `Guid.Empty`, `AttemptScore` требует `0 <= Earned <= Maximum`, `TestAttempt.Timeout` требует наступившего deadline (STAB-009, ADR-029, ADR-030).

**1.0 definition:** production-safe core assessment workflow, а не максимальное число типов вопросов.

---

# 8. `1.1.x` — Фаза F: путь студента и продуктивность авторинга

**Приоритет: P1**

## F0. Безопасное для студента представление и возобновление попытки — **ГОТОВО**

Первая product vertical после 1.0, реализована досрочно (ATT-010/ATT-011/UX-001, ADR-028):

- **DONE:** question/option presentation из immutable attempt revision (`GET /api/v1/attempts/{id}/presentation`);
- **DONE:** saved responses, status, deadline и `serverTime` для клиентского обратного отсчёта;
- **DONE:** запрет `IsCorrect`/answer-key leakage — закреплён тестом на сериализованном HTTP-ответе, верифицирован красным;
- **DONE:** resume активной попытки (`GET /api/v1/assignments/{id}/attempts/active`);
- **DONE:** ownership/contract tests. OpenAPI документ генерируется автоматически и покрыт существующим `OpenApiContractTests`.

## F1. Клонирование теста / черновик из revision

Создание нового working test из immutable revision без ручного копирования.

## F2. Теги / категории / расширенный поиск

- tags;
- category/subject;
- индексированные фильтры каталога.

## F3. Банк вопросов — требуется продуктовое решение

Если подтверждён reuse use case:

- отдельный QuestionBankItem aggregate/read model;
- явная семантика копирования/ссылки;
- PublishedTestRevision всё равно snapshot-ит content.

## F4. Import / export

Начать с versioned JSON contract; CSV только для ограниченных choice scenarios.

## F5. Эндпоинт валидации черновика

Получение publication errors без state mutation.

---

# 9. `1.2.x` — Фаза G: расширенное поведение оценивания

**Priority: P1/P2**

## G1. Randomization

- порядок вопросов;
- порядок вариантов;
- детерминированное зерно генерации для попытки;
- воспроизводимость исторического порядка предъявления.

## G2. Пулы вопросов

Attempt snapshot выбранных question IDs + stable scoring maximum.

## G3. Стратегии оценивания

Explicit versioned strategy, например:

- ExactSet;
- PartialPositive;
- будущие custom strategies.

## G4. Новые типы вопросов

Рекомендуемая последовательность:

1. числовой / короткий детерминированный ответ;
2. свободный текст + ручная проверка;
3. Ordering;
4. Matching;
5. форматированный контент / вложения.

Каждый тип получает собственную model/validation/scoring semantics; универсальный opaque JSON answer blob не вводится.

## G5. Ручная проверка

Потребуется lifecycle:

```text
Submitted -> AwaitingReview -> Graded
```

и разделение auto score / final score.

---

# 10. `1.3.x` — Фаза H: оркестрация назначений и уведомления

**Priority: P1/P2**

- переиспользуемые кампании и шаблоны назначений;
- планирование и активация со стороны интерфейса;
- бизнес-события интеграции: назначение создано, дедлайн приближается, результат получен;
- необязательный потребитель уведомлений вне основной транзакции;
- явное решение: динамическое членство в группе или снимок членства на момент назначения.

---

# 11. `1.4.x` — Phase I: Reporting & analytics

**Priority: P1/P2**

- доля завершения и прохождения;
- average/median/time-to-complete;
- сложность вопроса в разрезе `(RevisionId, QuestionId)`;
- авторизованный экспорт CSV/JSON с аудитом;
- проекции и материализованная аналитика только после измерения влияния на OLTP.

---

# 12. `2.x` / по бизнес-триггеру — Фаза J: развитие в сторону workspace / мультиарендности

Не форсируется без требования нескольких организаций/isolated workspaces.

При необходимости:

- агрегат Workspace/Tenant;
- членства + роли внутри workspace;
- тесты, назначения и результаты в области workspace;
- `(Issuer, Subject)` identity;
- идемпотентность, аудит, события и ограничения частоты с учётом арендатора;
- политики экспорта и удаления.

---

# 13. Фаза K: триггеры выделения сервисов при росте

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
