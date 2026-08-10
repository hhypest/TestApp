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

`Domain` не зависит от ASP.NET Core, EF Core, JWT или Keycloak SDK. `Infrastructure` реализует persistence, identity adapters и Outbox. `Api` содержит HTTP contracts, authentication/authorization policies и mapping ошибок в ProblemDetails.

## Основная модель

- `Test` — изменяемое рабочее определение теста.
- `PublishedTestRevision` — неизменяемый опубликованный snapshot.
- Изменение опубликованного `Test` переводит definition обратно в `Draft`; ранее опубликованные revisions не меняются.
- `TestAssignment` назначает конкретную revision пользователю или группе.
- `TestAttempt` всегда принадлежит одному пользователю, одному assignment и одной immutable revision.
- Варианты правильных ответов хранятся только на серверной стороне в published revision; клиентские ответы проверяются против этой revision перед сохранением.

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
- `results:review` — `test-author` или `test-admin`.

Бизнес-правила eligibility (назначение пользователю/группе, окно доступности, лимит попыток) проверяются отдельно от endpoint authorization.

## CQRS

Write side использует aggregates и command handlers. Read side использует отдельные DTO/projections с EF Core `AsNoTracking()`:

- editor view теста;
- назначения текущего пользователя;
- состояние попытки;
- результат попытки.

Отдельная read database пока намеренно не используется.

## Persistence

EF Core + SQLite используется для текущего development profile. Initial migration находится в `TestApp.Infrastructure/Persistence/Migrations` и применяется при старте API через `Database.MigrateAsync()`.

Mutable attempt responses хранятся нормализованно (`TestAttempt -> QuestionResponse -> SelectedAnswerOption`), а immutable published revision хранит snapshot вопросов в JSON.

Лимит попыток защищён от гонки через атомарный `TryAddWithinLimitAsync` в `Serializable` transaction.

## Events / Outbox

`IDomainEvent` — внутреннее событие домена. Только события, явно реализующие `IIntegrationEvent`, могут попадать в transactional Outbox. Обычные domain events больше не считаются автоматически внешними сообщениями.

## Проверка

```bash
dotnet restore TestApp.slnx
dotnet build TestApp.slnx --no-restore --configuration Release
dotnet test TestApp.slnx --no-build --configuration Release
```

CI выполняет эти команды для `beta-ddd`, архитектурной ветки и stacked `beta-ddd-*` веток.

Тесты включают domain/application unit tests, dependency-rule checks, Keycloak claims mapping и запуск защищённого Minimal API host.
