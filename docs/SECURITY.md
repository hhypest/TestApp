# Безопасность TestApp

> Статус: базовая authentication/authorization/audit инфраструктура **Implemented**; production hardening и ownership isolation — **Planned/P0**.

## 1. Security model overview

TestApp использует два уровня контроля:

1. **Transport/system authorization** — JWT + ASP.NET Core policies по Keycloak roles.
2. **Business authorization** — проверки текущего actor внутри Application use cases (например assignment target, attempt ownership).

Эти уровни нельзя смешивать: наличие роли не заменяет domain/business eligibility.

## 2. Identity Provider

Keycloak является внешним source of truth для:

- users;
- groups;
- coarse system roles.

TestApp не хранит локальный password hash, login/email credential state и не выдаёт access tokens.

## 3. JWT configuration

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

## 4. External identity types

Application/Domain используют:

- `ExternalUserId`;
- `ExternalGroupId`.

JWT/ClaimsPrincipal преобразуется в эти типы в Infrastructure (`HttpCurrentActor`). Domain от Identity Provider не зависит.

## 5. Roles и policies

| Role | Intended scope |
|---|---|
| `test-author` | authoring + publication + reviewer results |
| `test-admin` | authoring + publication + assignment administration + reviewer + operations |
| authenticated user without role | own assignment/attempt flow |

Policies:

| Policy | Roles |
|---|---|
| `tests:write` | `test-author`, `test-admin` |
| `tests:publish` | `test-author`, `test-admin` |
| `tests:assign` | `test-admin` |
| `results:review` | `test-author`, `test-admin` |
| `operations:read` | `test-admin` |

## 6. Business authorization

### Start attempt

Роль не требуется, но Application проверяет:

- direct user target либо membership в assignment group;
- assignment availability;
- cancellation;
- attempt limit.

### Student attempt operations

Answer/clear/submit/detail/result разрешены только когда:

```text
attempt.UserId == currentActor.UserId
```

### Admin timeout

Manual timeout защищён `tests:assign` и не использует student ownership.

## 7. Critical ownership gap

### Текущее состояние

`Test` не хранит `OwnerId/CreatedBy/TeamId/TenantId`.

Следствия:

- `test-author` catalog не фильтруется по владельцу;
- write handlers получают test по `TestId`, но не сравнивают owner;
- `results:review` не ограничивается тестами конкретного автора.

### Риск

Для доверенной единой author-группы это может быть допустимо. Для нескольких независимых подразделений/клиентов — это data isolation vulnerability.

### Planned P0/P1 fix

Ввести явную ownership model. Минимальный вариант:

```text
Test.CreatedBy / OwnerId = ExternalUserIdentity
```

Более масштабируемый вариант:

```text
Workspace/Tenant/Team
Test.WorkspaceId
WorkspaceMembership + role
```

Решение должно быть принято до multi-tenant production.

## 8. Multi-realm gap

Текущий `ExternalUserId` основан на `sub`.

При нескольких Keycloak realms/OIDC issuers одинаковый `sub` теоретически может появиться у разных issuers.

Planned identity key:

```text
ExternalIdentity(Issuer, Subject)
```

или стабильный internal principal ID, связанный с `(iss, sub)`.

Изменение затронет:

- assignments;
- attempts;
- idempotency;
- audit;
- author ownership;
- indexes/migrations;
- integration events.

## 9. Correct-answer data boundary

Correctness считается sensitive assessment data.

### Allowed

- Domain/PublishedTestRevision;
- editor view для author/admin;
- reviewer detail по `results:review`.

### Forbidden

Student endpoints не должны раскрывать:

- `IsCorrect`;
- correct option IDs как отдельный answer key;
- published revision raw JSON;
- reviewer DTO.

Regression tests должны проверять этот boundary при изменении read models.

## 10. Rate limiting

**Implemented:** fixed-window global limiter.

- partition: `sub`, fallback remote IP;
- 120 requests/minute;
- queue 0;
- 429 on rejection.

### Current weaknesses

- hard-coded values;
- одинаковая policy для GET catalog и high-frequency answer PUT;
- нет отдельной auth/operations policy;
- remote IP может быть неверным за reverse proxy без trusted forwarded headers.

### Planned

Конфигурация:

```text
RateLimiting:Global:PermitLimit
RateLimiting:Global:WindowSeconds
RateLimiting:StudentWrite:...
RateLimiting:Operations:...
```

Плюс trusted proxy configuration до использования client IP как security partition.

## 11. Correlation и audit

Header:

```text
X-Correlation-ID
```

Принимается только непустое значение <= 128 chars, иначе генерируется.

State-changing methods:

```text
POST PUT PATCH DELETE
```

пишутся в `audit_entries`.

Audit содержит metadata, но **не request/response body**.

Это снижает риск сохранения:

- access tokens;
- correct answers payload;
- PII из будущих форм;
- secrets.

### Current audit limitation

Audit entry создаётся best-effort после request execution. Ошибка audit persistence логируется, но не меняет business response. Это правильная availability trade-off для текущего уровня, но compliance-сценарий может потребовать другую гарантию.

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

### Current development defaults

Compose использует простые credentials `testapp/testapp` и dev Keycloak credentials. Они предназначены **только для local development**.

### Critical current gap

`Program.cs` имеет fallback MariaDB connection string с development credentials, если `ConnectionStrings:Database` отсутствует.

Production должен перейти на fail-fast:

- вне Development connection string обязателен;
- Keycloak authority/audience обязательны;
- если RabbitMQ enabled — broker connection обязателен;
- secrets не должны находиться в committed production config;
- environment/secret store injection.

## 14. TLS / reverse proxy

### Implemented

JWT metadata HTTPS required вне Development.

### Not yet formalized

- `UseForwardedHeaders` с KnownProxies/KnownNetworks;
- HSTS policy;
- HTTPS redirection strategy за ingress;
- secure headers/CSP для будущего UI;
- CORS allow-list;
- external base URL;
- proxy-aware rate limiting.

Эти задачи входят в P0 production hardening.

## 15. OpenAPI exposure

Сейчас `/openapi/v1.json` anonymous.

Для production требуется явное решение:

- public documentation — оставить anonymous;
- private API — ограничить/отключить в Production;
- internal gateway — защищать network boundary.

Это policy decision, не должно оставаться случайным default.

## 16. RabbitMQ security

При enabled transport connection string может содержать credentials и должен поступать из secret source.

Production recommendation:

- отдельный RabbitMQ user/vhost;
- minimum permissions только на нужный exchange;
- `amqps://` при выходе за trusted private network;
- rotation plan;
- не использовать `guest`/default administrator;
- broker management UI отдельно от application traffic.

## 17. Database security

Production:

- API DB user без root privileges;
- migration job может иметь отдельный более привилегированный user;
- TLS для remote MariaDB;
- backup encryption;
- credential rotation;
- network allow-list/private subnet.

## 18. Security backlog before 1.0

P0:

1. fail-fast production configuration;
2. remove insecure DB fallback outside Development;
3. trusted forwarded headers/proxy setup;
4. explicit CORS/TLS/HSTS policy;
5. configuration-driven rate limits;
6. OpenAPI production exposure policy;
7. ownership boundary для tests/results;
8. secret handling documentation + deployment injection;
9. dependency vulnerability scanning in CI.

P1:

10. `(Issuer, Subject)` identity;
11. standard `Idempotency-Key` header validation;
12. security regression matrix by role/resource owner;
13. audit retention and access review;
14. sensitive-data classification for integration events;
15. optional workspace/tenant model.

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