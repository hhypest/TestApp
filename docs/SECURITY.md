# Безопасность TestApp

> Статус: authentication/authorization, owner isolation и repository-level production hardening **Implemented**. Ниже отдельно отмечены remaining stabilization и deployment-owned controls.

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

## 7. Resource ownership isolation

Текущая single-organization модель:

```text
Test.OwnerId = Keycloak sub создавшего автора
```

Реализованы:

- immutable mandatory owner при создании Test;
- owner filtering catalog/editor/revisions/reviewer list/detail в SQL;
- owner checks для rename/settings/questions/options/publish/archive;
- `test-admin` global scope;
- legacy backfill `__legacy_admin_only__`;
- cross-author negative E2E, включая correctness detail.

Workspace/Tenant/Team и ACL не реализованы сознательно. Они становятся P0 только при multi-organization deployment; текущий `OwnerId` не следует ошибочно называть tenant boundary.

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

**Implemented:** configuration-driven fixed-window policies:

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

### Audit guarantees and limitations

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

### Current development defaults

Compose использует простые credentials `testapp/testapp` и dev Keycloak credentials. Они предназначены **только для local development**.

### Production behavior

- вне Development database connection string обязателен; dev fallback там не применяется;
- Keycloak authority/audience обязательны для HTTP runtime;
- если RabbitMQ enabled — broker connection обязателен;
- HTTPS metadata требуется по умолчанию;
- secrets не логируются и должны поступать через environment/deployment secret store.

`--migrate` изолирован до database-only configuration: composition root загружает только `RuntimeConfiguration.LoadDatabase`, Keycloak/RabbitMQ/CORS/rate-limit/proxy options не загружаются.

## 14. TLS / reverse proxy

### Implemented repository contract

- JWT metadata HTTPS required вне Development;
- opt-in `UseForwardedHeaders` с explicit `KnownProxies/KnownNetworks` и `ForwardLimit`;
- configurable HTTPS redirect/HSTS;
- explicit CORS allow-list без wildcard origin;
- proxy-aware rate-limit partition;
- disabled Kestrel server banner;
- `nosniff`, `DENY`, `no-referrer`, Permissions-Policy и restrictive CSP baseline.

Конкретная TLS termination, external base URL, certificate rotation и ingress/network policy остаются deployment-owned.

## 15. OpenAPI exposure

Runtime policy configuration-driven:

- Development: enabled/anonymous по умолчанию;
- Production: disabled по умолчанию;
- если включён с `AllowAnonymous=false`, endpoint требует `operations:read`;
- anonymous production exposure возможен только как явная configuration decision.

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

- API DB role без `SUPERUSER`, database ownership и DDL privileges;
- migration job может иметь отдельный более привилегированный user;
- TLS для remote PostgreSQL;
- backup encryption;
- credential rotation;
- network allow-list/private subnet.

## 18. Security/stability backlog before 1.0

Repository-level edge security, ownership, NuGet/image scan, secret scan и SBOM уже реализованы.

Actor-scoped `StartAttempt` replay также реализован: lookup включает current `UserId`, поэтому изменение assignment/group state не ломает retry владельца и не раскрывает attempt другого пользователя.

P0/P1 до 1.0:

1. сузить secret-scan allowlist вместо полного исключения workflow/Compose files;
2. pin GitHub Actions/container dependencies immutable SHA/digest;
3. выполнить staging alert/restore/rollback security rehearsal.

Business-triggered/после 1.0:

6. `(Issuer, Subject)` identity при multi-realm;
7. sensitive-data classification до первого integration event consumer;
8. Workspace/Tenant/ACL только при multi-organization requirement;
9. compliance-grade immutable external audit sink, если он требуется нормативно.

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
