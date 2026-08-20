# Безопасность TestApp

> Статус: authentication/authorization, owner isolation и repository-level production hardening **Implemented**. Ниже отдельно отмечены remaining stabilization и deployment-owned controls.

## 1. Обзор модели безопасности

TestApp использует два уровня контроля:

1. **Transport/system authorization** — JWT + ASP.NET Core policies по Keycloak roles.
2. **Business authorization** — проверки текущего actor внутри Application use cases (например assignment target, attempt ownership).

Эти уровни нельзя смешивать: наличие роли не заменяет domain/business eligibility.

## 2. Провайдер идентификации

Keycloak является внешним source of truth для:

- users;
- groups;
- крупнозернистые системные роли.

TestApp не хранит локальный password hash, login/email credential state и не выдаёт access tokens.

## 3. Конфигурация JWT

Текущий JWT Bearer setup:

- `Authority = Keycloak:Authority`;
- `Audience = Keycloak:Audience`;
- HTTPS metadata required вне Development;
- `MapInboundClaims = false`;
- `NameClaimType = sub`;
- `RoleClaimType = roles`.

### Ожидаемые claims

```text
sub     stable external subject
roles   application roles
groups  external group identifiers
```

`KeycloakClaimsMapper` дополнительно понимает:

- scalar `groups` claim;
- JSON-array string в `groups` claim;
- `ClaimTypes.Role`;
- scalar/JSON-array `roles`.

Subject fallback на `ClaimTypes.NameIdentifier` существует для совместимости, но canonical claim — `sub`.

## 4. Типы внешней идентичности

Application/Domain используют:

- `ExternalUserId`;
- `ExternalGroupId`.

JWT/ClaimsPrincipal преобразуется в эти типы в Infrastructure (`HttpCurrentActor`). Domain от Identity Provider не зависит.

## 5. Roles и policies

| Роль | Предусмотренная область |
|---|---|
| `test-author` | авторинг + публикация + результаты рецензирования |
| `test-admin` | авторинг + публикация + администрирование назначений + рецензирование + эксплуатация |
| аутентифицированный пользователь без роли | собственные назначения и попытки |

Policies:

| Policy | Roles |
|---|---|
| `tests:write` | `test-author`, `test-admin` |
| `tests:publish` | `test-author`, `test-admin` |
| `tests:assign` | `test-admin` |
| `results:review` | `test-author`, `test-admin` |
| `operations:read` | `test-admin` |

## 6. Бизнес-авторизация

### Старт попытки

Роль не требуется, но Application проверяет:

- direct user target либо membership в assignment group;
- доступность назначения;
- cancellation;
- лимит попыток.

### Операции студента над попыткой

Answer/clear/submit/detail/result разрешены только когда:

```text
attempt.UserId == currentActor.UserId
```

### Принудительный таймаут администратором

Manual timeout защищён `tests:assign` и не использует student ownership.

## 7. Изоляция по владельцу ресурса

Текущая single-organization модель:

```text
Test.OwnerId = Keycloak sub создавшего автора
```

Реализованы:

- immutable mandatory owner при создании Test;
- owner filtering catalog/editor/revisions/reviewer list/detail в SQL;
- owner checks для rename/settings/questions/options/publish/archive;
- глобальная область видимости для `test-admin`;
- владелец не может появиться иначе, чем из `sub` вызывающего: `OwnerId` — обязательная колонка без значения по умолчанию, а «легаси-владелец» `__legacy_admin_only__` из ранних миграций не существует ни в коде, ни в схеме (история схлопнута в baseline-миграцию);
- cross-author negative E2E, включая correctness detail.

Workspace/Tenant/Team и ACL не реализованы сознательно. Они становятся P0 только при multi-organization deployment; текущий `OwnerId` не следует ошибочно называть tenant boundary.

## 8. Пробел мультиреалмовости

Текущий `ExternalUserId` основан на `sub`.

При нескольких Keycloak realms/OIDC issuers одинаковый `sub` теоретически может появиться у разных issuers.

Планируемый ключ идентичности:

```text
ExternalIdentity(Issuer, Subject)
```

или стабильный internal principal ID, связанный с `(iss, sub)`.

Изменение затронет:

