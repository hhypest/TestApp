# Эксплуатация и deployment TestApp

> Статус: local/container runtime **Implemented**; production platform runbook частично **Planned**.

## 1. Runtime topology

Стандартный local Compose stack:

```mermaid
graph TD
    Client --> API[TestApp API :8080]
    API --> DB[MariaDB 12.3 :3306]
    API --> KC[Keycloak 26.7 :8080 internal / :8081 host]
    API --> RMQ[RabbitMQ 4.3.1 :5672]
    API --> OTEL[OTEL Collector :4317]
    MIG[Migrate job] --> DB
```

Compose services:

- `mariadb`;
- `rabbitmq`;
- `keycloak`;
- `otel-collector`;
- `migrate`;
- `api`.

## 2. Local quick start

Перед первым запуском:

- Docker Engine/compatible runtime;
- Docker Compose v2.

Запуск:

```bash
docker compose up --build
```

После смены major database baseline либо при необходимости чистого окружения:

```bash
docker compose down -v
docker compose up --build
```

### Host ports

| Service | Host |
|---|---|
| API | `http://localhost:8080` |
| Keycloak | `http://localhost:8081` |
| MariaDB | `localhost:3306` |
| RabbitMQ AMQP | `localhost:5672` |
| RabbitMQ management | `http://localhost:15672` |
| OTLP gRPC | `localhost:4317` |
| OTLP HTTP | `localhost:4318` |

## 3. Development credentials

**Только local development. Не использовать в production.**

MariaDB:

```text
root/root
testapp/testapp
```

RabbitMQ:

```text
testapp/testapp
```

Keycloak bootstrap:

```text
bootstrap-admin / bootstrap-admin
```

Dev realm users:

```text
admin   / admin   -> test-admin
author  / author  -> test-author
student / student -> group students
```

Keycloak realm import:

```text
deploy/keycloak/testapp-realm.json
```

## 4. Docker image

`Dockerfile` — multi-stage .NET build/runtime image.

Operational expectations:

- runtime process слушает configured ASP.NET port;
- container работает от non-root application user;
- тот же image используется и для API, и для migration-only mode;
- schema migration не требует отдельного tooling image.

## 5. Migration strategy

### 5.1 Migration-only command

```bash
dotnet TestApp.Api.dll --migrate
```

В container:

```bash
docker run ... testapp-api:<tag> --migrate
```

### 5.2 Local Compose order

```text
MariaDB healthy
    ↓
migrate container --migrate
    ↓
migrate exits 0
    ↓
API starts
```

### 5.3 Production order

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

## 6. Configuration catalog

### Database

```text
ConnectionStrings:Database
Database:ApplyMigrationsOnStartup
```

Current note: `Program.cs` пока имеет development-style fallback connection string. Production hardening должен сделать connection string обязательным вне Development.

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

### Attempt expiration

Infrastructure defaults:

```text
BatchSize = 100
PollInterval = 30 seconds
```

Binding этих options из production config следует формализовать, если потребуется tuning.

### Outbox delivery

Default internal options:

```text
BatchSize = 100
MaxAttempts = 10
PollInterval = 5 sec
BaseRetryDelay = 5 sec
MaxRetryDelay = 15 min
AdvisoryLockTimeoutSeconds = 5
```

Production configuration binding для этих параметров — planned hardening.

## 7. Health endpoints

### Liveness

```text
GET /health/live
```

Назначение: процесс способен отвечать. Не должен зависеть от MariaDB/RabbitMQ, иначе transient dependency failure может вызвать restart loop.

### Readiness

```text
GET /health/ready
```

Проверяет:

- MariaDB connectivity;
- RabbitMQ connection/channel/exchange access, **только если RabbitMQ delivery включён**.

При critical dependency failure readiness должна вернуть unhealthy/503 и исключить instance из traffic.

## 8. OpenTelemetry

Текущая instrumentation:

- ASP.NET Core requests;
- outgoing HttpClient;
- .NET runtime metrics.

Health endpoints исключаются из request traces, чтобы probes не создавали telemetry noise.

Local collector config:

```text
deploy/otel-collector-config.yaml
```

В local environment collector использует debug exporter.

### Production planned

Нужно определить backend:

- Grafana Tempo/Prometheus;
- Jaeger;
- Azure Monitor;
- Datadog;
- другой OTLP-compatible backend.

Выбор backend не должен менять Domain/Application.

## 9. Structured request telemetry

`RequestTelemetryMiddleware` логирует:

