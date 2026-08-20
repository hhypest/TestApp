# Артефакты staging-контура

Здесь лежит всё, что относится только к контуру и **не должно попадать в локальный стенд**.

Это не стилистика: `compose.yaml` монтирует каталог `deploy/keycloak` целиком в
`/opt/keycloak/data/import`, поэтому любой realm-файл, положенный туда, импортируется в
локальный и CI Keycloak. Staging-realm, оказавшийся в этом каталоге, уронил Keycloak на
старте и вместе с ним workflow `postman` и `performance` — realm лежит здесь именно поэтому.

## `testapp-staging-realm.json`

Realm контура. Отличается от `deploy/keycloak/testapp-realm.json` тремя вещами, и каждая —
причина, по которой dev-realm нельзя импортировать на контур:

1. **нет пользователей вообще** — их заводит оператор вручную после импорта, поэтому в
   репозитории не лежит ни одного действующего креденшела контура;
2. **клиент confidential**, секрет подставляется из окружения при импорте;
3. **redirect/web origins** привязаны к публичному домену, а не к `localhost`.

Роли, группы **и protocol mappers** совпадают с dev-realm намеренно: авторизация должна
проверяться та же. Мапперы здесь не косметика — приложение читает роли из claim `roles`
(`RoleClaimType` в `Program.cs`), группы из claim `groups` (`KeycloakClaimsMapper`) и требует
audience `testapp-api`. Realm без мапперов стартует, выдаёт токен и пропускает вход, но любой
запрос отвечает 401/403 одинаково для всех ролей — то есть выглядит как поломка приложения.
Так и было до `KeycloakImportDirectoryTests.Both_realms_issue_the_claims_the_application_reads`,
который теперь держит оба файла на одном контракте.

Подстановка `$(env:VAR)` выполняется Keycloak при импорте; переменные приходят из `.env`
через `compose.staging.yaml`.

## `Caddyfile`

Ingress контура: терминирует TLS и проксирует API, Keycloak под `/auth` и Grafana под
`/grafana`. Путь `/auth` обязан совпадать с `KC_HOSTNAME`, иначе issuer в токене не сойдётся
с `Keycloak__Authority` и валидация JWT будет падать.

## `init-keycloak-db.sh`

Создаёт отдельную базу для Keycloak в том же инстансе PostgreSQL. Выполняется образом
postgres один раз, при инициализации пустого тома.