- assignments;
- attempts;
- idempotency;
- audit;
- владение автора;
- indexes/migrations;
- интеграционные события.

## 9. Граница данных о правильных ответах

Correctness считается sensitive assessment data.

### Allowed

- Domain/PublishedTestRevision;
- editor view для author/admin;
- reviewer detail по `results:review`.

### Forbidden

Student endpoints не должны раскрывать:

- `IsCorrect`;
- correct option IDs как отдельный answer key;
- сырой JSON опубликованной revision;
- DTO рецензента.

Regression tests должны проверять этот boundary при изменении read models.

## 10. Ограничение частоты запросов

**Реализовано:** политики фиксированного окна, задаваемые конфигурацией:

```text
RateLimiting:General:...
RateLimiting:StudentWrite:...
RateLimiting:PrivilegedRead:...
RateLimiting:Operations:...
```

Partition = authenticated `sub`; fallback = effective remote IP после trusted forwarded-header processing. `KnownProxies/KnownNetworks` и `ForwardLimit` конфигурируются явно, untrusted forwarded headers игнорируются. Rejection возвращает `429`, correlation header сохраняется.

## 11. Correlation и audit

Header:

```text
X-Correlation-ID
```

Принимается только непустое значение <= 128 chars, иначе генерируется.

Методы, изменяющие состояние:

```text
POST PUT PATCH DELETE
```

пишутся в `audit_entries`.

Audit содержит metadata, но **не request/response body**.

Это снижает риск сохранения:

- access-токены;
- содержимое правильных ответов;
- PII из будущих форм;
- secrets.

### Гарантии и ограничения аудита

Audit entry создаётся best-effort после request execution. Ошибка audit persistence логируется, но не меняет business response. Это правильная availability trade-off для текущего уровня, но compliance-сценарий может потребовать другую гарантию.

Audit middleware наблюдает финальный status после exception mapping; handled binding/concurrency exceptions и обычные precondition failures сохраняются как фактические `400/409/412`, а unhandled failures — как `500`. Audit по-прежнему не является compliance-grade immutable external sink.

## 12. ProblemDetails и information disclosure

Unhandled exception возвращает generic:

```text
internal.error
An unexpected error occurred.
```

Stack trace не возвращается клиенту.

Concurrency conflict возвращает transport-safe message и trace ID.

Domain/application validation messages являются частью business contract и могут возвращаться как ProblemDetails detail.

## 13. Secrets/configuration

### Текущие значения по умолчанию для разработки

Compose использует простые credentials `testapp/testapp` и dev Keycloak credentials. Они предназначены **только для local development**.

### Поведение в production

- вне Development database connection string обязателен; dev fallback там не применяется;
- Keycloak authority/audience обязательны для HTTP runtime;
- если RabbitMQ enabled — broker connection обязателен;
- HTTPS metadata требуется по умолчанию;
- secrets не логируются и должны поступать через environment/deployment secret store.

`--migrate` изолирован до database-only configuration: composition root загружает только `RuntimeConfiguration.LoadDatabase`, Keycloak/RabbitMQ/CORS/rate-limit/proxy options не загружаются.

## 14. TLS и обратный прокси

### Реализованный контракт репозитория

- JWT metadata HTTPS required вне Development;
- opt-in `UseForwardedHeaders` с explicit `KnownProxies/KnownNetworks` и `ForwardLimit`;
- настраиваемые перенаправление на HTTPS и HSTS;
- explicit CORS allow-list без wildcard origin;
- партиционирование rate limit с учётом прокси;
- отключённый баннер сервера Kestrel;
- `nosniff`, `DENY`, `no-referrer`, Permissions-Policy и restrictive CSP baseline.

Конкретная TLS termination, external base URL, certificate rotation и ingress/network policy остаются deployment-owned.

## 15. Публикация OpenAPI

Политика времени выполнения задаётся конфигурацией:

- Development: enabled/anonymous по умолчанию;
- Production: disabled по умолчанию;
- если включён с `AllowAnonymous=false`, endpoint требует `operations:read`;
- anonymous production exposure возможен только как явная configuration decision.

## 16. Безопасность RabbitMQ

При enabled transport connection string может содержать credentials и должен поступать из secret source.

