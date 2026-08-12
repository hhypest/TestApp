# HTTP API TestApp

> Статус: **Implemented API contract**. Canonical base path — `/api/v1`.

## 1. Versioning и compatibility

Canonical endpoints находятся под:

```text
/api/v1/...
```

Для обратной совместимости текущий middleware переписывает legacy path:

```text
/api/... -> /api/v1/...
```

если request ещё не начинается с `/api/v1`.

### Важно

Legacy rewrite — временный compatibility mechanism, а не полноценная lifecycle/versioning policy. Срок deprecation `/api/*` пока не определён.

## 2. OpenAPI

```text
GET /openapi/v1.json
```

Endpoint anonymous.

Текущий OpenAPI генерируется ASP.NET Core `AddOpenApi/MapOpenApi` из Minimal API metadata. Полные examples/descriptions/error schemas ещё требуют дополнительного hardening.

## 3. Authentication

API использует JWT Bearer.

Ожидаемые claims:

- `sub` — external user ID;
- `roles` — application roles;
- `groups` — external group membership;
- `aud` должен соответствовать `Keycloak:Audience`.

`MapInboundClaims=false`.

Все `/api/v1/*` endpoints, кроме явно anonymous infrastructure endpoints, требуют authentication напрямую или через authenticated route group.

## 4. Authorization policies

| Policy | Allowed roles | Назначение |
|---|---|---|
| `tests:write` | `test-author`, `test-admin` | authoring/catalog/editor/archive |
| `tests:publish` | `test-author`, `test-admin` | publication |
| `tests:assign` | `test-admin` | assignment administration/manual timeout |
| `results:review` | `test-author`, `test-admin` | reviewer result views |
| `operations:read` | `test-admin` | audit/outbox operations |

### Критическое ограничение

Role policy пока не дополняется ownership check для `Test`. Любой `test-author`, имеющий TestId, в текущей модели может обращаться к authoring operations этого test.

## 5. Общие HTTP semantics

### 5.1 Success

Command endpoints в основном возвращают:

```text
200 OK
```

с value/result JSON body.

Read endpoints возвращают `200`, а detail queries — `404`, если read model отсутствует.

### 5.2 Application error mapping

`Result<T, Error>` отображается так:

| ErrorType | HTTP |
|---|---:|
| Validation | 400 |
| NotFound | 404 |
| Conflict | 409 |
| Forbidden | 403 |
| Other | 500 |

ProblemDetails:

- `title` = application error code;
- `detail` = error message.

### 5.3 Exception mapping

Optimistic concurrency:

```text
409 Conflict
Title: concurrency.conflict
```

Unhandled exception:

```text
500 Internal Server Error
Title: internal.error
Detail: An unexpected error occurred.
```

Unhandled exception details/stack trace клиенту не раскрываются. `traceId` помещается в ProblemDetails extensions.

## 6. Correlation

Header:

```text
X-Correlation-ID
```

Поведение:

- клиент может передать собственный ID длиной <= 128;
- иначе используется current Activity TraceId либо Guid v7;
- ID записывается в `HttpContext.TraceIdentifier`;
- возвращается в response header;
- используется audit/logging/telemetry.

## 7. Rate limiting

Текущая global policy:

- Fixed Window;
- 120 requests;
- окно 1 minute;
- queue limit = 0;
- partition key = authenticated `sub`, иначе remote IP, иначе `anonymous`;
- reject status = `429 Too Many Requests`.

Policy пока hard-coded и должна стать configuration-driven.

## 8. Pagination

Стандартная read-side pagination:

```text
page     default 1, minimum 1
pageSize default 20, clamp 1..100
```

`PagedResult<T>`:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalCount": 0,
  "totalPages": 0
}
```

## 9. Idempotency

Текущий public contract передаёт `Guid idempotencyKey` в JSON body для:

- publish;
- single assignment;
- bulk assignment;
- start attempt;
- submit attempt.

Empty Guid отклоняется.

### Planned

Перенести общий HTTP contract на `Idempotency-Key` header с transitional body compatibility.

---

# 10. Tests/authoring endpoints

Base group:

```text
/api/v1/tests
```

Все endpoints authenticated.

## 10.1 Catalog

```http
GET /api/v1/tests?page=1&pageSize=20&status=Published&search=text
Authorization: tests:write
```

Response: `PagedResult<TestCatalogItem>`.

Catalog item:

- `id`;
- `title`;
- `status`;
- `passingPercentage`;
- `timeLimitMinutes`;
- `questionCount`;
- `publishedRevisionCount`;
- `latestRevisionVersion`;
- `latestPublishedAt`.

## 10.2 Create

```http
POST /api/v1/tests
Authorization: tests:write
Content-Type: application/json

