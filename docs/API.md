# HTTP API приложения TestApp

> Статус: **Implemented API contract**. Canonical base path — `/api/v1`.

## 1. Versioning и compatibility

Канонические эндпоинты:

```text
/api/v1/...
```

Legacy compatibility middleware временно переписывает:

```text
/api/... -> /api/v1/...
```

Legacy compatibility является управляемым lifecycle layer:

- включение/отключение через конфигурацию;
- RFC-style `Deprecation` header;
- optional `Sunset`;
- после configured retirement legacy path возвращает `410 api.version.retired`.

Конкретная дата отключения rewrite ещё не назначена и должна учитывать telemetry/client migration.

### Политика совместимости v1

Форма контракта зафиксирована снимком `docs/openapi/v1.json` — это артефакт версии, а не кэш. Его расхождение с живым документом роняет `OpenApiSnapshotTests`, поэтому изменение контракта не может пройти незамеченным (issue #22, гейт `docs/ROADMAP.md` §7 пункт 1).

**Считается ломающим и требует v2:**

- удаление эндпоинта, поля ответа, кода ответа или значения enum;
- переименование чего-либо из перечисленного;
- сужение типа или формата поля, ужесточение валидации существующего поля;
- превращение необязательного поля запроса в обязательное;
- изменение семантики кода ответа для того же условия;
- изменение формы идентификатора или ошибки (`ProblemDetails`, `code`).

**Допустимо внутри v1:**

- новый эндпоинт;
- новое **необязательное** поле запроса;
- новое поле ответа — клиент обязан игнорировать незнакомые поля;
- новое значение enum там, где документ это допускает, при неизменной семантике существующих;
- новый код ответа для условия, которое раньше не возникало;
- правки описаний, примеров и summary.

**`operationId` — часть контракта.** Генераторы SDK превращают его в имя метода клиента, поэтому переименование `operationId` ломает уже сгенерированных клиентов так же, как переименование поля. Каждый эндпоинт обязан иметь стабильный `operationId`, объявленный в `ApiOperationContracts`; авто-генерируемое имя вида `GET_api_v1_...` считается недоработкой, а не значением по умолчанию.

**Форма успешного ответа объявляется явно.** Эндпоинты возвращают нетипизированный `IResult`, поэтому вывести тип тела из делегата нечем — он указывается в `ApiOperationContracts` рядом с `operationId`. Операция с ответом `200` без схемы тела роняет `OpenApiResponseSchemaTests`.

**Порядок изменения.** Любая правка, затрагивающая документ, обновляет снимок в том же change set:

```bash
UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --filter OpenApiSnapshotTests
```

Diff снимка в code review — это и есть предъявление изменения контракта. Обновление снимка без описания изменения в `CHANGELOG.md` считается ошибкой ревью, а не деталью реализации.

**Статус заморозки: окончательная** (ADR-035). С этого момента ломающее изменение из списка выше требует v2, а не согласия ревьюера.

Заморозка держится на двух проверках, у которых разные задачи:

| Проверка | Что делает | Что происходит при изменении |
|---|---|---|
| `OpenApiSnapshotTests` | сравнивает документ со снимком целиком | падает на **любом** изменении; снимок обновляется командой, diff идёт в тот же change set |
| `OpenApiFrozenSurfaceTests` | сверяет живой документ с `docs/openapi/v1-frozen-surface.json` на **включение** | дополнения проходят молча; исчезновение или переименование эндпоинта, кода ответа, поля тела или свойства DTO падает |

Разделение существенное. Снимок делает изменение видимым, но обновляется одной командой, и отличить в его diff «добавили поле» от «удалили поле» может только внимательный читатель — а ломает клиентов исключительно второе. У файла замороженной поверхности автоматической регенерации нет намеренно: он правится руками и только вместе с решением о v2, и тогда diff показывает ровно то, от чего отказались.

Поля ответов перечислены в нём полными путями (`items[].id.value`), потому что тела ответов встроены в операции, а не вынесены в `components`: список схем заморозил бы формы запросов и оставил формы ответов незамороженными.

**Что заморожено с оговоркой.** Генератор перечисляет в `required` все поля record'а, включая nullable: `AssignRequest.userId` и `AssignRequest.groupId` объявлены обязательными, хотя ровно один из них обязан быть `null`, а `attemptLimit` и `availableUntil` необязательны по смыслу. Документ строже реализации: приложение принимает запрос и без этих ключей. Это не мешает заморозке — ослабление `required` разрешено внутри v1 и не ломает уже написанного клиента, потому что он эти ключи и так отправляет. Приводить документ к смыслу следует отдельным изменением, а не в момент заморозки.

**Дата удаления legacy-возможностей.** Rewrite `/api/*` (ADR-019) и приём `idempotencyKey` в теле (ADR-020) остаются в v1. Календарная дата не назначается «на глаз»: она объявляется заголовком `Sunset` при развёртывании 1.0 и обязана удовлетворять двум условиям одновременно — не раньше чем через 90 дней после объявления и не раньше чем после 30 подряд суток нулевого legacy-трафика по telemetry. До выполнения обоих условий удаление откладывается; после — legacy-путь отвечает `410 api.version.retired`, что уже реализовано и настраивается конфигурацией. Само удаление кода — изменение v2 или отдельный мажорный шаг, потому что для клиента, всё ещё ходящего в `/api/*`, это ломающее изменение независимо от предупреждений.

## 2. OpenAPI

```http
GET /openapi/v1.json
```

Политика времени выполнения:

- Development: enabled + anonymous по умолчанию;
- Production: disabled по умолчанию;
- если `OpenApi:Enabled=true` и `OpenApi:AllowAnonymous=false`, требуется `operations:read`.

OpenAPI обогащается document/operation/schema transformers и содержит Bearer security scheme, operation IDs, summaries/descriptions, authorization requirements, `Idempotency-Key`/`If-Match`/`ETag`, ProblemDetails responses и examples/enum metadata. Serialized document защищён contract test.

## 3. Authentication

Аутентификация JWT Bearer через Keycloak.

Ожидаемые claims:

- `sub` — стабильный внешний ID пользователя;
- `roles` — роли приложения;
- `groups` — членство во внешних группах;
- `aud` — `Keycloak:Audience`.

`MapInboundClaims=false`.

## 4. Политики авторизации

| Policy | Roles | Scope |
|---|---|---|
| `tests:write` | `test-author`, `test-admin` | authoring/catalog/editor/archive |
| `tests:publish` | `test-author`, `test-admin` | publish |
| `tests:assign` | `test-admin` | администрирование назначений и ручной таймаут |
| `results:review` | `test-author`, `test-admin` | результаты рецензирования |
| `operations:read` | `test-admin` | аудит, Outbox и OpenAPI при защищённом режиме |

### Ownership

Role policy не является единственной защитой.

`Test.OwnerId` = Keycloak `sub` создавшего test.

- `test-author` видит/изменяет только свои tests;
- reviewer author видит результаты только собственных tests;
- `test-admin` имеет global scope;
- чужой editor/reviewer detail возвращается как unavailable/not found на read boundary;
- чужая write command возвращает `403 test.forbidden`.

## 5. ProblemDetails и status codes

Ошибки уровня приложения:

| ErrorType | HTTP |
|---|---:|
| Validation | 400 |
| NotFound | 404 |
| Conflict | 409 |
| Forbidden | 403 |
| PreconditionFailed | 412 |

Infrastructure/API contracts дополнительно используют:

- `428 concurrency.precondition_required`;
- `400 concurrency.if_match`;
- `409 concurrency.conflict`;
- `409 idempotency.key_reused`;
- `400 idempotency.key_mismatch`;
- `429 Too Many Requests` — превышен лимит частоты.

Необработанное исключение:

```text
500 internal.error
```

Stack trace/detail internal exception клиенту не выдаётся.

### Нулевой GUID

`00000000-0000-0000-0000-000000000000` не является допустимым идентификатором ни для одного ресурса (ADR-029). Ответ зависит от того, где он встретился:

| Расположение | Ответ | Почему |
|---|---|---|
| Route-сегмент (`/api/v1/tests/{id}`) | `404 Not Found` | сегмент именует ресурс, которого не может существовать — то же, что и для любого неизвестного идентификатора |
| Query-фильтр (`?testId=`, `?revisionId=`) | `400 request.validation` | это входное значение фильтра, а не имя ресурса |
| Поле тела запроса (`revisionId`, `optionIds[]`) | `400 request.validation` | та же категория, что `[Required]`/`[StringLength]` |

## 6. Correlation

Заголовок запроса и ответа:

```text
X-Correlation-ID
```

Клиентский ID принимается при длине <=128, иначе генерируется current TraceId/Guid v7. Correlation ID используется в audit/logging/telemetry.

Audit middleware наблюдает итоговый response после exception mapping. Для state-changing запросов audit status совпадает с клиентским status, включая handled `400/409`, precondition `412` и unhandled `500`.

## 7. Оптимистичный контроль параллельного доступа по HTTP: ETag / If-Match

Mutable authoring resource `Test` использует strong numeric ETag.

### 7.1 Получение версии

```http
GET /api/v1/tests/{testId}/editor
```

Response содержит:

```text
ETag: "7"
```

и body field:

```json
{
  "concurrencyVersion": 7
}
```

ETag соответствует текущему `Test.ConcurrencyVersion`.

### 7.2 Обязательный If-Match

Все изменения существующего `Test` требуют:

```text
If-Match: "7"
```

Это относится к:

- rename;
- settings;
- добавление/изменение/удаление/переупорядочивание вопроса;
- добавление/изменение/удаление/переупорядочивание варианта;
- publish;
- archive.

Semantics:

| Ситуация | HTTP / code |
|---|---|
| If-Match отсутствует | 428 `concurrency.precondition_required` |
| malformed/weak/wildcard | 400 `concurrency.if_match` |
| версия устарела | 412 `concurrency.precondition_failed` |
| версия актуальна | command исполняется |
| race после проверки | 409 `concurrency.conflict` через EF concurrency token |

Weak ETag `W/"7"` и wildcard `*` намеренно не поддерживаются.

## 8. Idempotency-Key

Основной публичный контракт повтора:

```text
Idempotency-Key: 8f85b273-9f45-4b4e-94dd-856f4697e8ad
```

Используется для:

- publish;
- одиночное назначение;
- массовое назначение;
- старт попытки;
- отправка попытки.

### Переходная совместимость

Легаси-поле в JSON:

```json
{
  "idempotencyKey": "..."
}
```

пока поддерживается.

Правила key resolution:

- key только в header — supported;
- только в теле — поддерживается как переходный вариант;
- оба одинаковые — supported;
- оба непустые, но разные -> `400 idempotency.key_mismatch`;
- некорректный или пустой обязательный ключ -> `400 idempotency.key`.

Для publish/start/submit key может находиться только в header: endpoint binding принимает настоящий zero-length body (request DTO параметр nullable) при валидном `Idempotency-Key` header — JSON body с legacy `idempotencyKey` остаётся поддержанным, но больше не обязателен.

### Отпечаток запроса

Для common idempotency operations persistence хранит SHA-256 fingerprint канонического логического payload.

Повтор:

```text
same actor + operation + key + same payload -> previous result replay
same actor + operation + key + different payload -> 409 idempotency.key_reused
```

Publish operation дополнительно resource-scoped по TestId.

Start attempt использует отдельный unique `(AssignmentId, UserId, StartRequestId)` + PostgreSQL advisory lease на assignment/user вокруг replay/count/insert transaction. Actor-scoped replay lookup выполняется до загрузки assignment и mutable availability/group-membership checks: уже успешный request возвращает прежний ID после cancellation, expiry или изменения group claim. Для нового key authorization, availability, revision и attempt-limit проверки выполняются полностью.

## 9. Pagination

Соглашение стороны чтения:

```text
page     default 1; minimum 1
pageSize default 20; clamp 1..100
```

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalCount": 0,
  "totalPages": 0
}
```

Offset paging во всех read models упорядочен deterministically (`timestamp`, затем `Id`); cursor pagination потребуется только при измеренном concurrent-churn use case.

## 10. Ограничение частоты запросов

Классы, задаваемые конфигурацией:

- general;
- student-write;
- privileged-read;
- operations.

Ключ партиционирования — аутентифицированный `sub`; запасной вариант — доверенный эффективный удалённый IP.

Отклонённый запрос:

```text
429 Too Many Requests
```

Correlation middleware выполняется до rate limiter, поэтому response сохраняет `X-Correlation-ID`.

---

# 11. API авторинга тестов

Base:

```text
/api/v1/tests
```

## 11.1 Catalog

```http
GET /api/v1/tests?page=1&pageSize=20&status=Published&search=text
Authorization: tests:write
```

Author получает только owned tests; admin — global list.

`TestCatalogItem`:

- id;
- title;
- status;
- passingPercentage;
- timeLimitMinutes;
- questionCount;
- publishedRevisionCount;
- latestRevisionVersion;
- latestPublishedAt.

## 11.2 Create

```http
POST /api/v1/tests
Authorization: tests:write
Content-Type: application/json