Рекомендация для production:

- отдельный RabbitMQ user/vhost;
- minimum permissions только на нужный exchange;
- `amqps://` при выходе за trusted private network;
- план ротации;
- не использовать `guest`/default administrator;
- broker management UI отдельно от application traffic.

## 17. Безопасность базы данных

Production:

- API DB role без `SUPERUSER`, database ownership и DDL privileges;
- migration job может иметь отдельный более привилегированный user;
- TLS для remote PostgreSQL;
- шифрование резервных копий;
- ротация учётных данных;
- сетевой allow-list или приватная подсеть.

## 18. Backlog безопасности и стабильности до 1.0

Repository-level edge security, ownership, NuGet/image scan, secret scan и SBOM уже реализованы. Secret scan проверяет весь репозиторий, включая workflow и local Compose: `--skip-files` и allowlist отсутствуют. Все внешние GitHub Actions закреплены по commit SHA, исполняемые container references — по `sha256` digest, runner — по major OS label `ubuntu-24.04`, .NET SDK — точной patch-band версией; `scripts/verify_immutable_references.py` удерживает это как CI-инвариант.

Actor-scoped `StartAttempt` replay также реализован: lookup включает current `UserId`, поэтому изменение assignment/group state не ломает retry владельца и не раскрывает attempt другого пользователя.

P0/P1 до 1.0:

1. выполнить staging alert/restore/rollback security rehearsal.

Business-triggered/после 1.0:

6. `(Issuer, Subject)` identity при multi-realm;
7. sensitive-data classification до первого integration event consumer;
8. Workspace/Tenant/ACL только при multi-organization requirement;
9. compliance-grade immutable external audit sink, если он требуется нормативно.

## 18.1 Чеклист P0 перед 1.0 (гейт §7 пункты 2 и 5)

Гейт требует «ноль открытых дефектов P0 по безопасности и изоляции данных». Отсутствие открытых issues доказательством не является, поэтому каждое утверждение ниже сопровождается тем, чем оно проверяется. Строка без проверки — это не «сделано», а «не измерено».

| Утверждение | Чем проверяется | Статус |
|---|---|---|
| Вне Development connection string обязателен, dev fallback не применяется | `RuntimeConfigurationTests.Production_requires_explicit_database_connection_string`, `Development_database_default_is_explicitly_development_only` | закрыто |
| Startup-миграции запрещены вне Development | `RuntimeConfigurationTests.Production_rejects_automatic_schema_migration` | закрыто |
| Keycloak требует HTTPS metadata по умолчанию | `RuntimeConfigurationTests.Production_keycloak_requires_https_metadata_by_default` | закрыто |
| CORS не принимает wildcard, список источников явный | `RuntimeConfigurationTests.Cors_requires_explicit_non_wildcard_origin_allow_list`, `EdgeSecurityTests.Cors_preflight_allows_only_configured_origin` | закрыто |
| Reverse proxy не включается без явной границы доверия; untrusted `X-Forwarded-*` игнорируется | `RuntimeConfigurationTests.Reverse_proxy_cannot_be_enabled_without_explicit_trust_boundary`, `EdgeSecurityTests.Untrusted_forwarded_for_is_ignored` | закрыто |
| Transport security в production объявляется явно | `RuntimeConfigurationTests.Production_transport_security_must_be_explicit` | закрыто |
| OpenAPI в production выключен по умолчанию, при включении закрыт ролью | `RuntimeConfigurationTests.OpenApi_defaults_to_development_only_public_exposure`, `EdgeSecurityTests.Production_OpenAPI_can_be_enabled_as_admin_only` | закрыто |
| Ключ ответов не попадает в student-контракт | `AttemptPresentationTests.The_serialized_student_payload_contains_no_correctness_information` — проверка на сериализованных байтах, подтверждена красным | закрыто |
| Изоляция по владельцу на write, read и reviewer boundary | `TestOwnershipTests`, cross-author E2E | закрыто |
| Ни один мутирующий эндпоинт не принимает чужого автора | `CrossAuthorIsolationTests.Not_a_single_mutating_endpoint_accepts_a_foreign_author` — перебираются все 12, состояние после отказа сверяется; подтверждён красным снятием одной проверки | закрыто |
| Поиск и `TotalCount` не выдают существование чужого теста | `CrossAuthorIsolationTests.Search_and_paging_count_only_the_callers_own_tests` | закрыто |
| Представления попытки принадлежат студенту, а не владельцу теста | `CrossAuthorIsolationTests.The_test_owner_reads_attempts_through_the_reviewer_route_and_no_other` | закрыто |
| Текст CLR-исключения не утекает в `ProblemDetails.detail` | `RequestValidationContractTests`, `TestAggregateTests` (STAB-008, ADR-026) | закрыто |
| Нулевой GUID не превращается в `500` | `RequestValidationContractTests.The_empty_guid_is_answered_by_the_transport_and_never_reaches_the_domain_guard` (ADR-029) | закрыто |
| Наружу не публикуется ни одного интеграционного события | `IntegrationEventCatalogTests` (ADR-033) | закрыто |
| Репозиторий не содержит секретов | шаг `Scan repository for secrets` в workflow `security` (Trivy, без исключённых файлов) | закрыто |
| Production-образ без уязвимостей HIGH/CRITICAL | шаг image scan в workflow `security` | закрыто |
| `__legacy_admin_only__` не всплывает у произвольного автора ни в одном read model | `CrossAuthorIsolationTests.No_owner_can_exist_that_the_application_never_issued` — проверять на контуре нечего: backfill жил в схлопнутых миграциях, `OwnerId` объявлен обязательным без значения по умолчанию, строка-часовой отсутствует в `src/` | закрыто |
| Отсутствие отладочных креденшелов на самом контуре | **проверка на staging, issue #16** | открыто |

