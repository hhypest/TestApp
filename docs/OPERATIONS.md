# Эксплуатация и deployment TestApp

> Статус: local/container runtime и repository operational automation D1–D6 **Implemented**; platform-specific secret store, PITR, alert routing и release rehearsal остаются deployment work.

## 1. Топология времени выполнения

Стандартный local Compose stack:

```mermaid
graph TD
    Client --> API[TestApp API :8080]
    API --> DB[PostgreSQL 18 :5432]
    API --> KC[Keycloak 26.7 :8080 internal / :8081 host]
    API --> RMQ[RabbitMQ 4.3.1 :5672]
    API --> OTEL[OTEL Collector :4317]
    OTEL --> PROM[Prometheus :9090]
    PROM --> GRAF[Grafana :3000]
    MIG[Migrate job] --> DB
```

Сервисы Compose:

- `postgres`;
- `rabbitmq`;
- `keycloak`;
- `otel-collector`;
- `prometheus`;
- `grafana`;
- `migrate`;
- `api`.

## 2. Быстрый локальный старт

Перед первым запуском:

- Docker Engine или совместимая среда выполнения;
- Docker Compose версии 2.

Запуск:

```bash
docker compose up --build
```

PostgreSQL использует новый volume `testapp-postgres`. Старый MariaDB volume не удаляется автоматически. Не применяйте `docker compose down -v`, пока не подтверждены backup/cutover и допустимость удаления всех Compose volumes.

### Порты на хосте

| Service | Host |
|---|---|
| API | `http://localhost:8080` |
| Keycloak | `http://localhost:8081` |
| PostgreSQL | `localhost:5432` |
| RabbitMQ AMQP | `localhost:5672` |
| Панель управления RabbitMQ | `http://localhost:15672` |
| OTLP gRPC | `localhost:4317` |
| OTLP HTTP | `localhost:4318` |
| Prometheus | `http://localhost:9090` |
| Grafana | `http://localhost:3000` |

## 3. Учётные данные для разработки

**Только local development. Не использовать в production.**

PostgreSQL: `testapp / testapp` (локальный Compose superuser; в production application и migration roles должны быть разделены).

Grafana: `admin / admin` (`GF_SECURITY_ADMIN_USER`/`GF_SECURITY_ADMIN_PASSWORD` в `compose.yaml`).

RabbitMQ:

```text
testapp/testapp
```

Начальная учётная запись Keycloak:

```text
bootstrap-admin / bootstrap-admin
```

Пользователи dev-realm:

```text
admin   / admin   -> test-admin
author  / author  -> test-author
student / student -> group students
```

Импорт realm Keycloak:

```text
deploy/keycloak/testapp-realm.json
```

## 4. Docker-образ

`Dockerfile` — многоэтапный образ сборки и выполнения .NET.

Эксплуатационные ожидания:

- runtime process слушает configured ASP.NET port;
- container работает от non-root application user;
- тот же image используется и для API, и для migration-only mode;
- schema migration не требует отдельного tooling image.

## 5. Стратегия миграций

### 5.1 Команда режима только миграций

```bash
dotnet TestApp.Api.dll --migrate
```

В container:

```bash
docker run ... testapp-api:<tag> --migrate
```

### 5.2 Порядок запуска в локальном Compose

```text
PostgreSQL healthy
    ↓
migrate container --migrate
    ↓
migrate exits 0
    ↓
API starts
```

### 5.3 Порядок в production

Recommended:

```text
build immutable image
    ↓
backup / verify restore point
    ↓
run one migration job
    ↓
verify migration exit + schema/readiness
    ↓
roll API replicas
    ↓
post-deploy smoke tests
```

API replicas не должны одновременно применять schema migrations.

`Database:ApplyMigrationsOnStartup` в Production должен быть `false`/default false.

## 6. Каталог конфигурации

### Database

```text
ConnectionStrings:Database
Database:ApplyMigrationsOnStartup
```

Outside Development connection string обязателен; development fallback применяется только в Development. Startup migrations вне Development запрещены.

### Keycloak

```text
Keycloak:Authority
Keycloak:Audience
```

JWT metadata HTTPS required вне Development.

### RabbitMQ

```text
RabbitMq:Enabled
RabbitMq:ConnectionString
RabbitMq:Exchange
RabbitMq:RoutingKeyPrefix
RabbitMq:ClientProvidedName
```

Если `Enabled=false/absent`, Outbox delivery worker не должен зависеть от broker.

### OpenTelemetry

Стандартные env variables:

```text
OTEL_SERVICE_NAME=TestApp.Api
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
OTEL_EXPORTER_OTLP_PROTOCOL=grpc
```

Без `OTEL_EXPORTER_OTLP_ENDPOINT` exporter не подключается.

### Истечение попыток

Значения Infrastructure по умолчанию:

```text
BatchSize = 100
PollInterval = 30 seconds
```

Options уже загружаются/валидируются из `AttemptExpiration:BatchSize` и `AttemptExpiration:PollIntervalSeconds`.

### Доставка Outbox

Внутренние значения по умолчанию:

```text
BatchSize = 100
MaxAttempts = 10
PollInterval = 5 sec
BaseRetryDelay = 5 sec
MaxRetryDelay = 15 min
AdvisoryLockTimeoutSeconds = 5
```

Options загружаются/валидируются из секции `Outbox`. При invalid range startup завершается fail-fast.

### Композиция режима только миграций

`--migrate` загружает только `RuntimeConfiguration.LoadDatabase`; Keycloak/RabbitMQ/worker/CORS/rate-limit/OpenAPI/reverse-proxy configuration не загружается и соответствующие DI-регистрации пропускаются. Migration job/container может получать только `ConnectionStrings:Database` (и опционально `Database:ApplyMigrationsOnStartup`) без остальных runtime secrets/config — см. `compose.yaml` сервис `migrate`.

## 7. Health-эндпоинты

### Liveness

```text
GET /health/live
```

Назначение: процесс способен отвечать. Не должен зависеть от PostgreSQL/RabbitMQ, иначе transient dependency failure может вызвать restart loop.

### Readiness

```text
GET /health/ready
```

Проверяет:

- доступность PostgreSQL;
- RabbitMQ connection/channel/exchange access, **только если RabbitMQ delivery включён**.

При critical dependency failure readiness должна вернуть unhealthy/503 и исключить instance из traffic.

## 8. OpenTelemetry

Текущая instrumentation:

- запросы ASP.NET Core;
- исходящие запросы HttpClient;
- метрики среды выполнения .NET.

Health endpoints исключаются из request traces, чтобы probes не создавали telemetry noise.

Конфигурация локального коллектора:

```text
deploy/otel-collector-config.yaml
```

Collector пишет traces только в `debug` exporter (нет configured traces backend). Metrics пишутся в `debug` **и** `prometheus` exporter (`0.0.0.0:8889`), который Compose-сервис `prometheus` scrape'ит как target `testapp` (`deploy/prometheus/prometheus.yml`). Prometheus также загружает alert rules из `deploy/prometheus/alerts.yml`, реализующие пороги `docs/SLO_ALERTS.md` §4. Grafana подключена к Prometheus как provisioned datasource (`deploy/grafana/provisioning`) и показывает dashboard `TestApp Overview` (`deploy/grafana/dashboards/testapp-overview.json`), реализующий "dashboard minimum" из `docs/SLO_ALERTS.md` §6. См. ADR-027 в `docs/DECISIONS.md` за обоснованием выбора Prometheus/Grafana вместо Jaeger.