{
  "title": "DDD fundamentals"
}
```

Response: `TestId`.

## 10.3 Rename

```http
PATCH /api/v1/tests/{testId}/title
Authorization: tests:write

{
  "title": "New title"
}
```

## 10.4 Settings

```http
PATCH /api/v1/tests/{testId}/settings
Authorization: tests:write

{
  "passingPercentage": 70,
  "timeLimitMinutes": 30
}
```

`timeLimitMinutes` nullable.

## 10.5 Add question

```http
POST /api/v1/tests/{testId}/questions
Authorization: tests:write

{
  "text": "Question text",
  "type": 1,
  "points": 1,
  "order": 0
}
```

Текущий API не регистрирует `JsonStringEnumConverter`, поэтому `QuestionType` передаётся стандартным numeric enum JSON contract:

```text
1 = SingleChoice
2 = MultipleChoice
```

Response: `QuestionId`.

## 10.6 Update question

```http
PUT /api/v1/tests/{testId}/questions/{questionId}
Authorization: tests:write

{
  "text": "Updated",
  "type": 1,
  "points": 2
}
```

## 10.7 Remove question

```http
DELETE /api/v1/tests/{testId}/questions/{questionId}
Authorization: tests:write
```

## 10.8 Reorder question

```http
PATCH /api/v1/tests/{testId}/questions/{questionId}/order
Authorization: tests:write

{
  "order": 3
}
```

## 10.9 Add answer option

```http
POST /api/v1/tests/{testId}/questions/{questionId}/options
Authorization: tests:write

{
  "text": "Answer",
  "isCorrect": true,
  "order": 0
}
```

Response: `AnswerOptionId`.

## 10.10 Update answer option

```http
PUT /api/v1/tests/{testId}/questions/{questionId}/options/{optionId}
Authorization: tests:write

{
  "text": "Updated answer",
  "isCorrect": false
}
```

## 10.11 Remove option

```http
DELETE /api/v1/tests/{testId}/questions/{questionId}/options/{optionId}
Authorization: tests:write
```

## 10.12 Reorder option

```http
PATCH /api/v1/tests/{testId}/questions/{questionId}/options/{optionId}/order
Authorization: tests:write

{
  "order": 2
}
```

## 10.13 Publish

```http
POST /api/v1/tests/{testId}/publish
Authorization: tests:publish

{
  "idempotencyKey": "00000000-0000-0000-0000-000000000001"
}
```

Response: `PublishedTestRevisionId`.

## 10.14 Archive

```http
POST /api/v1/tests/{testId}/archive
Authorization: tests:write
```

## 10.15 Editor view

```http
GET /api/v1/tests/{testId}/editor
Authorization: tests:write
```

Editor view включает `IsCorrect` и поэтому не является student-safe DTO.

## 10.16 Revision list

```http
GET /api/v1/tests/{testId}/revisions
Authorization: tests:write
```

Response metadata не раскрывает answer correctness.

---

# 11. Assignment endpoints

Base:

```text
/api/v1/assignments
```

## 11.1 Admin list

```http
GET /api/v1/assignments?testId=&revisionId=&targetType=&targetId=&status=&page=1&pageSize=20
Authorization: tests:assign
```

Response содержит assignment metadata и aggregate attempt statistics.

## 11.2 Admin detail

```http
GET /api/v1/assignments/{assignmentId}
Authorization: tests:assign
```

## 11.3 Assignment attempts

```http
GET /api/v1/assignments/{assignmentId}/attempts?status=&outcome=&page=1&pageSize=20
Authorization: tests:assign
```

## 11.4 Single assign

```http
POST /api/v1/assignments
Authorization: tests:assign

{
  "revisionId": "...",
  "userId": "student-sub",
  "groupId": null,
  "availableFrom": "2026-08-12T15:00:00Z",
  "availableUntil": "2026-08-13T15:00:00Z",
  "attemptLimit": 2,
  "idempotencyKey": "..."
}
```

Ровно одно из `userId/groupId` должно быть задано.

## 11.5 Bulk assign

```http
POST /api/v1/assignments/bulk
Authorization: tests:assign

{
  "revisionId": "...",
  "targets": [
    { "userId": "student-1", "groupId": null },
    { "userId": null, "groupId": "students" }
  ],
  "availableFrom": "...",
  "availableUntil": null,
  "attemptLimit": 1,
  "idempotencyKey": "..."
}
```

Constraints:

- 1..500 targets;
- каждый target — ровно user или group;
- duplicate target внутри batch запрещён.

Response: `BulkAssignTestsResult { createdCount, assignmentIds[] }`.

## 11.6 Change window

```http
PATCH /api/v1/assignments/{assignmentId}/window
Authorization: tests:assign

