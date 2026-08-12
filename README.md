# TestApp

Система создания, публикации, назначения и прохождения тестов. Проект развивается как модульный монолит на .NET 10 с DDD, Clean Architecture и CQRS.

## Архитектура

Направление зависимостей:

```text
TestApp.Api -> TestApp.Application -> TestApp.Domain -> TestApp.Core
     |                 |
     +-------> TestApp.Infrastructure

TestApp.Application -> TestApp.Messaging
TestApp.Infrastructure -> TestApp.Application + TestApp.Domain
```

`Domain` не зависит от ASP.NET Core, EF Core, JWT или Keycloak SDK. `Infrastructure` реализует persistence, identity adapters, MariaDB advisory locking, health checks, OpenTelemetry и transactional Outbox. `Api` содержит HTTP contracts, authentication/authorization policies и mapping ошибок в ProblemDetails.

## Основная модель

- `Test` — изменяемое рабочее определение теста.
- `PublishedTestRevision` — неизменяемый опубликованный snapshot.
- Изменение опубликованного `Test` переводит definition обратно в `Draft`; ранее опубликованные revisions не меняются.
- `TestAssignment` назначает конкретную revision пользователю или группе.
- `TestAttempt` всегда принадлежит одному пользователю, одному assignment и одной immutable revision.
- Варианты правильных ответов хранятся только на серверной стороне в published revision; student API не раскрывает correctness.
- Reviewer API строит детальный результат по immutable revision, поэтому дальнейшее редактирование draft не меняет историю уже завершённой попытки.

## Keycloak

Keycloak является source of truth для пользователей, групп и системных ролей. TestApp не создаёт локальных пользователей и не генерирует внешние identity IDs.

Ожидаемый claims contract access token:

- `sub` — стабильный идентификатор пользователя;
- `groups` — один или несколько внешних идентификаторов групп;
- `roles` — роли Keycloak, используемые ASP.NET Core authorization policies.

JWT handler работает с `MapInboundClaims=false`, использует `sub` как name claim и `roles` как role claim. `HttpCurrentActor` отдельно преобразует identity claims в application actor.

Текущие policy names:

- `tests:write` — `test-author` или `test-admin`;
- `tests:publish` — `test-author` или `test-admin`;
- `tests:assign` — `test-admin`;
- `results:review` — `test-author` или `test-admin`;
- `operations:read` — `test-admin`.

Бизнес-правила eligibility (назначение пользователю/группе, окно доступности, лимит попыток) проверяются отдельно от endpoint authorization.

## CQRS / API read side

Write side использует aggregates и command handlers. Read side использует отдельные DTO/projections с EF Core `AsNoTracking()`.

Author/reviewer endpoints включают:

```text
GET /api/tests
GET /api/tests/{testId}/revisions
GET /api/tests/{testId}/editor
GET /api/results
GET /api/results/{attemptId}
```

Администрирование назначений доступно только `test-admin`:

```text
GET  /api/assignments
GET  /api/assignments/{assignmentId}
GET  /api/assignments/{assignmentId}/attempts
POST /api/assignments/bulk
```

Admin list поддерживает фильтры по test/revision/target/status и pagination. Detail содержит агрегированную статистику попыток, pass/fail и score percentages.

Bulk assignment валидирует весь batch до записи, ограничен 500 targets, запрещает дубли целей и использует один distributed idempotency lease. Все assignments и idempotency result сохраняются одной транзакцией.

## Persistence

Основная СУБД — MariaDB. Infrastructure использует EF Core 9 + Pomelo provider, приложение остаётся на `net10.0`.

Connection string читается из `ConnectionStrings:Database`. Development fallback:

```text
Server=localhost;Port=3306;Database=testapp;User=testapp;Password=testapp;
```

Migrations находятся в `src/TestApp.Infrastructure/Persistence/Migrations`.

Для development `Database:ApplyMigrationsOnStartup` по умолчанию включён. Для production автоматическое применение migrations по умолчанию выключено: каждый API replica не должен самостоятельно конкурировать за schema migration.

Отдельный migration-only режим:

```bash
dotnet TestApp.Api.dll --migrate
```

Он применяет EF migrations и завершает процесс до запуска HTTP listener. В deployment этот режим следует запускать отдельным release/init job перед API replicas.

Mutable attempt responses хранятся нормализованно (`TestAttempt -> QuestionResponse -> SelectedAnswerOption`), а immutable published revision хранит snapshot вопросов в JSON.