### Результат sweep по креденшелам (пункт 5)

Проверены `compose.yaml`, конфигурационные файлы приложения, realm-файл Keycloak и переменные workflow.

**Приложение не содержит конфигурационных файлов вообще:** в репозитории нет ни одного `appsettings*.json`. Вся конфигурация приходит из окружения через `RuntimeConfiguration`, поэтому отладочный креденшел физически не может уехать в образ через закоммиченный файл.

**Единственные fallback-значения в коде конфигурации** — не секреты, а операционные умолчания RabbitMQ: `RabbitMq:Exchange` → `testapp.events`, `RabbitMq:RoutingKeyPrefix` → `testapp`, `RabbitMq:ClientProvidedName` → `TestApp.Outbox` (`RuntimeConfiguration.Messaging.cs`). Креденшелов среди них нет.

**Отладочные креденшелы существуют только в local-dev артефактах** и в production-путь не входят:

| Где | Что | Назначение |
|---|---|---|
| `compose.yaml` | `POSTGRES_PASSWORD: testapp`, строка подключения с `Password=testapp` | локальный стек |
| `compose.yaml` | `KC_BOOTSTRAP_ADMIN_PASSWORD: bootstrap-admin` | локальный Keycloak |
| `compose.yaml` | `GF_SECURITY_ADMIN_PASSWORD: admin` | локальная Grafana |
| `deploy/keycloak/testapp-realm.json` | пользователи `admin`/`author`/`student` с тривиальными паролями; клиент `testapp-api` — public, без секрета | fixture для тестов и Postman |
| `.github/workflows/dotnet.yml` | `POSTGRES_PASSWORD: testapp` | сервисный контейнер CI |

Ни один из этих файлов не является артефактом развёртывания: production разворачивается образом из GHCR с конфигурацией из окружения. Требование отдельного realm `testapp-staging` со своими секретами закреплено в issue #16; до его выполнения пункт остаётся открытым **для контура**, но не для приложения.

## 19. Security review checklist для новой фичи

Перед merge проверить:

- Кто имеет endpoint policy?
- Есть ли resource-level ownership/eligibility?
- Не раскрывает ли DTO correctness/PII?
- Можно ли повторить unsafe request после timeout?
- Есть ли cross-instance race?
- Какие данные попадают в audit/logs/traces/events?
- Нужно ли redaction?
- Требуется ли новый DB index для authorization query?
- Есть ли negative tests: anonymous/forbidden/wrong owner/wrong group?
- Не появляется ли secret в committed config?
- Как новая фича ведёт себя за reverse proxy/multiple replicas?