{
  "availableFrom": "...",
  "availableUntil": "..."
}
```

## 11.7 Change attempt limit

```http
PATCH /api/v1/assignments/{assignmentId}/attempt-limit
Authorization: tests:assign

{
  "attemptLimit": 3
}
```

Nullable means unlimited.

## 11.8 Cancel

```http
POST /api/v1/assignments/{assignmentId}/cancel
Authorization: tests:assign

{
  "reason": "Cancelled by administrator"
}
```

## 11.9 Start attempt

```http
POST /api/v1/assignments/{assignmentId}/attempts
Authorization: any authenticated user + application target validation

{
  "idempotencyKey": "..."
}
```

Response: `TestAttemptId`.

Handler дополнительно проверяет target membership, availability, revision и attempt limit.

---

# 12. Current-user read endpoints

## 12.1 My assignments

```http
GET /api/v1/me/assignments?page=1&pageSize=20&status=Active
Authorization: authenticated
```

Visibility = direct user target или current actor group membership.

## 12.2 My attempts

```http
GET /api/v1/me/attempts?page=1&pageSize=20&status=InProgress
Authorization: authenticated
```

---

# 13. Attempt endpoints

Base:

```text
/api/v1/attempts
```

## 13.1 Answer question

```http
PUT /api/v1/attempts/{attemptId}/answers/{questionId}
Authorization: authenticated + ownership

{
  "optionIds": ["guid"]
}
```

Application/revision validation:

- attempt ownership;
- attempt `InProgress`;
- deadline;
- question membership;
- selection count for SingleChoice;
- option membership.

## 13.2 Clear answer

```http
DELETE /api/v1/attempts/{attemptId}/answers/{questionId}
Authorization: authenticated + ownership
```

## 13.3 Submit

```http
POST /api/v1/attempts/{attemptId}/submit
Authorization: authenticated + ownership

{
  "idempotencyKey": "..."
}
```

Если deadline истёк к моменту submit, attempt завершается как `TimedOut`, а score всё равно вычисляется по сохранённым responses.

Response: `AttemptScore`.

## 13.4 Manual timeout

```http
POST /api/v1/attempts/{attemptId}/timeout
Authorization: tests:assign
```

Admin-only manual completion.

## 13.5 Attempt detail

```http
GET /api/v1/attempts/{attemptId}
Authorization: authenticated + ownership in read query
```

Student-safe: содержит selected IDs, но не correct flags.

## 13.6 Own result

```http
GET /api/v1/attempts/{attemptId}/result
Authorization: authenticated + ownership in read query
```

Не содержит correct-answer breakdown.

---

# 14. Reviewer results

## 14.1 Result list

```http
GET /api/v1/results?testId=&revisionId=&outcome=&page=1&pageSize=20
Authorization: results:review
```

## 14.2 Detailed result

```http
GET /api/v1/results/{attemptId}
Authorization: results:review
```

Reviewer detail включает:

- question text/type/order/points;
- selected options;
- `IsCorrect`;
- earned points;
- final score/outcome.

### Security note

Этот DTO нельзя переиспользовать в student endpoint.

---

# 15. Operational endpoints

## 15.1 Outbox status

```http
GET /api/v1/operations/outbox?deadLetterLimit=20
Authorization: operations:read
```

Возвращает pending/retry/dead-letter operational information без выдачи event payload.

## 15.2 Audit trail

```http
GET /api/v1/operations/audit?actorId=&statusCode=&from=&to=&page=1&pageSize=20
Authorization: operations:read
```

Audit entry:

- ID;
- occurredAt;
- actorId;
- method;
- route;
- statusCode;
- correlationId;
- traceId;
- durationMs.

---

# 16. Health endpoints

```http
GET /health/live
GET /health/ready
```

`live` не зависит от external services.

`ready` проверяет MariaDB; при включённом RabbitMQ delivery дополнительно проверяет broker connection/channel/exchange access.

---

# 17. Что API пока не гарантирует

- ownership isolation между разными authors;
- tenant isolation;
- standard `Idempotency-Key` header;
- ETag/If-Match optimistic HTTP contract;
- configurable per-endpoint rate limits;
- formal deprecation headers для legacy `/api/*`;
- stable integration event HTTP/webhook API;
- media upload endpoints;
- public frontend-oriented BFF.

Эти пункты находятся в `ROADMAP.md`/`FEATURE_PLAN.md`.