`scripts/validate-observability-stack.sh` (запускается CI workflow `observability`) проверяет: `promtool check config/rules`, здоровье Prometheus targets, наличие ожидаемых metric families, здоровье Grafana datasource и присутствие dashboard.

### Известные ограничения

- В локальном `compose.yaml` Alertmanager намеренно не поднимается. В staging маршрутизация
  настроена через `compose.staging.yaml`, но реальный receiver и доставка до него должны быть
  проверены alert drill'ом (`docs/SLO_ALERTS.md` §7, `docs/ROADMAP.md` Phase E item 12).
- Активный HTTP-пробинг `/health/ready` работает через `blackbox_exporter` локально и на
  staging; дополнительная staging-проба проверяет ingress. Внешний DNS, роутер и проброс
  портов она не покрывает, потому что выполняется из сети compose.
- Трейсы (`traces` pipeline) по-прежнему уходят только в `debug` exporter — выбор backend для трейсинга (Tempo/Jaeger/другой) остаётся открытым и не обязателен, пока нет измеренной потребности в distributed tracing (см. ADR-027).

## 9. Структурированная телеметрия запросов

`RequestTelemetryMiddleware` логирует:

- HTTP-метод;
- path;
- код статуса;
- длительность в мс;
- идентификатор трассировки и корреляции.

Не следует добавлять в request logs raw access token, passwords, correct-answer payload или arbitrary body.

## 10. Correlation

`X-Correlation-ID`:

- принимается от клиента при валидной длине;
- генерируется при отсутствии;
- возвращается в response;
- используется как `TraceIdentifier`;
- попадает в audit.

При инциденте основной join key:

```text
client correlation ID
    ↔ HTTP log
    ↔ audit_entries.CorrelationId
    ↔ trace ID
```

## 11. Операции аудита

Endpoint:

```text
GET /api/v1/operations/audit
role: test-admin
```

Filters:

- actorId;
- statusCode;
- from;
- to;
- page/pageSize.

Audit записывается для:

- POST;
- PUT;
- PATCH;
- DELETE.

Audit не содержит body/query payload.

### Retention

Repository-level bounded cleanup реализован для audit, idempotency и processed Outbox rows:

- типизированные сроки хранения, размер пакета и интервал опроса;
- PostgreSQL advisory lock между replicas;
- ограниченные удаления, дружественные к индексам;
- pending/retrying/active dead-letter Outbox rows не удаляются;
- deleted-row counters экспортируются через `TestApp.Operations`.

Deployment owner всё ещё определяет фактические retention periods, archival/export и необходимость immutable/WORM external audit sink.

### Контракт статуса аудита

Audit/correlation middleware оборачивает exception mapping и сохраняет финальный HTTP status. Regression coverage подтверждает равенство response/audit для handled `400/409`, precondition `412` и real `500`. При incident triage correlation ID остаётся ключом сопоставления audit, HTTP log и trace.

## 12. Операции Outbox

Endpoint:

```text
GET /api/v1/operations/outbox
role: test-admin
```

Использовать для проверки:

- растущая очередь необработанных;
- рост числа повторов;
- количество dead-letter;
- возраст самого старого необработанного;
- повторяющаяся причина ошибки.

### Runbook: Outbox backlog растёт

1. Проверить `/health/ready`.
2. Проверить `RabbitMq:Enabled` и broker DNS/network/auth.
3. Проверить RabbitMQ exchange/permissions.
4. Проверить application logs по OutboxMessageId.
5. Проверить dead-letter state.
6. Не удалять rows вручную до выяснения причины.
7. После восстановления transport processor автоматически продолжит due retries.

### Runbook: сообщение в dead-letter

1. Зафиксировать message ID/type/error/attempt count.
2. Проверить, временная это ошибка или permanent schema/routing issue.
3. Исправить consumer/broker/config/application.
4. Получить safe detail без payload: `GET /api/v1/operations/outbox/dead-letters/{eventId}`.
5. После устранения причины выполнить audited `POST .../{eventId}/requeue` с mandatory reason.
6. Если message доказанно устарел, выполнить audited `POST .../{eventId}/discard`; это terminal state, не physical delete.
7. Не редактировать payload и delivery columns вручную.

## 13. Операции истечения попыток

`OverdueAttemptProcessor` работает внутри API process.

### Если expired attempts остаются InProgress

Проверить:

- application instance жив;
- логи запуска воркера;
- доступность БД;
- отметки времени дедлайнов;
- конфликты параллельного доступа;
- error logs по AttemptId.

Worker batch-based и eventual: изменение не обязано произойти ровно в момент deadline; default scan cadence около 30 секунд.

### Восстановление воркера на уровне цикла

Per-attempt processing exceptions логируются; initial/batch DB scan также покрыт cycle-level recovery boundary (`RunCycleAsync`) — как у expiration worker, так и у Outbox batch query/lock/failure-state persistence. Transient dependency failure логируется и worker продолжает на следующий poll tick вместо завершения hosted worker/process.

## 14. Эксплуатационная политика PostgreSQL 18

CI выполняет `SHOW server_version_num` и гарантирует PostgreSQL 18+ (`>= 180000`).

### Правило обновления

Перед изменением PostgreSQL line:

1. backup;
2. проверить vendor upgrade path;
3. поднять isolated copy;
4. прогнать migrations;
5. прогнать полный integration suite;
6. проверить advisory locks;
7. проверить locale/collation/extensions/indexes;
8. выполнить production-image `--migrate`;
9. только после этого менять production.

Нельзя считать смену Docker tag достаточным production upgrade process.

## 15. Backup/restore/DR

Repository baseline реализован:

- `scripts/postgresql-backup.sh` создаёт consistent custom-format archive + SHA-256;
- `scripts/postgresql-restore-verify.sh` восстанавливает только в disposable target database;
- проверяются table counts, EF migration history и optional business marker;
- `RESTORE_EVIDENCE_PATH` атомарно сохраняет приватный JSON (`0600`) с database recovery
  timing и не перезаписывает прошлое доказательство при неуспехе;
- основной CI выполняет recovery drill после migration production image;
- инженерные цели: RPO <= 24 ч, RTO <= 4 ч.

До production deployment необходимо:

- настроить schedule, encrypted offsite storage и retention;
- выполнить staging/platform restore drill и записать actual RTO;
- для более строгого RPO включить provider-native snapshot/WAL/PITR;
- определить credential/Keycloak realm restore и rotation;
- уметь пересоздать RabbitMQ topology; broker не является единственным source of truth благодаря Outbox.

Полный runbook: `BACKUP_RESTORE.md`.

`databaseRecoverySeconds` из repository-level evidence — нижняя граница, а не фактический
RTO приложения. Issue #18 закрывается только после прибавления времени до `/health/ready`
и показательного бизнес-запроса на восстановленном staging-экземпляре.

### Проверка производительности D6

