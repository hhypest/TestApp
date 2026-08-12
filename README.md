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

`Domain` не зависит от ASP.NET Core, EF Core, JWT или Keycloak SDK. `Infrastructure` реализует persistence, identity adapters, MariaDB advisory locking, health checks и transactional Outbox. `Api` содержит HTTP contracts, authentication/authorization policies и mapping ошибок в ProblemDetails.

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
- `groups` — один или несколько внешних идентификаторов групп; mapper принимает повторяющиеся claims и JSON-массив;
- роли — `ClaimTypes.Role` и/или `roles` (также поддерживается JSON-массив).

HTTP-слой преобразует Keycloak roles в application permissions. Текущие policy names:

- `tests:write` — `test-author` или `test-admin`;
- `tests:publish` — `test-author` или `test-admin`;
- `tests:assign` — `test-admin`;
- `results:review` — `test-author` или `test-admin`;
- `operations:read` — `test-admin`.

Бизнес-правила eligibility (назначение пользователю/группе, окно доступности, лимит попыток) проверяются отдельно от endpoint authorization.

## CQRS

Write side использует aggregates и command handlers. Read side использует отдельные DTO/projections с EF Core `AsNoTracking()`:

- editor view теста;
- назначения текущего пользователя;
- попытки текущего пользователя;
- состояние и итог собственной попытки;
- reviewer results list;
- детальный reviewer result с question/option breakdown.

Отдельная read database пока намеренно не используется.

## Persistence

Основная СУБД — MariaDB. Infrastructure использует EF Core 9 + Pomelo provider, приложение остаётся на `net10.0`.

Connection string читается из `ConnectionStrings:Database`. Development fallback:

```text
Server=localhost;Port=3306;Database=testapp;User=testapp;Password=testapp;
```

MariaDB migrations находятся в `src/TestApp.Infrastructure/Persistence/Migrations` и применяются при старте API через `Database.MigrateAsync()`.

Mutable attempt responses хранятся нормализованно (`TestAttempt -> QuestionResponse -> SelectedAnswerOption`), а immutable published revision хранит snapshot вопросов в JSON.

Лимит попыток защищён от гонки через `Serializable` transaction. Идемпотентные publish/assign/submit operations дополнительно сериализуются между несколькими API-инстансами через MariaDB advisory locks (`GET_LOCK`/`RELEASE_LOCK`) с повторной проверкой idempotency result после получения lease.

## Events / Outbox

`IDomainEvent` — внутреннее событие домена. Только события, явно реализующие `IIntegrationEvent`, могут попадать в transactional Outbox.

Outbox delivery использует at-least-once semantics. `EventId` передаётся publisher как стабильный deduplication key для downstream consumer.

Важно: `AddInfrastructure()` сам по себе **не запускает доставку Outbox**. Это сделано намеренно: если реальный transport publisher не настроен, сообщения остаются durable pending rows в MariaDB и не помечаются доставленными через no-op implementation.

Доставка включается явно:

```csharp
services.AddOutboxDelivery<MyOutboxPublisher>(options =>
{
    options.MaxAttempts = 10;
    options.PollInterval = TimeSpan.FromSeconds(5);
});
```

Processor использует exponential retry/backoff, dead-letter state и MariaDB advisory lock по `OutboxMessage.Id`, чтобы несколько API-инстансов не публиковали одно сообщение одновременно. После crash между внешней публикацией и записью `ProcessedAt` повторная доставка возможна — это нормальная семантика at-least-once, поэтому consumer обязан дедуплицировать события по `EventId`.

Operational status доступен `test-admin`:

```text
GET /api/operations/outbox
```

Ответ содержит pending/retry/dead-letter counts, возраст старейшего pending message и последние dead letters без раскрытия payload.

## Health

- `GET /health/live` — процесс жив, проверка не зависит от MariaDB;
- `GET /health/ready` — readiness с реальным подключением к MariaDB, возвращает `503`, если база недоступна.

## Проверка

```bash
dotnet restore TestApp.slnx
dotnet build TestApp.slnx --no-restore --configuration Release
dotnet test TestApp.slnx --no-build --configuration Release
```

GitHub Actions поднимает настоящий `mariadb:11.4` service container и выполняет restore/build/test против MariaDB. Integration tests используют отдельные временные databases и проверяют migrations, optimistic concurrency, idempotency/advisory locks, persistence round-trip, Outbox delivery/dead-letter behavior и защищённый Minimal API end-to-end flow.