- HTTP method;
- path;
- status code;
- duration ms;
- trace/correlation identifier.

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

## 11. Audit operations

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

### Planned retention

Перед production 1.0 определить:

- retention period;
- archival/export;
- who may query audit;
- whether compliance requires immutable/WORM external sink;
- cleanup job/index impact.

## 12. Outbox operations

Endpoint:

```text
GET /api/v1/operations/outbox
role: test-admin
```

Использовать для проверки:

- growing pending backlog;
- retry growth;
- dead-letter count;
- oldest pending age;
- repeated error reason.

### Runbook: Outbox backlog растёт

1. Проверить `/health/ready`.
2. Проверить `RabbitMq:Enabled` и broker DNS/network/auth.
3. Проверить RabbitMQ exchange/permissions.
4. Проверить application logs по OutboxMessageId.
5. Проверить dead-letter state.
6. Не удалять rows вручную до выяснения причины.
7. После восстановления transport processor автоматически продолжит due retries.

### Runbook: dead-letter message

1. Зафиксировать message ID/type/error/attempt count.
2. Проверить, временная это ошибка или permanent schema/routing issue.
3. Исправить consumer/broker/config/application.
4. Сейчас отдельный safe admin requeue API **не реализован**; ручное изменение DB должно выполняться только как controlled operation.
5. Planned feature: explicit requeue/acknowledge dead-letter command с audit.

## 13. Attempt expiration operations

`OverdueAttemptProcessor` работает внутри API process.

### Если expired attempts остаются InProgress

Проверить:

- application instance жив;
- worker startup logs;
- DB connectivity;
- deadline timestamps;
- concurrency conflicts;
- error logs по AttemptId.

Worker batch-based и eventual: изменение не обязано произойти ровно в момент deadline; default scan cadence около 30 секунд.

## 14. MariaDB 12.3 operational policy

CI гарантирует minimum runtime version 12.3.

### Upgrade rule

Перед изменением MariaDB line:

1. backup;
2. проверить vendor upgrade path;
3. поднять isolated copy;
4. прогнать migrations;
5. прогнать полный integration suite;
6. проверить advisory locks;
7. проверить charset/collation/indexes;
8. выполнить production-image `--migrate`;
9. только после этого менять production.

Нельзя считать смену Docker tag достаточным production upgrade process.

## 15. Backup/restore/DR — P0/P1 до production

Должны быть формально определены:

- **RPO** — допустимая потеря данных;
- **RTO** — время восстановления;
- backup frequency;
- full/incremental/binlog strategy;
- encrypted storage;
- retention;
- offsite copy;
- restore drill;
- credential restore/rotation;
- Keycloak realm/config backup;
- RabbitMQ topology recreation (messages не должны быть единственным source of truth; Outbox позволяет повторную доставку).

## 16. Deployment smoke checklist

После rollout:

```text
/health/live -> 200
/health/ready -> healthy
/openapi/v1.json -> согласно production policy
JWT auth -> работает
GET /api/v1/me/assignments -> expected auth behavior
MariaDB version -> expected
migration history -> no pending migrations
Outbox status -> no unexpected backlog
OTEL -> traces/metrics arrive
```

Для migration release дополнительно выполнить representative create/publish/assign/start/submit flow в staging.

## 17. Scaling model

API можно горизонтально масштабировать при общей MariaDB/RabbitMQ/Keycloak инфраструктуре.

Cross-instance safety уже предусмотрена для:

- optimistic aggregate concurrency;
- unsafe idempotent operations;
- Outbox message processing.

Background attempt expiration может выполняться на нескольких replicas: optimistic concurrency делает duplicate attempt completion безопасным, хотя при большом scale можно позже выделить dedicated worker role.

## 18. Когда выделять worker process

Отдельный worker deployment целесообразен, если:

- Outbox/expiration создают заметную нагрузку на API replicas;
- требуется независимый scaling;
- нужны разные resource limits;
- operational isolation становится важнее простоты монолита.

До этого hosted services внутри modular monolith достаточны.

## 19. Production readiness gaps

До production 1.0 закрыть:

- fail-fast configuration;
- secret manager/injection;
- trusted proxies/forwarded headers;
- TLS/HSTS/CORS policy;
- configurable rate limiting;
- backup/restore drill;
- audit/outbox retention;
- alerting/SLO;
- dependency/security scan;
- capacity/load test;
- dead-letter requeue procedure/API;
- documented rollback/forward-fix release process.