PostgreSQL baseline подтверждён полным green run на implementation commit `9916b98`. Artifact содержит 8771/8771 successful checks, HTTP failure rate 0, expiration drain 1247 -> 0 за 14 s и Outbox drain 100 -> 0 за 1 s. RabbitMQ probe использует поддерживаемую 4.3 durable queue topology в одноразовом CI volume.

## 16. Чек-лист дымового прогона после развёртывания

После rollout:

```text
/health/live -> 200
/health/ready -> healthy
/openapi/v1.json -> согласно production policy
JWT auth -> работает
GET /api/v1/me/assignments -> expected auth behavior
PostgreSQL version -> expected
migration history -> no pending migrations
Outbox status -> no unexpected backlog
OTEL -> traces/metrics arrive
```

Для migration release дополнительно выполнить representative create/publish/assign/start/submit flow в staging.

## 16.0 Staging-контур

Контур гейта 1.0 (issue #16, ADR-031). Одна VM, `compose.staging.yaml`, наружу опубликован только ingress.

### Что нужно от машины

4 vCPU / 8 ГБ — разумный минимум для десяти контейнеров, из которых Keycloak на JVM, Prometheus и Grafana — основные потребители памяти. Это оценка; фактическую цифру даст `#20`.

**k6 запускать с другой машины.** Генератор нагрузки на том же хосте конкурирует за CPU с системой под тестом, и `p95` измерит борьбу за планировщик, а не ёмкость.

### Развёртывание

```bash
git clone <repo> && cd TestApp
cp .env.staging.example .env && chmod 600 .env    # заполнить, пароли генерировать: openssl rand -base64 32
docker compose -f compose.staging.yaml --env-file .env up -d
```

`TESTAPP_IMAGE` задаётся **digest'ом** из тела GitHub Release, не тегом. `TESTAPP_INTERNAL_CIDR` узнаётся после первого запуска:

```bash
docker network inspect testapp-staging_default -f '{{(index .IPAM.Config 0).Subnet}}'
```

### Обязательная проверка после первого запуска

Лимит частоты ключуется по claim `sub`, а для анонимных запросов — по `RemoteIpAddress`, при этом `ForwardedHeaders` доверяет только объявленным сетям. Если `ReverseProxy:KnownNetworks` задан неверно, весь анонимный трафик схлопнется в одну партицию по адресу ingress: `#20` упрётся в `429`, не связанные с ёмкостью, а `#19` откалибрует по этим цифрам пороги. Обе ошибки выглядят как настоящие измерения.

Проверить до первого нагрузочного прогона — в audit trail должны быть **клиентские** адреса, а не адрес ingress:

```bash
curl -s -H "Authorization: Bearer $ADMIN_TOKEN" \
  "https://$TESTAPP_PUBLIC_HOST/api/v1/operations/audit?page=1&pageSize=5"
```

### Фикстурные пользователи

Realm `testapp-staging` импортируется **без пользователей** — в репозитории не лежит ни одного действующего креденшела контура. Автора, администратора и студента заводит оператор вручную в консоли Keycloak (`/auth`), с ролями `test-author`, `test-admin` и группой `students` соответственно, паролями из `.env`. Клиент `testapp-api` — confidential; его секрет нужен k6 и Postman.

Сотни студентов руками не заводят: их создаёт `scripts/seed-staging.sh` при засеве объёма (§16.0.3). Автор и администратор всё равно заводятся вручную — их два, и права у них разные.

### Проверка готовности

```text
GET https://<host>/health/live                    -> 200
GET https://<host>/health/ready                   -> healthy
GET https://<host>/api/v1/operations/version      -> версия и digest совпадают с развёрнутым
дашборд TestApp Overview в /grafana               -> живой трафик
панель «API readiness (active probe)»             -> READY, а не NO DATA
Prometheus /targets                               -> testapp-readiness и testapp-ingress up
```

`NO DATA` на панели готовности означает, что page-правило 1 не сработает ни при каком отказе: серии `probe_success` нет, выражение `== 0` не даёт результата, и молчание неотличимо от благополучия (ADR-034). Проверять до того, как контур начнут считать наблюдаемым.

Проб две, и они отвечают на разные вопросы: `testapp-readiness` ходит внутрь (`приложение готово`), `testapp-ingress` — по публичному адресу через ingress (`клиент до него дойдёт`). Вторая требует файла цели, который генерируется здесь же:

```bash
printf '[{"targets":["https://%s/health/ready"]}]\n' "$TESTAPP_PUBLIC_HOST" \
  > deploy/staging/targets/ingress.json
```

Без него job остаётся без целей и молчит; на это молчание есть отдельное правило `TestAppIngressProbeMissing`, но проще не доводить. Подробности — `deploy/staging/targets/README.md`.

Развёрнутый digest записать — он потребуется для отката в `#23`.

## 16.0.1 Первый подъём контура

Одноразовая процедура. Отличается от штатного развёртывания (§16.0) тем, что образа в GHCR ещё нет, `.env` не заполнен, а realm импортируется впервые. Дальнейшие развёртывания — §16.0 и §16.2.

Выполнять по порядку: каждый шаг проверяем прежде, чем идти дальше. Отказ на пятом шаге, вызванный ошибкой на втором, диагностируется втрое дольше.

### Шаг 1. Образ в GHCR

Штатно `TESTAPP_IMAGE` берётся digest'ом из тела GitHub Release, но ни одного релиза ещё нет. Есть два пути; **рекомендуется первый**.

**Через `release` workflow на временном теге.** Заодно это первый реальный прогон `release.yml`, который до сих пор проверялся только синтаксически — лучше узнать о его проблемах здесь, чем при выпуске RC.

```bash
git tag v1.0.0-preflight.1 <коммит>
git push origin v1.0.0-preflight.1
```

Тег обязан начинаться с `v` и согласовываться с `VersionPrefix` из `Directory.Build.props` — workflow это проверяет и падает при расхождении. Суффикс `preflight`, а не `rc`, чтобы не занимать имя первого кандидата.

Digest берётся из тела созданного Release, строка «Развернуть по digest».

**Вручную**, если workflow недоступен:

```bash
docker build -t ghcr.io/hhypest/testapp:preflight .
docker push ghcr.io/hhypest/testapp:preflight
docker inspect --format='{{index .RepoDigests 0}}' ghcr.io/hhypest/testapp:preflight
```

Имя образа **в нижнем регистре** — Docker не принимает заглавные в имени репозитория, хотя сам репозиторий называется `hhypest/TestApp`.

### Шаг 2. Доступ к GHCR с машины контура

Пакет приватного репозитория по умолчанию приватный, поэтому анонимный `docker pull` вернёт `denied`. Нужен PAT с областью `read:packages`:

```bash
echo "$GHCR_TOKEN" | docker login ghcr.io -u <github-логин> --password-stdin
docker pull ghcr.io/hhypest/testapp@sha256:<digest>    # проверка доступа до подъёма стека
```

Если `pull` не проходит — дальше идти бессмысленно: контейнеры `migrate` и `api` не стартуют.

### Шаг 3. Окружение

```bash
git clone <repo> && cd TestApp
cp .env.staging.example .env && chmod 600 .env
```

Заполнить. Пароли **генерировать**, а не придумывать: `openssl rand -base64 32`. `TESTAPP_IMAGE` и `TESTAPP_IMAGE_DIGEST` — из шага 1; первый в форме `образ@sha256:...`, второй — только `sha256:...`.

`TESTAPP_INTERNAL_CIDR` на этом шаге ещё неизвестен: сети не существует, пока стек не поднят. Оставить значение из шаблона, исправить на шаге 5.

DNS-запись домена из `TESTAPP_PUBLIC_HOST` должна указывать на машину **до** первого запуска: Caddy запрашивает сертификат при старте, и без резолва получит отказ, а Let's Encrypt ограничивает частоту повторов.

### Шаг 4. Первый запуск

```bash
docker compose -f compose.staging.yaml --env-file .env up -d
docker compose -f compose.staging.yaml --env-file .env ps
```

Порядок гарантирован `depends_on`: PostgreSQL → init-скрипт создаёт базу Keycloak → `migrate` отрабатывает и завершается → стартует `api`.

Проверить, что миграции применились и контейнер завершился успешно, а не молча упал:

```bash
docker compose -f compose.staging.yaml --env-file .env logs migrate | tail -20
```

Ожидаемо: `migrate` в состоянии `exited (0)`. Ненулевой код — читать логи, API всё равно не поднимется.

### Шаг 5. Доверенная сеть — до любых измерений

Узнать фактический CIDR и вписать его в `.env`:

```bash
docker network inspect testapp-staging_default -f '{{(index .IPAM.Config 0).Subnet}}'
```

Если значение отличается от того, что в `.env`, — исправить и перезапустить API:

```bash
docker compose -f compose.staging.yaml --env-file .env up -d --force-recreate api
```

**Почему это нельзя отложить.** Лимит частоты ключуется по claim `sub`, а для анонимных запросов — по `RemoteIpAddress`; `ForwardedHeaders` доверяет только объявленным сетям. При неверном CIDR весь анонимный трафик схлопнется в одну партицию по адресу ingress. Нагрузочный прогон `#20` упрётся в `429`, не связанные с ёмкостью, эти цифры уйдут в `PERFORMANCE.md` как baseline 1.0, а по ним откалибруются пороги алертов `#19`. Обе ошибки будут выглядеть как настоящие измерения.

### Шаг 6. Пользователи realm

Realm `testapp-staging` импортируется **без пользователей**. Завести вручную в консоли Keycloak (`https://<host>/auth`, вход по `KEYCLOAK_ADMIN_USER`):

| Пользователь | Роль realm | Группа |
|---|---|---|
| автор | `test-author` | — |
| администратор | `test-admin` | — |
| студент | — | `students` |

Пароли — те, что записаны в `.env` (`AUTHOR_PASSWORD` и остальные): их читают k6 и Postman.

### Шаг 7. Проверка

```bash
curl -sf https://<host>/health/live                 # 200
curl -sf https://<host>/health/ready                # healthy
```

Токен и версия:

```bash
TOKEN=$(curl -s -X POST "https://<host>/auth/realms/testapp-staging/protocol/openid-connect/token" \
  -d client_id=testapp-api -d client_secret="$TESTAPP_KEYCLOAK_CLIENT_SECRET" \
  -d grant_type=password -d username="$ADMIN_USERNAME" -d password="$ADMIN_PASSWORD" \
  | python3 -c 'import sys,json;print(json.load(sys.stdin)["access_token"])')

curl -s -H "Authorization: Bearer $TOKEN" https://<host>/api/v1/operations/version
```

Ответ обязан содержать версию и **тот самый digest**, который развёрнут. Расхождение означает, что `TESTAPP_IMAGE_DIGEST` не соответствует `TESTAPP_IMAGE` — до исправления откат в `#23` будет выполняться вслепую.

Затем — проверка доверенной сети из шага 5:

```bash
curl -s -H "Authorization: Bearer $TOKEN" "https://<host>/api/v1/operations/audit?page=1&pageSize=5"
```

В записях должны быть **клиентские** адреса, а не адрес контейнера ingress.

Наконец, дашборд `TestApp Overview` в `https://<host>/grafana` должен показывать живой трафик — значит цепочка API → OTel Collector → Prometheus → Grafana собрана.

### Шаг 8. Зафиксировать

Записать в `#16`: развёрнутый digest, фактический CIDR, дату подъёма и владельца машины. Digest потребуется для отката в `#23`, CIDR — при пересоздании сети.

### Если что-то пошло не так

| Симптом | Наиболее вероятная причина |
|---|---|
| `denied` при старте `migrate`/`api` | нет `docker login ghcr.io` (шаг 2) или образ приватный |
| Caddy не получает сертификат | DNS не резолвится в машину, либо порт 80 закрыт фаерволом |
| Keycloak падает на старте | база из `KEYCLOAK_DB` не создана — том инициализировался до появления init-скрипта; пересоздать том |
| `401` на любой запрос с валидным токеном | `Keycloak__Authority` не совпадает с issuer токена; сверить `KC_HOSTNAME` и путь `/auth` |
| `429` в спокойном режиме | неверный `TESTAPP_INTERNAL_CIDR` (шаг 5) |
| `/operations/version` отдаёт `imageDigest: null` | `TESTAPP_IMAGE_DIGEST` не задан в `.env` |

## 16.0.2 Минимум для внутреннего пилота

Гейт 1.0 открыт по семи пунктам, и это осознанно: контур поднимается раньше, чем гейт пройден. Но два сценария отказа нельзя оставлять открытыми даже для внутреннего пилота, потому что цена у них — необратимая.

| Сценарий | Чем закрыт |
|---|---|
| Авария, о которой никто не узнал | Alertmanager в `compose.staging.yaml` |
| Данные, которые нельзя вернуть | `scripts/backup-offsite.sh` по расписанию |

Оба закрывают **доставку**, а не проверку. Полный объём `#18` (восстановление с измеренным RTO) и `#19` (три drill'а с калибровкой порогов) остаётся открытым: наличие копии в хранилище не означает, что из неё можно восстановиться, а доставка алерта не означает, что порог выбран верно.

### Куда уходят алерты

URL webhook'а — секрет: у Slack и Mattermost он даёт право писать в канал. Поэтому он не в конфиге, а в отдельном файле, который создаёт оператор:

```bash
printf '%s' 'https://hooks.example.org/services/...' > deploy/staging/webhook-url
chmod 600 deploy/staging/webhook-url
```

Файл в `.gitignore`. Без него Alertmanager **не стартует** — и это правильно: молчащий приёмник хуже явного отказа, по той же причине, по которой ADR-014 запрещает no-op publisher как success path.

Маршрутизация разбирает алерты по метке `severity`, которую проставляют правила: `page` ждёт 10 секунд и повторяется раз в час, `warning` — 30 секунд и раз в четыре часа. Пока горит `page`, `warning` того же алерта подавляется.

Проверить доставку до того, как понадобится:

```bash
docker compose -f compose.staging.yaml --env-file .env exec alertmanager \
  amtool alert add TestDelivery severity=page --alertmanager.url=http://localhost:9093
```

Сообщение обязано прийти в канал. Если не пришло — разбираться сейчас, а не в момент аварии.

### Бэкапы с машины

Локальный том рядом с базой не переживёт отказ диска, поэтому копия обязана уехать. `scripts/backup-offsite.sh` снимает дамп, шифрует его **до** выгрузки и отправляет командой, которую задаёт развёртывание:

```bash
BACKUP_AGE_RECIPIENT=age1... \
BACKUP_UPLOAD_CMD='rclone copyto {} remote:testapp-backups/' \
POSTGRES_DOCKER_NETWORK=testapp-staging_default \
POSTGRES_HOST=postgres POSTGRES_PASSWORD=... \
  bash scripts/backup-offsite.sh
```

`POSTGRES_DOCKER_NETWORK` обязателен: на контуре порт PostgreSQL наружу не публикуется, поэтому `pg_dump` запускается в сети compose, а не в сети хоста. По умолчанию скрипты используют `host` — так работает CI, где база опубликована на `127.0.0.1`.

По расписанию, ежедневно ночью:

```cron
17 3 * * * cd /opt/TestApp && BACKUP_AGE_RECIPIENT=age1... BACKUP_UPLOAD_CMD='...' POSTGRES_DOCKER_NETWORK=testapp-staging_default POSTGRES_HOST=postgres POSTGRES_PASSWORD=... bash scripts/backup-offsite.sh >> /var/log/testapp-backup.log 2>&1
```

Ключ age, которым шифруются копии, хранить **не на этой машине**: копия, расшифровываемая только тем диском, который вы потеряли, бесполезна.

### Чего это не даёт

RPO остаётся суточным: между ночными выгрузками изменения существуют в одном экземпляре. Для внутреннего пилота это приемлемо, если участники предупреждены; для внешних пользователей нужен WAL-архив, и `docs/BACKUP_RESTORE.md` §1 прямо относит это к ответственности развёртывания.

Восстановление ни разу не выполнялось на реальном объёме — это `#18`. До его закрытия считать, что бэкапы работают, нельзя: можно считать только, что они снимаются.

## 16.0.3 Засев данными эксплуатационного объёма

Выполняется один раз, после подъёма контура и до измерений `#20` и снятия дампа в `#18`. Пустая база делает обе процедуры бессмысленными: RTO окажется временем восстановления пустого дампа, а `p95` — временем запросов по таблицам, где план не имеет смысла.

```bash
set -a
. ./.env
set +a

export BASE_URL=https://$TESTAPP_PUBLIC_HOST KEYCLOAK_URL=https://$TESTAPP_PUBLIC_HOST/auth
export KEYCLOAK_REALM=testapp-staging
export SEED_STUDENT_PASSWORD="$(openssl rand -base64 24)"
scripts/seed-staging.sh
```

`seed-staging.sh` принимает имена из `.env` (`TESTAPP_KEYCLOAK_CLIENT_SECRET` и
`KEYCLOAK_ADMIN_USER`) напрямую и преобразует их в имена k6. Confidential client secret
проверяется до первого сетевого запроса; пустое значение завершает скрипт немедленно.

Профиль объёма, обоснование и порядок фиксации фактических цифр — `docs/PERFORMANCE.md`, раздел «Засев контура данными эксплуатационного объёма». Запускать **не с машины контура**, по той же причине, по которой с неё не запускают k6.

Пароль студентов — общий для всех создаваемых учётных записей и является секретом того же класса, что содержимое `.env`: он даёт доступ к сотням учётных записей. На контуре это допустимо, в production такой приём недопустим.

Скрипт отвергает повторный запуск с той же меткой `SEED_TAG`: удвоенный объём делает baseline невоспроизводимым, а расхождение обнаружилось бы уже после измерений.

## 16.0.4 Подъём на Windows 11 через Docker Desktop

Процедура целиком повторяет §16.0.1; здесь описано только то, чем Windows отличается. Отличий девять, и семь из них проявляются как отказ, не похожий на свою причину.

### Требования к машине

16 ГБ ОЗУ, 4 ядра, SSD/NVMe под данные. Windows и Docker Desktop сами занимают заметную часть памяти, поэтому 8 ГБ, достаточных для Linux-хоста, здесь мало.

Питание и сон: контур обязан работать круглосуточно. В параметрах электропитания отключается спящий режим и гибернация, иначе ночные бэкапы и drill'ы `#19` будут пропущены, а Prometheus получит разрывы в истории, по которым потом откалибруются пороги.

### 1. Docker Desktop и WSL2

Установить Docker Desktop с backend WSL2 (не Hyper-V). Проверить:

```powershell
docker version
wsl --status
```

Выделить ресурсы WSL2 — по умолчанию он берёт половину памяти и может отдать её обратно системе в неудачный момент. Файл `%UserProfile%\.wslconfig`:

```ini
[wsl2]
memory=12GB
processors=6
swap=4GB
```

После правки — `wsl --shutdown` и перезапуск Docker Desktop. Без этого Keycloak на JVM и Prometheus с 30-дневной retention будут конкурировать за память с самой Windows.

В настройках Docker Desktop включить **Start Docker Desktop when you log in**, иначе после перезагрузки контур не поднимется, а узнается об этом по отсутствию алертов — то есть никак.

### 2. Где лежит репозиторий

Клонировать **внутрь файловой системы WSL2**, а не в `C:\`:

```bash
wsl
cd ~
git clone <repo> TestApp && cd TestApp
```

Bind-mount из `/mnt/c` проходит через транслятор 9p: PostgreSQL на нём работает в разы медленнее, а измерения `#20` покажут файловую систему, а не приложение. У нас том базы — именованный volume, поэтому это касается смонтированных конфигов, но правило «репозиторий живёт в WSL» проще, чем помнить исключения.

Все команды дальше выполняются **изнутри WSL** (`wsl` в терминале), а не в PowerShell: `docker compose` там тот же самый, а `bash`, `openssl` и `printf` — настоящие.

### 3. Переводы строк

Скрипты исполняются внутри Linux-контейнеров, и CRLF ломает их с сообщением `$'\r': command not found`. В репозитории `.gitattributes` объявляет `*.sh text eol=lf`, поэтому clone внутрь WSL даёт правильные окончания. Если репозиторий когда-то клонировали на Windows со старым `.gitattributes`, проверить:

```bash
file deploy/staging/init-keycloak-db.sh   # не должно быть "CRLF line terminators"
```

Этот конкретный скрипт выполняет контейнер PostgreSQL при первом старте, и его отказ выглядит как «Keycloak не может подключиться к своей базе» — на два шага дальше настоящей причины.

### 4. Порты 80 и 443

Их нужно освободить, и мешать могут три вещи:

```powershell
netstat -ano | findstr ":80 "
netstat -ano | findstr ":443 "
Get-Service W3SVC -ErrorAction SilentlyContinue    # IIS
netsh interface ipv4 show excludedportrange protocol=tcp
```

Последняя команда — та, о которой обычно не думают. Hyper-V резервирует диапазоны портов под себя, и если 80 или 443 попал в исключённый диапазон, Docker откажется публиковать порт с сообщением «An attempt was made to access a socket in a way forbidden by its access permissions». Служба при этом не занята — порт просто изъят. Лечится либо перезахватом диапазона (`netsh int ipv4 add excludedportrange` после освобождения), либо отключением `winnat` с последующим перезапуском.

### 5. Имя и сертификат

Caddy получает сертификат Let's Encrypt по ACME HTTP-01, для чего нужны публичное DNS-имя, указывающее на эту машину, и входящие 80/443 снаружи. Это штатный путь, и он же самый простой.

Если пилот живёт только в локальной сети, вариантов два, и оба стоят дороже:

- **DNS-01** у публичного DNS-провайдера. Требует собственного образа Caddy со сборкой плагина провайдера — стоковый `caddy:2.10-alpine` его не содержит. Публичное имя нужно всё равно, но входящие порты снаружи — нет.
- **Самоподписанный `tls internal`** — **не подходит** без дополнительной работы, и это важно понимать заранее. API обращается к Keycloak по тому же `https://${TESTAPP_PUBLIC_HOST}/auth`, который записан в issuer токена, и обязан доверять сертификату. Внутренний CA Caddy в доверенных у контейнера API отсутствует, поэтому получение метаданных OIDC упадёт, и контур будет отвечать 401 на любой запрос при внешне исправном виде.

Отдельно: публичное имя обязано разрешаться в ingress **изнутри** сети compose. Это уже сделано сетевым алиасом в `compose.staging.yaml` — без него запрос ушёл бы в публичный DNS и вернулся на внешний адрес машины, а hairpin NAT в WSL2 не работает.

### 6. Секреты

`openssl` в Windows может отсутствовать; из WSL он есть, а если нет — годится контейнер:

```bash
docker run --rm alpine/openssl rand -base64 32
```

Пароли **генерировать**, не придумывать. Заполнить `.env` из `.env.staging.example`, `chmod 600 .env`. Ключ `age` для бэкапов создаётся здесь же, а хранится **вне этой машины**: копия и ключ, погибшие вместе, — это отсутствие бэкапа.

### 7. Первый запуск

```bash
docker compose -f compose.staging.yaml --env-file .env up -d
```

Порядок и проверки после каждого шага — §16.0.1. Три вещи, которые нужно сделать здесь и которые не нужны на локальном стенде:

**CIDR внутренней сети** — до любых измерений, иначе весь анонимный трафик схлопнется в одну партицию лимитера:

```bash
docker network inspect testapp-staging_default -f '{{(index .IPAM.Config 0).Subnet}}'
```

Полученное значение — в `TESTAPP_INTERNAL_CIDR`, затем `up -d` ещё раз. Имя проекта закреплено в `compose.staging.yaml` (`name: testapp-staging`), поэтому имя сети не зависит от каталога, куда склонирован репозиторий.

**Цель внешней пробы** — иначе она молчит, а молчание неотличимо от исправности:

```bash
printf '[{"targets":["https://%s/health/ready"]}]\n' "$TESTAPP_PUBLIC_HOST" \
  > deploy/staging/targets/ingress.json
```

**URL webhook'а Alertmanager** — в `deploy/staging/webhook-url`. Без файла Alertmanager не стартует, и это правильно: молчащий приёмник хуже явного отказа.

### 8. Проверка, что контур поднялся

```text
GET  https://<host>/health/live                    -> 200
GET  https://<host>/health/ready                   -> healthy
GET  https://<host>/api/v1/operations/version      -> версия и digest совпадают с развёрнутым
панель «API readiness (active probe)» в /grafana   -> READY, а не NO DATA
Prometheus /targets                                -> testapp, prometheus, testapp-readiness,
                                                      testapp-ingress — все up
```

`testapp-readiness` up, а `testapp-ingress` down означает, что приложение готово, а путь до него — нет: ingress, сертификат или проброс портов. Обратное сочетание невозможно и указывало бы на ошибку в конфигурации проб.

### 9. Бэкапы под Windows

Два отличия.

**`--network host` на Docker Desktop работает не так, как на Linux** — контейнер не получает сеть хоста. Скрипты резервного копирования это учитывают: сеть задаётся переменной.

```bash
export POSTGRES_DOCKER_NETWORK=testapp-staging_default
export POSTGRES_HOST=postgres POSTGRES_PORT=5432
bash scripts/postgresql-backup.sh /path/to/dump
```

Значение по умолчанию `host` рассчитано на CI и Linux; на Windows оно даст «connection refused», причём к моменту, когда бэкап понадобится, а не когда его настраивали.

**Расписание** — Планировщик заданий Windows, действие вызывает WSL:

```text
Программа: wsl.exe
Аргументы: -d Ubuntu -- bash -lc "cd ~/TestApp && POSTGRES_DOCKER_NETWORK=testapp-staging_default bash scripts/backup-offsite.sh"
```

Задание обязано выполняться при работе от батареи и будить машину, если она всё же засыпает. `age` в WSL ставится пакетным менеджером дистрибутива.

### 10. Что дальше

Засев объёма — §16.0.3, запускать **не с этой машины**: генератор нагрузки на том же хосте конкурирует за те же ядра, и `p95` измерит борьбу за планировщик. Для `#20` это критично, для засева — желательно.

### Топология с Keenetic и KeenDNS

Контур на `192.168.3.50`, наружу опубликован через KeenDNS. Это добавляет **вторую переприсадку** перед приложением, и с ней связаны три вещи, каждая из которых иначе обнаруживается поздно.

#### Адрес закрепить резервацией

`192.168.3.50` задать DHCP-резервацией на Keenetic по MAC, а не статикой в настройках Windows. Статика вне пула переживает всё, кроме смены пула; резервация переживает и её, а конфликт адресов на контуре выглядит как «периодически отваливается».

#### Какой режим KeenDNS — определить до подъёма

У KeenDNS два режима, и от режима зависит, кто терминирует TLS.

| Режим | Когда доступен | Кто терминирует TLS | Что это значит для контура |
|---|---|---|---|
| Прямой доступ | у провайдера белый IP | наш Caddy | штатный путь §16.0.1: проброс 80/443 на `192.168.3.50`, Let's Encrypt по HTTP-01 работает |
| Через облако | белого IP нет (CGNAT) | Keenetic | Caddy не может получить сертификат: входящий 80 до машины не доходит |

Режим виден в веб-интерфейсе роутера в разделе KeenDNS. **Проверить до заполнения `.env`:** при облачном режиме `TESTAPP_PUBLIC_HOST` с автоматическим HTTPS в Caddy не заработает, и обнаружится это на шаге первого запуска, после того как Let's Encrypt уже начнёт считать неудачные попытки.

При облачном режиме рабочих вариантов два, и оба меняют конфигурацию:

- терминировать TLS на роутере, а внутри контура ходить по HTTP: `KC_HOSTNAME` и `Keycloak__Authority` переводятся на `http://`, `Keycloak__RequireHttpsMetadata=false`. Контур перестаёт проверять собственный TLS-путь — это осознанная плата, и её следует записать в `#16`, потому что в production так быть не должно;
- поднять контур на имени, которым мы управляем сами, и получать сертификат по DNS-01. Требует собственного образа Caddy с плагином DNS-провайдера; `caddy:2.10-alpine` его не содержит.

Отдельно: доменом `keenetic.pro` управляет Keenetic, поэтому DNS-01 на нём недоступен — только на своём домене.

#### Сколько переприсадок перед приложением — считать, а не предполагать

`ForwardLimit` обязан равняться числу узлов, которые **терминируют соединение** перед приложением. Роутер попадает в этот счёт не всегда, и разница определяется тем, как опубликован контур.

| Публикация | Что делает роутер | Что видит Caddy | `TESTAPP_FORWARD_LIMIT` |
|---|---|---|---|
| Проброс портов при белом IP | NAT: подменяет адрес назначения, соединение не терминирует | исходный адрес клиента | `1` (умолчание) |
| Роутер как обратный прокси или KeenDNS через облако | терминирует соединение и открывает своё | адрес роутера или облачного узла | `2`, плюс `TESTAPP_UPSTREAM_PROXY_CIDR` |

При прямом доступе с белым IP роутер выполняет обычный DNAT: пакет доходит до Caddy с адресом клиента, `X-Forwarded-For` роутер не добавляет, и лишняя единица в `ForwardLimit` сделала бы хуже — приложение сняло бы запись, которой нет, и взяло бы за клиента адрес самого Caddy. Поэтому умолчания менять не нужно:

```dotenv
TESTAPP_FORWARD_LIMIT=1
TESTAPP_INTERNAL_CIDR=<CIDR сети compose из шага 7>
TESTAPP_UPSTREAM_PROXY_CIDR=127.0.0.1/32
```

Во втором случае — когда соединение действительно терминируется дважды — роутер объявляется **маской `/32`**, то есть ровно одним адресом, а не сетью `192.168.3.0/24`: доверие целой домашней сети означало бы, что любой её хост может подделать `X-Forwarded-For` и обойти лимит частоты.

#### Проверить, а не поверить: чей адрес попадает в аудит

Обе схемы проверяются одним запросом с **другой машины**, а не с самого контура:

```bash
curl -s -H "Authorization: Bearer $TOKEN" \
  "https://<host>/api/v1/operations/audit?page=1&pageSize=5"
```

| Что в аудите | Что это значит |
|---|---|
| разные клиентские адреса | цепочка настроена верно |
| у всех один адрес `192.168.3.1` | роутер терминирует соединение, а `ForwardLimit` равен 1 |
| у всех один адрес из подсети Docker | порт публикуется через реле Docker Desktop — см. ниже |
| у всех один внешний адрес | KeenDNS работает через облако и не передаёт `X-Forwarded-For` |

**Реле Docker Desktop — отдельный риск именно на Windows.** Опубликованный порт обслуживает не iptables, как на Linux, а процесс-посредник, и контейнер может видеть вместо адреса клиента внутренний адрес WSL2. Тогда `X-Forwarded-For` будет содержать его, и восстановить настоящий адрес нечем ни при каком `ForwardLimit`. Проверять эмпирически — таблицей выше, до измерений `#20`.

Если адрес клиента восстановить невозможно (реле Docker Desktop или облачный KeenDNS), per-client лимит на контуре непроверяем. Это не повод останавливать пилот, но обязано быть записано рядом с baseline `#20`, а не обнаружено при разборе его цифр: иначе окажется, что пороги `#19` откалиброваны по прогону, где все клиенты были одним клиентом.

#### Что внешняя проба на самом деле проверяет

Публичное имя внутри сети compose разрешается в ingress сетевым алиасом — иначе API не получил бы метаданные OIDC. Побочный эффект: проба `testapp-ingress` идёт в Caddy напрямую и **не проходит через роутер**. Она покрывает ingress, маршрутизацию и сертификат, но не покрывает KeenDNS, проброс портов и сам роутер.

Это ограничение, а не дефект: чтобы покрыть внешний путь целиком, пробер должен стоять снаружи периметра. Для пилота достаточно понимать границу; для production — вынести проверку доступности наружу.

#### Нестандартные порты: можно на хосте, нельзя снаружи

Порты бывают двух видов, и путать их дорого.

**На хосте** — сколько угодно. Если 80 или 443 на Windows заняты или изъяты диапазоном Hyper-V, они переназначаются переменными, а роутер пробрасывает наружные 80 и 443 на них:

```dotenv
TESTAPP_HTTP_PORT=9080
TESTAPP_HTTPS_PORT=9443
```

```text
WAN:80  -> 192.168.3.50:9080 -> контейнер:80
WAN:443 -> 192.168.3.50:9443 -> контейнер:443
```

Внутри контейнера Caddy всегда слушает 80 и 443, публичный адрес остаётся `https://<host>` без порта, и в конфигурации не меняется больше ничего.

**Снаружи** — нет, и это не наше ограничение. Проверка Let's Encrypt по HTTP-01 ходит строго в 80-й порт, по TLS-ALPN-01 — строго в 443-й; порт в них не настраивается, иначе валидация перестала бы что-либо доказывать. Публикация контура на `https://<host>:9443` означает, что автоматический сертификат получить нечем, и остаётся DNS-01 со своим доменом и собственным образом Caddy.

К этому добавляются три места, где публичный адрес перестаёт быть просто именем:

- сетевой алиас ingress в `compose.staging.yaml` — алиасом может быть только имя хоста, `host:port` там недопустим, а без алиаса API не получит метаданные OIDC;
- `KC_HOSTNAME`, `Keycloak__Authority` и `GF_SERVER_ROOT_URL` — порт обязан появиться в каждом, иначе issuer токена не сойдётся с тем, что проверяет API;
- цель внешней пробы в `deploy/staging/targets/ingress.json`.

Для пилота это лишняя работа без выигрыша: наружу оставить 80 и 443, а занятость портов на хосте решить переназначением.

#### Брандмауэр Windows

Docker Desktop публикует порты на хосте Windows, поэтому входящие 80 и 443 должны быть разрешены во входящих правилах брандмауэра для профиля «Частная сеть». Проброс на роутере без этого правила даёт таймаут, неотличимый от неверного проброса.

### Отличия одним списком

| Что | Linux | Windows 11 + Docker Desktop |
|---|---|---|
| Расположение репозитория | любое | внутри WSL2, не `/mnt/c` |
| Окончания строк в `*.sh` | LF | LF принудительно через `.gitattributes` |
| Память | параметры хоста | `.wslconfig`, иначе WSL2 заберёт половину |
| Порты 80/443 | занятость службами | плюс изъятые диапазоны Hyper-V |
| `--network host` | работает | не даёт сеть хоста, нужна `POSTGRES_DOCKER_NETWORK` |
| Автозапуск | systemd | Docker Desktop «Start when you log in» |
| Расписание бэкапов | cron | Планировщик заданий через `wsl.exe` |
| Сон машины | обычно отключён | отключать явно |

## 16.1 Сбор доказательств CI для релиза

Гейт 1.0 (`docs/ROADMAP.md` §7, пункт 7) требует зелёных пайплайнов на релизном коммите, а критерий готовности RC (issue #23) — записанных run ID. Триггеры устроены так, что «прогон не запускался» и «прогон прошёл» выглядят в интерфейсе одинаково, поэтому собирать нужно именно ID, а не отсутствие красного.

| Workflow | На push в `beta-ddd`/`master` | На тег `v*` | Ручной запуск |
|---|---|---|---|
| `dotnet` | всегда | да | `workflow_dispatch` |
| `security` | всегда | да | `workflow_dispatch` |
| `observability` | по paths-фильтру | **нет** | `workflow_dispatch` |
| `performance` | по paths-фильтру | **нет** | `workflow_dispatch` |
| `postman` | по paths-фильтру | **нет** | `workflow_dispatch` |

Три нижних workflow ограничены `paths`, а релизный коммит меняет только `CHANGELOG.md` — под фильтр он не попадает. Добавлять им триггер на тег бессмысленно: `paths` применяется и к push тега, и результат непредсказуем. Поэтому для них предусмотрен ручной запуск.

Порядок сбора доказательств:

1. Запушить релизный коммит в `beta-ddd` и дождаться `dotnet`/`security` на его точном SHA.
2. Запустить `observability`, `performance` и `postman` вручную (`Actions` → workflow →
   `Run workflow`) на той же ветке. Дождаться завершения всех трёх.
3. Проверить, что последний завершённый прогон каждого из пяти workflow на этом SHA имеет
   заключение `success`.
4. Только после этого поставить тег `v1.0.0-rc.N`. Workflow `release` повторит эту проверку
   через GitHub API **до** входа в GHCR, сборки образа и создания Release.

Если хотя бы один workflow отсутствует, не завершён или завершён неуспешно, выпуск
останавливается. После исправления или завершения прогонов workflow `release` запускается
повторно с ветки и существующим тегом. `evidence_only` нужен только для обновления таблицы у
уже опубликованного Release: образ в этом режиме не пересобирается, его digest читается из
реестра, а gate всё равно проверяется заново.

Запускать такой прогон следует **с ветки, а не с тега**: `workflow_dispatch` берёт определение workflow из того ref, с которого запущен, поэтому тег со старой версией `release.yml` про `evidence_only` не знает. Ветка задаёт, *чем* выпускать; поле `tag` — *что* выпускать. Код и коммит для доказательств берутся из тега, а не из ветки.

Проверка перед объявлением RC автоматизирована: таблица содержит ровно по одному последнему
завершённому успешному прогону на workflow. Неуспешный или отсутствующий прогон не может
попасть в опубликованный Release, потому что останавливает job раньше сборки.

## 16.2 Выпуск релиза

### Единый источник версии

`Directory.Build.props` в корне репозитория задаёт `VersionPrefix`. Суффикс предрелиза передаёт релизный пайплайн:

```bash
dotnet publish -p:VersionSuffix=rc.1     # -> 1.0.0-rc.1
```

Dockerfile принимает это как `--build-arg VERSION_SUFFIX`. До появления `Directory.Build.props` версии не было нигде: сборки получали неявный `1.0.0`, поэтому по развёрнутому образу нельзя было сказать, что именно в нём.

Что развёрнуто, спрашивается у самого процесса:

```http
GET /api/v1/operations/version
Authorization: operations:read
```

```json
{ "version": "1.0.0-rc.1", "imageDigest": "sha256:…" }
```

`version` читается из атрибута сборки, а не из константы — константу легко забыть поднять, и тогда контур будет уверенно называть неверную версию. `imageDigest` приходит из переменной окружения `TESTAPP_IMAGE_DIGEST`, которую обязано проставить развёртывание; без неё поле равно `null`.

### Политика тегов

```text
v1.0.0-rc.N   режется из beta-ddd
v1.0.0        режется после merge в master
```

Тег обязан согласовываться с `VersionPrefix`: релизный workflow сверяет их и падает при расхождении. Поднимать `VersionPrefix` следует в том же change set, что и тег.

После первого RC ветка `beta-ddd` заморожена для feature-изменений и принимает только fix-коммиты, каждый с regression-тестом.

### Порядок выпуска

1. Закрыть quality gate по §16.1 — пять успешных run ID на релизном коммите.
2. Поставить тег на этот коммит. `release` повторно проверяет gate, собирает образ и сканирует
   **эту же сборку** до публикации в GHCR.
3. После успешного сканирования создаются SBOM, архив образа и GitHub Release с автоматически
   собранной таблицей доказательств.
4. Разворачивать **по digest, а не по тегу**: тег перемещаем, digest — нет. Digest указан в
   теле Release и обязан быть проброшен в `TESTAPP_IMAGE_DIGEST`.

### Что приложено к Release

| Файл | Что это |
|---|---|
| `testapp-api-<версия>.image.tar.gz` | сам образ, загружаемый `docker load` без доступа к реестру |
| `testapp-api.cdx.json` | SBOM образа (CycloneDX) |
| `openapi-v1.json` | замороженный контракт v1 на момент выпуска |

Образ приложен по той же причине, что и SBOM: GHCR может стать недоступен, пакет — приватным, а машина, на которую разворачивают, — отрезанной от интернета. Release остаётся самодостаточным.

### Офлайн-установка из архива

```bash
sha256sum -c <<< "<SHA-256 из тела Release>  testapp-api-<версия>.image.tar.gz"
docker load -i testapp-api-<версия>.image.tar.gz
```

Штатный путь этим не отменяется: канонической ссылкой на сборку остаётся digest в GHCR. У архива своя особенность — после `docker load` образ доступен под тегом, но **без** digest: RepoDigest присваивает реестр, а не архив, поэтому `TESTAPP_IMAGE` на такой машине задаётся тегом. Роль доказательства подлинности берёт на себя SHA-256 архива, записанный в теле Release и проверенный до загрузки. `TESTAPP_IMAGE_DIGEST` при этом всё равно выставляется в digest из Release: он не участвует в получении образа, а отвечает на вопрос «что запущено» через `GET /api/v1/operations/version`.

Контрольная сумма считается от файла, а не от манифеста, сознательно: `docker save`/`docker load` сохраняет содержимое образа, но представление метаданных зависит от того, какое хранилище образов использует демон (классическое или containerd), и сравнение digest'ов между разными машинами перестаёт быть надёжным. Сумма файла не зависит ни от чего.

Workflow `release` не дублирует тесты, но является fail-closed оркестратором гейта: требует
пять успешных workflow на том же коммите и отдельно сканирует точный публикуемый образ.

## 17. Модель масштабирования

API можно горизонтально масштабировать при общей PostgreSQL/RabbitMQ/Keycloak инфраструктуре.

Cross-instance safety уже предусмотрена для:

- оптимистичный параллельный доступ к агрегатам;
- небезопасные идемпотентные операции;
- обработка сообщений Outbox.

Background attempt expiration может выполняться на нескольких replicas: optimistic concurrency делает duplicate attempt completion безопасным, хотя при большом scale можно позже выделить dedicated worker role.

## 18. Когда выделять worker process

Отдельный worker deployment целесообразен, если:

- Outbox/expiration создают заметную нагрузку на API replicas;
- требуется независимый scaling;
- нужны разные resource limits;
- operational isolation становится важнее простоты монолита.

До этого hosted services внутри modular monolith достаточны.

## 19. Пробелы готовности к production

До production 1.0 закрыть:

- staging backup/restore drill с measured RTO;
- deployed secret manager/injection и encrypted backup schedule/PITR policy;
- реальные dashboard/alert routes и alert drill;
- задокументированная репетиция релиза с откатом и исправлением вперёд.

Trusted proxies, CORS/TLS/HSTS configuration contract, configurable rate limiting, repository retention, dead-letter API, security/image scan, SBOM и immutable SHA/digest для GitHub Actions и внешних контейнеров уже реализованы и не должны оставаться в списке отсутствующих возможностей. Плавающие executable references запрещает `scripts/verify_immutable_references.py` в workflow `dotnet`.