{
  "title": "DDD fundamentals"
}
```

Создание не требует `If-Match`, потому что resource ещё не существует. Owner = current `sub`.

Response: `TestId`.

## 11.3 Editor

```http
GET /api/v1/tests/{testId}/editor
Authorization: tests:write + ownership/admin scope
```

Response содержит questions/options/correctness и `ConcurrencyVersion`; response header содержит `ETag`.

Editor DTO **не является student-safe**.

## 11.4 Rename

```http
PATCH /api/v1/tests/{testId}/title
If-Match: "N"

{
  "title": "New title"
}
```

## 11.5 Settings

```http
PATCH /api/v1/tests/{testId}/settings
If-Match: "N"

{
  "passingPercentage": 70,
  "timeLimitMinutes": 30
}
```

`timeLimitMinutes` nullable.

## 11.6 Добавление вопроса

```http
POST /api/v1/tests/{testId}/questions
If-Match: "N"

{
  "text": "Question text",
  "type": 1,
  "points": 1,
  "order": 0
}
```

Текущие значения перечисления:

```text
1 = SingleChoice
2 = MultipleChoice
```

Перечисления в JSON сейчас сериализуются числами по умолчанию средствами System.Text.Json.

## 11.7 Изменение, удаление и переупорядочивание вопроса

```http
PUT    /api/v1/tests/{testId}/questions/{questionId}
DELETE /api/v1/tests/{testId}/questions/{questionId}
PATCH  /api/v1/tests/{testId}/questions/{questionId}/order
If-Match: "N"
```

## 11.8 Варианты ответа

```http
POST   /api/v1/tests/{testId}/questions/{questionId}/options
PUT    /api/v1/tests/{testId}/questions/{questionId}/options/{optionId}
DELETE /api/v1/tests/{testId}/questions/{questionId}/options/{optionId}
PATCH  /api/v1/tests/{testId}/questions/{questionId}/options/{optionId}/order
If-Match: "N"
```

## 11.9 Publish

```http
POST /api/v1/tests/{testId}/publish
Authorization: tests:publish + ownership/admin scope
If-Match: "N"
Idempotency-Key: <UUID>

