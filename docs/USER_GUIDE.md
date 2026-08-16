# Руководство пользователя TestApp (по ролям)

> Статус: **Implemented snapshot**, baseline 2026-08-16. Frontend отсутствует — единственный публичный интерфейс системы это HTTP API (`/api/v1`). Документ описывает пошаговые сценарии для каждой роли поверх этого API. Полный технический reference со всеми полями/кодами ошибок — [API.md](API.md); готовый end-to-end сценарий для импорта в Postman — [tests/TestApp.Postman](../tests/TestApp.Postman/README.md).

Документ различает **Implemented** (работает в текущем коде) и **Planned** (описано в roadmap, но ещё не реализовано) — см. правило в [docs/README.md](README.md#как-читать-документацию). Там, где текущая реализация имеет практическое ограничение для роли, это указано явно, а не сглажено.

## 0. Роли системы

| Роль | Кто это | Основные возможности |
|---|---|---|
| **test-author** | автор тестов | создание/редактирование/публикация **собственных** тестов, просмотр результатов **собственных** тестов |
| **test-admin** | администратор | всё, что может author, но глобально (любые тесты) + управление назначениями, operational endpoints (audit/outbox/dead-letter) |
| **student** | проходящий тест | видит свои назначения, проходит попытки, видит свой результат |

Роль определяется claim'ом `roles` в JWT (`test-author`/`test-admin`), принадлежность к группе — claim'ом `groups` (например, `students`). Один и тот же пользователь Keycloak может одновременно не иметь этих ролей — тогда он видит только `/api/v1/me/*` как студент без назначений.

## 1. Подготовка стенда (один раз, для всех ролей)

```bash
docker compose up --build -d
curl --fail http://localhost:8080/health/ready
```

Локальный стенд поднимает API (`:8080`), Keycloak (`:8081`), PostgreSQL 18, RabbitMQ. Тестовые dev-учётные записи (только для локальной разработки, см. [OPERATIONS.md](OPERATIONS.md) и [Postman README](../tests/TestApp.Postman/README.md#local-credentials)):

| User | Password | Роль/группа |
|---|---|---|
| `author` | `author` | `test-author` |
| `admin` | `admin` | `test-admin` |
| `student` | `student` | группа `students` |

### 1.1 Получение JWT

Realm `testapp`, публичный client `testapp-api` (password grant разрешён только на dev-стенде):

```bash
TOKEN_AUTHOR=$(curl -s -X POST \
  http://localhost:8081/realms/testapp/protocol/openid-connect/token \
  -d grant_type=password -d client_id=testapp-api \
  -d username=author -d password=author \
  | jq -r .access_token)

TOKEN_ADMIN=$(curl -s -X POST \
  http://localhost:8081/realms/testapp/protocol/openid-connect/token \
  -d grant_type=password -d client_id=testapp-api \
  -d username=admin -d password=admin \
  | jq -r .access_token)

TOKEN_STUDENT=$(curl -s -X POST \
  http://localhost:8081/realms/testapp/protocol/openid-connect/token \
  -d grant_type=password -d client_id=testapp-api \
  -d username=student -d password=student \
  | jq -r .access_token)
```

Токен живёт ограниченное время (dev realm default), при `401` просто получите новый тем же запросом — refresh-flow в этом руководстве не используется.

### 1.2 Общие HTTP-конвенции

Каждый запрос к API:

```text
Authorization: Bearer <token>
Content-Type: application/json      # только если есть JSON body
```

Дополнительно, где требуется:

```text
Idempotency-Key: <UUID>             # publish, assign, bulk-assign, start attempt, submit attempt
If-Match: "<ConcurrencyVersion>"    # любое изменение уже существующего Test
X-Correlation-ID: <строка ≤128>     # опционально, иначе сервер сгенерирует сам
```

`Idempotency-Key` **обязательно генерируйте новый UUID на каждое новое логическое действие** (`uuidgen` или `python3 -c "import uuid;print(uuid.uuid4())"`) и **переиспользуйте тот же UUID**, если повторяете тот же самый запрос после сетевого сбоя/таймаута — так вы получите тот же результат вместо дубликата.

Ошибки возвращаются как `ProblemDetails`:

```json
{
  "type": "https://httpstatuses.io/400",
  "title": "test.title",
  "status": 400,
  "detail": "Title is required.",
  "traceId": "..."
}
```

Частые коды: `400` (валидация/malformed запрос), `401` (нет/невалидный токен), `403` (роль есть, но не владелец/не тот scope), `404` (не найдено или недоступно чужому author), `409` (конфликт/повтор с другим payload), `412` (устаревший `If-Match`), `428` (`If-Match` не передан), `429` (rate limit). Полная таблица — [API.md §5](API.md#5-problemdetails-и-status-codes).

Постраничные ответы:

```json
{ "items": [], "page": 1, "pageSize": 20, "totalCount": 0, "totalPages": 0 }
```

---

## 2. Роль test-author — создание и публикация теста

Ниже — полный жизненный цикл теста от создания до публикации, в порядке реального использования.

### 2.1 Каталог собственных тестов

```bash
curl -s http://localhost:8080/api/v1/tests?page=1\&pageSize=20 \
  -H "Authorization: Bearer $TOKEN_AUTHOR"
```

Автор видит только тесты, где он owner (`OwnerId` = его `sub`).

### 2.2 Создать тест

```bash
TEST_ID=$(curl -s -X POST http://localhost:8080/api/v1/tests \
  -H "Authorization: Bearer $TOKEN_AUTHOR" -H "Content-Type: application/json" \
  -d '{"title": "DDD fundamentals"}' | jq -r .)
```

Новый тест создаётся в статусе `Draft`, owner = текущий `sub`. `If-Match` для создания не нужен — ресурса ещё нет.

### 2.3 Открыть editor и получить версию (ETag)

```bash
curl -si http://localhost:8080/api/v1/tests/$TEST_ID/editor \
  -H "Authorization: Bearer $TOKEN_AUTHOR"
```

В ответе — заголовок `ETag: "1"` и тело с `concurrencyVersion`. **Любое** последующее изменение этого теста требует `If-Match` с текущим значением ETag; после каждого успешного изменения версия увеличивается — заново читайте editor или берите версию из ответа мутации перед следующим шагом.

### 2.4 Переименовать / изменить настройки прохождения

```bash
curl -s -X PATCH http://localhost:8080/api/v1/tests/$TEST_ID/title \
  -H "Authorization: Bearer $TOKEN_AUTHOR" -H "Content-Type: application/json" \
  -H 'If-Match: "1"' \
  -d '{"title": "DDD fundamentals — module 1"}'

curl -s -X PATCH http://localhost:8080/api/v1/tests/$TEST_ID/settings \
  -H "Authorization: Bearer $TOKEN_AUTHOR" -H "Content-Type: application/json" \
  -H 'If-Match: "2"' \
  -d '{"passingPercentage": 70, "timeLimitMinutes": 30}'
```

Ограничения (проверяются на сервере): `passingPercentage` — число `0..100` (по умолчанию для нового теста `70`); `timeLimitMinutes` — либо `null` (без ограничения по времени), либо положительное целое.

### 2.5 Добавить вопрос

```bash
curl -s -X POST http://localhost:8080/api/v1/tests/$TEST_ID/questions \
  -H "Authorization: Bearer $TOKEN_AUTHOR" -H "Content-Type: application/json" \
  -H 'If-Match: "3"' \
  -d '{"text": "Что такое Aggregate Root?", "type": 1, "points": 1, "order": 0}'
```

`type`: `1` = `SingleChoice`, `2` = `MultipleChoice`. `order` должен быть уникален в пределах теста; `points` — строго больше нуля. Ответ — `questionId`. Обновление/удаление/переупорядочивание — `PUT`/`DELETE`/`PATCH .../order` по тому же пути с `{questionId}`, тоже с `If-Match`.

### 2.6 Добавить варианты ответа

```bash
curl -s -X POST http://localhost:8080/api/v1/tests/$TEST_ID/questions/$QUESTION_ID/options \
  -H "Authorization: Bearer $TOKEN_AUTHOR" -H "Content-Type: application/json" \
  -H 'If-Match: "4"' \
  -d '{"text": "Сущность с идентичностью, точка входа в изменения aggregate", "isCorrect": true, "order": 0}'
```

Повторите для остальных вариантов (обычно 3–5 на вопрос), увеличивая `order` и `If-Match` на каждый шаг. **Бизнес-правила, которые сервер проверит на публикации (см. 2.7), а для `SingleChoice` — уже при добавлении второго `isCorrect: true`:**

- `SingleChoice` — ровно один вариант с `isCorrect: true`; попытка пометить вторым правильным вариантом вернёт `409 test.single_choice.multiple_correct`;
- `MultipleChoice` — хотя бы один вариант с `isCorrect: true`.

Обновление/удаление/переупорядочивание вариантов — та же схема `PUT`/`DELETE`/`PATCH .../order` с `{optionId}`.

### 2.7 Опубликовать тест

```bash
IDEMPOTENCY_KEY=$(python3 -c "import uuid;print(uuid.uuid4())")
curl -s -X POST http://localhost:8080/api/v1/tests/$TEST_ID/publish \
  -H "Authorization: Bearer $TOKEN_AUTHOR" \
  -H 'If-Match: "8"' \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY"
```

Publish создаёт immutable `PublishedTestRevision` — снимок вопросов/вариантов/правильности на момент публикации. Требования, которые проверяются перед публикацией:

- в тесте есть хотя бы один вопрос;
- у каждого `SingleChoice` вопроса ровно один правильный вариант;
- у каждого `MultipleChoice` вопроса хотя бы один правильный вариант.

Любое несоответствие вернёт `400`/`409` с конкретным кодом ошибки вместо публикации. Body можно не передавать (пустое тело допустимо при `Idempotency-Key` в заголовке) — ответ содержит `PublishedTestRevisionId`, который дальше нужен `test-admin` для назначения.

**Важно:** после публикации любое дальнейшее изменение теста (даже опции) автоматически переводит его обратно в `Draft` (`Status` меняется, но уже опубликованная revision остаётся неизменной и продолжает использоваться в существующих назначениях/попытках) — повторная публикация создаёт **новую** revision.

### 2.8 История revisions и результаты своих тестов

```bash
curl -s http://localhost:8080/api/v1/tests/$TEST_ID/revisions \
  -H "Authorization: Bearer $TOKEN_AUTHOR"

curl -s "http://localhost:8080/api/v1/results?testId=$TEST_ID&page=1&pageSize=20" \
  -H "Authorization: Bearer $TOKEN_AUTHOR"

curl -s http://localhost:8080/api/v1/results/$ATTEMPT_ID \
  -H "Authorization: Bearer $TOKEN_AUTHOR"
```

`/api/v1/results` (`results:review`) для author ограничен собственными тестами; detail содержит текст вопроса/вариантов, что выбрал студент, `isCorrect` по каждому варианту и итоговый score — этот DTO не должен показываться студенту (см. §4 ниже).

### 2.9 Архивировать тест

```bash
curl -s -X POST http://localhost:8080/api/v1/tests/$TEST_ID/archive \
  -H "Authorization: Bearer $TOKEN_AUTHOR" -H 'If-Match: "9"'
```

Архивный тест нельзя больше редактировать/публиковать (`409 test.archived`); уже созданные назначения/попытки по его revisions не затрагиваются.

---

## 3. Роль test-admin — назначения и эксплуатация

`test-admin` может выполнять все шаги §2 для **любого** теста (глобальный scope), плюс:

### 3.1 Назначить revision пользователю

```bash
IDEMPOTENCY_KEY=$(python3 -c "import uuid;print(uuid.uuid4())")
curl -s -X POST http://localhost:8080/api/v1/assignments \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -d '{
    "revisionId": "'"$REVISION_ID"'",
    "userId": "<sub студента>",
    "groupId": null,
    "availableFrom": "2026-08-16T09:00:00Z",
    "availableUntil": "2026-08-30T23:59:59Z",
    "attemptLimit": 2,
    "idempotencyKey": "00000000-0000-0000-0000-000000000000"
  }'
```

Ровно одно из `userId`/`groupId` должно быть непустым, второе — `null`. `availableUntil` (если задан) должен быть строго позже `availableFrom` (`400 assignment.window` иначе), `attemptLimit` — либо `null` (без ограничения), либо положительное целое (`400 assignment.attempt_limit` иначе). `sub` студента можно получить, декодировав его JWT (`jwt.io` или `python3 -c "import base64,json,sys; ..."`), либо назначить по группе (см. 3.2) — это проще, если студентов много.

### 3.2 Массовое назначение по группе

```bash
IDEMPOTENCY_KEY=$(python3 -c "import uuid;print(uuid.uuid4())")
curl -s -X POST http://localhost:8080/api/v1/assignments/bulk \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -d '{
    "revisionId": "'"$REVISION_ID"'",
    "targets": [{"userId": null, "groupId": "students"}],
    "availableFrom": "2026-08-16T09:00:00Z",
    "availableUntil": null,
    "attemptLimit": null,
    "idempotencyKey": "00000000-0000-0000-0000-000000000000"
  }'
```

До 500 targets за раз, каждый — либо `userId`, либо `groupId` (не оба); дубликаты targets запрещены; весь batch валидируется целиком до записи (частичный успех невозможен). Локальная dev-группа — `students` (claim `groups` в токене студента).

### 3.3 Список/детали/изменение назначений

```bash
curl -s "http://localhost:8080/api/v1/assignments?testId=$TEST_ID&page=1&pageSize=20" \
  -H "Authorization: Bearer $TOKEN_ADMIN"

curl -s http://localhost:8080/api/v1/assignments/$ASSIGNMENT_ID \
  -H "Authorization: Bearer $TOKEN_ADMIN"

curl -s "http://localhost:8080/api/v1/assignments/$ASSIGNMENT_ID/attempts?page=1&pageSize=20" \
  -H "Authorization: Bearer $TOKEN_ADMIN"

curl -s -X PATCH http://localhost:8080/api/v1/assignments/$ASSIGNMENT_ID/window \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" \
  -d '{"availableFrom": "2026-08-16T09:00:00Z", "availableUntil": "2026-09-01T00:00:00Z"}'

curl -s -X PATCH http://localhost:8080/api/v1/assignments/$ASSIGNMENT_ID/attempt-limit \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" \
  -d '{"attemptLimit": 3}'

curl -s -X POST http://localhost:8080/api/v1/assignments/$ASSIGNMENT_ID/cancel \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" \
  -d '{"reason": "test superseded by v2"}'
```

Изменение window/attempt-limit **не** требует `If-Match` (не общая с `Test` concurrency-модель) и **не** влияет на уже завершённые попытки — только на возможность стартовать новые.

### 3.4 Принудительный timeout попытки

```bash
curl -s -X POST http://localhost:8080/api/v1/attempts/$ATTEMPT_ID/timeout \
  -H "Authorization: Bearer $TOKEN_ADMIN"
```

Используется, если нужно закрыть попытку раньше фонового воркера (например, студент забыл submit, а результат нужен прямо сейчас).

### 3.5 Результаты по всем тестам (не только своим)

```bash
curl -s "http://localhost:8080/api/v1/results?outcome=Failed&page=1&pageSize=20" \
  -H "Authorization: Bearer $TOKEN_ADMIN"
```

В отличие от `test-author`, `test-admin` не ограничен owner — видит все тесты/попытки.

### 3.6 Operational endpoints (только test-admin, policy `operations:read`)

```bash
curl -s "http://localhost:8080/api/v1/operations/audit?statusCode=500&page=1&pageSize=20" \
  -H "Authorization: Bearer $TOKEN_ADMIN"

curl -s "http://localhost:8080/api/v1/operations/outbox?deadLetterLimit=20" \
  -H "Authorization: Bearer $TOKEN_ADMIN"

curl -s http://localhost:8080/api/v1/operations/outbox/dead-letters/$EVENT_ID \
  -H "Authorization: Bearer $TOKEN_ADMIN"

curl -s -X POST http://localhost:8080/api/v1/operations/outbox/dead-letters/$EVENT_ID/requeue \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" \
  -d '{"reason": "downstream consumer restored"}'

curl -s -X POST http://localhost:8080/api/v1/operations/outbox/dead-letters/$EVENT_ID/discard \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" \
  -d '{"reason": "duplicate, safe to drop"}'
```

`reason` обязателен и непуст для `requeue`/`discard` — это осознанное операционное действие, попадающее в audit (actor/action/reason/correlation). `requeue` и `discard` взаимоисключающие — не выполняйте оба на одном событии.

---

## 4. Роль student — прохождение теста

### 4.1 Мои назначения

```bash
curl -s "http://localhost:8080/api/v1/me/assignments?page=1&pageSize=20" \
  -H "Authorization: Bearer $TOKEN_STUDENT"
```

Видны назначения, где студент — прямой `userId`-target **или** член указанной в `groupId` группы (по `groups` claim текущего токена).

### 4.2 Начать попытку

```bash
IDEMPOTENCY_KEY=$(python3 -c "import uuid;print(uuid.uuid4())")
ATTEMPT_ID=$(curl -s -X POST http://localhost:8080/api/v1/assignments/$ASSIGNMENT_ID/attempts \
  -H "Authorization: Bearer $TOKEN_STUDENT" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" | jq -r .)
```

Сервер проверяет: назначение существует и не отменено, текущее время внутри `availableFrom`/`availableUntil`, лимит попыток не исчерпан. Повтор с тем же `Idempotency-Key` от того же студента всегда возвращает тот же `attemptId` (даже если назначение потом отменили/истекло) — новый `Idempotency-Key` проходит все проверки заново. Дедлайн попытки (`deadlineAt`) вычисляется из `timeLimitMinutes` зафиксированной revision в момент старта.

### 4.3 Посмотреть вопросы и варианты

```bash
curl -s http://localhost:8080/api/v1/attempts/$ATTEMPT_ID/presentation \
  -H "Authorization: Bearer $TOKEN_STUDENT"
```

Возвращает всё необходимое для прохождения: текст вопросов и вариантов, их `id` (нужны для §4.4), `points`/`order`, уже сохранённые ваши ответы, а также `status`, `deadlineAt` и `serverTime`.

Вопросы берутся из **зафиксированной revision**, к которой привязана попытка, — если автор отредактирует тест, пока вы его проходите, ваша попытка не изменится.

Для обратного отсчёта используйте `serverTime` из ответа, а не часы клиента:

```text
осталось = deadlineAt - serverTime
```

**Правильные ответы в этом ответе не передаются** — ни явным флагом, ни каким-либо другим признаком. Это гарантия контракта, закреплённая тестом на самом теле HTTP-ответа (ADR-028).

### 4.3.1 Возобновить прерванную попытку

Если клиент потерял `attemptId` (перезагрузка страницы, смена устройства), не начинайте новую попытку — она израсходует лимит. Спросите активную:

```bash
curl -s http://localhost:8080/api/v1/assignments/$ASSIGNMENT_ID/attempts/active \
  -H "Authorization: Bearer $TOKEN_STUDENT"
```

`200` с тем же `AttemptPresentationView` — продолжайте; `404` — активной попытки нет, можно стартовать новую (§4.2).

### 4.4 Ответить на вопрос / снять ответ

```bash
curl -s -X PUT http://localhost:8080/api/v1/attempts/$ATTEMPT_ID/answers/$QUESTION_ID \
  -H "Authorization: Bearer $TOKEN_STUDENT" -H "Content-Type: application/json" \
  -d '{"optionIds": ["'"$OPTION_ID"'"]}'

curl -s -X DELETE http://localhost:8080/api/v1/attempts/$ATTEMPT_ID/answers/$QUESTION_ID \
  -H "Authorization: Bearer $TOKEN_STUDENT"
```

Для `SingleChoice` вопроса `optionIds` должен содержать **ровно один** ID (иначе `400`); для `MultipleChoice` — один или несколько. Ответ можно менять/снимать сколько угодно раз до `submit` или до дедлайна.

### 4.5 Посмотреть текущее состояние попытки

```bash
curl -s http://localhost:8080/api/v1/attempts/$ATTEMPT_ID \
  -H "Authorization: Bearer $TOKEN_STUDENT"
```

Возвращает статус, `startedAt`/`deadlineAt`/`completedAt` и список уже данных ответов (`questionId` + выбранные `optionId`) — без текста вопросов/вариантов и без `isCorrect` (student-safe).

### 4.6 Завершить попытку

```bash
IDEMPOTENCY_KEY=$(python3 -c "import uuid;print(uuid.uuid4())")
curl -s -X POST http://localhost:8080/api/v1/attempts/$ATTEMPT_ID/submit \
  -H "Authorization: Bearer $TOKEN_STUDENT" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY"
```

Если дедлайн уже прошёл на момент запроса (или отдельный фоновый воркер успел раньше), попытка фиксируется как `TimedOut` вместо `Submitted` — счёт всё равно считается по уже сохранённым ответам.

**Оценивание — "exact-set" без частичного зачёта:** балл за вопрос начисляется, только если выбранный набор вариантов **точно совпадает** с набором правильных — как для `SingleChoice`, так и для `MultipleChoice` (выбрать 2 из 3 правильных не даёт частичных баллов). `Percentage = Earned / Maximum * 100`, результат `Passed`, если `Percentage >= passingPercentage` теста.

### 4.7 Мой результат

```bash
curl -s http://localhost:8080/api/v1/attempts/$ATTEMPT_ID/result \
  -H "Authorization: Bearer $TOKEN_STUDENT"

curl -s "http://localhost:8080/api/v1/me/attempts?page=1&pageSize=20&status=Submitted" \
  -H "Authorization: Bearer $TOKEN_STUDENT"
```

`result` содержит `earned`/`maximum`/`percentage`/`outcome`, но **не** содержит, какие варианты были правильными (это доступно только `test-author`/`test-admin` через `/api/v1/results/{attemptId}`, см. §2.8/§3.5).

---

## 5. Типичные ошибки по ролям (диагностика)

| Ситуация | HTTP | Код | Кто чаще встречает |
|---|---:|---|---|
| Токен истёк/отсутствует | 401 | — | все |
| Author трогает чужой тест | 403/404 | `test.forbidden` / not found | test-author |
| Student пытается вызвать `tests:write`/`results:review` endpoint | 403 | — | student |
| Забыт `If-Match` при правке `Test` | 428 | `concurrency.precondition_required` | test-author/test-admin |
| `If-Match` устарел (кто-то другой уже поменял тест) | 412 | `concurrency.precondition_failed` | test-author/test-admin |
| Публикация теста без вопросов / без правильного варианта | 400/409 | `test.publish.questions_required`, `test.single_choice.correct_count`, `test.multiple_choice.correct_required` | test-author |
| Повтор запроса с тем же `Idempotency-Key`, но другим телом | 409 | `idempotency.key_reused` | все три роли |
| `availableUntil` раньше `availableFrom` в назначении | 400 | `assignment.window` | test-admin |
| `attemptLimit <= 0` | 400 | `assignment.attempt_limit` | test-admin |
| Старт попытки вне окна доступности/лимит исчерпан | 409/403 | — | student |
| Слишком много запросов подряд | 429 | — | все, особенно student (класс `student-write`) |

Полная таблица кодов и семантика — [API.md §5](API.md#5-problemdetails-и-status-codes).

## 6. Куда дальше

- Полный технический контракт (все endpoints, DTO, enum, edge cases) — [API.md](API.md).
- Готовый end-to-end сценарий для всех трёх ролей одним прогоном (Postman/Newman) — [tests/TestApp.Postman/README.md](../tests/TestApp.Postman/README.md).
- Что именно проверяется автотестами при каждом изменении — [TESTING.md](TESTING.md).
- Формальная модель домена (инварианты, value objects, lifecycle) — [DOMAIN_MODEL.md](DOMAIN_MODEL.md).
- Список открытых ограничений и план развития (включая student presentation API из §4.3) — [CURRENT_STATE.md](CURRENT_STATE.md) и [FEATURE_PLAN.md](FEATURE_PLAN.md).