Лимит попыток защищён от гонки через `Serializable` transaction. Идемпотентные publish/assign/submit/bulk-assign operations дополнительно сериализуются между несколькими API-инстансами через MariaDB advisory locks (`GET_LOCK`/`RELEASE_LOCK`) с повторной проверкой idempotency result после получения lease.

## Events / Outbox

`IDomainEvent` — внутреннее событие домена. Только события, явно реализующие `IIntegrationEvent`, могут попадать в transactional Outbox.

Outbox delivery использует at-least-once semantics. `EventId` передаётся publisher как стабильный deduplication key для downstream consumer.

`AddInfrastructure()` сам по себе **не запускает доставку Outbox**. Если реальный transport publisher не настроен, сообщения остаются durable pending rows в MariaDB и не помечаются доставленными через no-op implementation.

Доставка включается явно:

```csharp
services.AddOutboxDelivery<MyOutboxPublisher>(options =>
{
    options.MaxAttempts = 10;
    options.PollInterval = TimeSpan.FromSeconds(5);
});
```

Processor использует exponential retry/backoff, dead-letter state и MariaDB advisory lock по `OutboxMessage.Id`. После crash между внешней публикацией и записью `ProcessedAt` повторная доставка возможна — это штатная at-least-once семантика, поэтому consumer обязан дедуплицировать события по `EventId`.

Operational status доступен `test-admin`:

```text
GET /api/operations/outbox
```

Ответ содержит pending/retry/dead-letter counts, возраст старейшего pending message и последние dead letters без раскрытия payload.

## Health

- `GET /health/live` — процесс жив, проверка не зависит от MariaDB;
- `GET /health/ready` — readiness с реальным подключением к MariaDB, возвращает `503`, если база недоступна.

## OpenTelemetry

Infrastructure регистрирует OpenTelemetry traces и metrics для:

- ASP.NET Core requests;
- outgoing `HttpClient` calls;
- .NET runtime metrics.

Health probes исключены из request traces. Без `OTEL_EXPORTER_OTLP_ENDPOINT` exporter не включается и приложение не зависит от collector.

Для OTLP используются стандартные environment variables, например:

```text
OTEL_SERVICE_NAME=TestApp.Api
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
OTEL_EXPORTER_OTLP_PROTOCOL=grpc
```

Local Compose включает OpenTelemetry Collector с debug exporter, поэтому traces/metrics можно сразу видеть в логах collector.

## Docker Compose для локальной разработки

Полный локальный stack:

```bash
docker compose up --build
```

Сервисы:

- TestApp API — `http://localhost:8080`;
- Keycloak — `http://localhost:8081`;
- MariaDB — `localhost:3306`;
- OTLP gRPC — `localhost:4317`;
- OTLP HTTP — `localhost:4318`.

Compose сначала ждёт MariaDB, запускает отдельный `migrate` container и только после его успешного завершения стартует API.

Keycloak realm импортируется из `deploy/keycloak/testapp-realm.json`. Конфигурация предназначена **только для локальной разработки** и содержит тестовые учётные записи:

```text
admin   / admin   -> test-admin
author  / author  -> test-author
student / student -> group students
```

Bootstrap admin для локального Keycloak: `bootstrap-admin / bootstrap-admin`.

Realm mapper выдаёт `roles`, `groups` и audience `testapp-api`. Frontend hostname — `localhost:8081`, а dynamic backchannel позволяет API обращаться к Keycloak через приватное имя `keycloak:8080`.

Удалить локальные volumes и начать с чистой базы/realm:

```bash
docker compose down -v
```

## Проверка

```bash
dotnet restore TestApp.slnx
dotnet build TestApp.slnx --no-restore --configuration Release
dotnet test TestApp.slnx --no-build --configuration Release
docker compose -f compose.yaml config --quiet
docker build -t testapp-api:local .
```

GitHub Actions поднимает настоящий `mariadb:11.4` service container, выполняет restore/build/test, валидирует Compose, собирает production Docker image и запускает этот image в `--migrate` режиме против MariaDB.

Integration tests используют отдельные временные databases и проверяют migrations, optimistic concurrency, distributed idempotency/advisory locks, bulk assignments, persistence round-trip, Outbox delivery/dead-letter behavior и защищённый Minimal API end-to-end flow.