{
  "idempotencyKey": "00000000-0000-0000-0000-000000000000"
}
```

Body key может быть empty при использовании header.

Response: `PublishedTestRevisionId`.

Idempotency replay проверяется до повторного aggregate mutation; retry уже завершённого publish возвращает сохранённый revision ID.

## 11.10 Archive

```http
POST /api/v1/tests/{testId}/archive
If-Match: "N"
```

## 11.11 Revisions

```http
GET /api/v1/tests/{testId}/revisions
Authorization: tests:write + owner/admin scope
```

Metadata list не раскрывает correctness.

---

# 12. API назначений

Base:

```text
/api/v1/assignments
```

Admin only для administration endpoints.

### List/detail/attempts

```http
GET /api/v1/assignments?testId=&revisionId=&targetType=&targetId=&status=&page=&pageSize=
GET /api/v1/assignments/{assignmentId}
GET /api/v1/assignments/{assignmentId}/attempts?status=&outcome=&page=&pageSize=
```

### Одиночное назначение

```http
POST /api/v1/assignments
Idempotency-Key: <UUID>

{
  "revisionId": "...",
  "userId": "student-sub",
  "groupId": null,
  "availableFrom": "2026-08-12T15:00:00Z",
  "availableUntil": null,
  "attemptLimit": 2,
  "idempotencyKey": "00000000-0000-0000-0000-000000000000"
}
```

Ровно один target: user или group.

### Массовое назначение

```http
POST /api/v1/assignments/bulk
Idempotency-Key: <UUID>
```

Constraints:

- 1..500 targets;
- каждый target = user xor group;
- duplicate target запрещён;
- весь batch валидируется до записи;
- fingerprint canonicalizes target set независимо от порядка входного массива.

### Lifecycle

```http
PATCH /api/v1/assignments/{assignmentId}/window
PATCH /api/v1/assignments/{assignmentId}/attempt-limit
POST  /api/v1/assignments/{assignmentId}/cancel
```

### Старт попытки

```http
POST /api/v1/assignments/{assignmentId}/attempts
Authorization: authenticated + target/availability validation
Idempotency-Key: <UUID>
```

Start имеет дополнительную DB uniqueness/attempt-limit concurrency protection.

---

# 13. API текущего пользователя

```http
GET /api/v1/me/assignments?page=&pageSize=&status=
GET /api/v1/me/attempts?page=&pageSize=&status=
```

Assignment visibility = direct user target или current token group membership.

---

# 14. API попыток

```http
PUT    /api/v1/attempts/{attemptId}/answers/{questionId}
DELETE /api/v1/attempts/{attemptId}/answers/{questionId}
POST   /api/v1/attempts/{attemptId}/submit
GET    /api/v1/attempts/{attemptId}
GET    /api/v1/attempts/{attemptId}/presentation
GET    /api/v1/attempts/{attemptId}/result
```

### Student presentation и resume

```http
GET /api/v1/attempts/{attemptId}/presentation
GET /api/v1/assignments/{assignmentId}/attempts/active
```

Оба возвращают один и тот же `AttemptPresentationView` — всё, что нужно для прохождения или возобновления попытки:

- вопросы и варианты из **immutable revision**, к которой привязана попытка (редактирование working `Test` после публикации не меняет попытку в полёте);
- сохранённые ответы самого студента (`selectedOptionIds`, `answeredAt`);
- `status`/`startedAt`/`deadlineAt`/`completedAt`, `passingPercentage`, `timeLimitMinutes`;
- `serverTime` — авторитетное серверное время, чтобы обратный отсчёт не зависел от часов клиента.

`presentation` доступен только владельцу попытки; чужая попытка возвращает `404` (а не `403`, чтобы не подтверждать существование). Reviewer/admin читают ту же попытку через `/api/v1/results/{attemptId}`, который намеренно содержит корректность.

`attempts/active` возвращает попытку в статусе `InProgress` для данного assignment или `404`, если возобновлять нечего — это сигнал клиенту стартовать новую. Завершённые попытки через resume не отдаются, они читаются через `/result`.

**Answer-key boundary:** presentation DTO не содержит признака правильности ни на одном уровне. Это свойство проекции, а не хранилища — revision хранит корректность в том же `jsonb`, поэтому граница закреплена тестом на сериализованном ответе. См. ADR-028.

**Окно между дедлайном и worker'ом:** GET не завершает попытку. Между истечением `deadlineAt` и проходом expiration worker возможен ответ с `status=InProgress` и `deadlineAt < serverTime`; любая запись в этом окне вернёт `409 attempt.expired`.

Submit использует `Idempotency-Key`.

Student attempt/read DTOs не содержат `IsCorrect`.

Если deadline истёк, answer/submit фиксируют TimedOut либо background worker завершает attempt отдельно.

Ручной таймаут администратором:

```http
POST /api/v1/attempts/{attemptId}/timeout
Authorization: tests:assign
```

Этот эндпоинт намеренно не требует наступившего deadline: он существует именно для попыток, которые часы не закроют — по тесту без ограничения времени либо когда попытку нужно завершить досрочно. Автоматические пути (answer/submit по истёкшему deadline, background worker) закрывают попытку только после deadline и отвечают `409 attempt.not_expired`, если он ещё не наступил. Оба пути дают статус `TimedOut`; см. ADR-030.

---

# 15. API рецензента

```http
GET /api/v1/results?testId=&revisionId=&outcome=&page=&pageSize=
GET /api/v1/results/{attemptId}
Authorization: results:review
```

`test-author` scope ограничен owned tests. `test-admin` global.

Detailed reviewer DTO включает:

- текст, тип, порядок и баллы вопроса;
- выбранные варианты;
- `IsCorrect`;
- набранные баллы;
- итоговый балл и результат.

Этот DTO нельзя переиспользовать в student API.

---

# 16. Эксплуатационный API

```http
GET /api/v1/operations/outbox?deadLetterLimit=20
GET /api/v1/operations/audit?actorId=&statusCode=&from=&to=&page=&pageSize=
GET /api/v1/operations/outbox/dead-letters/{eventId}
POST /api/v1/operations/outbox/dead-letters/{eventId}/requeue
POST /api/v1/operations/outbox/dead-letters/{eventId}/discard
Authorization: operations:read
```

Operational rate-limit policy применяется отдельно от general API.

Outbox/dead-letter endpoints не раскрывают event payload. Requeue/discard требуют непустой reason, используют shared message lock и сохраняют actor/action/reason/correlation audit.

---

```http
GET /api/v1/operations/version
Authorization: operations:read
```

Отвечает, что именно развёрнуто: `version` — информационная версия работающей сборки, `imageDigest` — immutable digest образа GHCR из переменной `TESTAPP_IMAGE_DIGEST` (или `null`, если развёртывание её не задало). Подробности выпуска — `docs/OPERATIONS.md` §16.2.

# 17. Health

```http
GET /health/live
GET /health/ready
```

- live — жизнеспособность процесса;
- ready — PostgreSQL; RabbitMQ также проверяется при enabled delivery.

---

# 18. Контракт безопасности периметра и времени выполнения

- forwarded headers учитываются только от configured trusted proxies/networks;
- CORS выключен по умолчанию; при включении только explicit origins;
- wildcard origin запрещён;
- решение о перенаправлении на HTTPS и HSTS в production принимается явно;
- базовые заголовки безопасности задаются конфигурацией;
- Kestrel server banner выключен.

---

# 19. Следующие изменения API

Ближайший stabilization scope:

- student-safe attempt presentation/resume DTO без correctness leakage.

Legacy body `idempotencyKey` и `/api/*` rewrite удаляются только через объявленное compatibility window и lifecycle telemetry.
