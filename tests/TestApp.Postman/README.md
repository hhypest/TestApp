# TestApp.Postman

Импортируемый Postman-проект для проверки HTTP API ветки `beta-ddd`. Коллекция построена по фактическим routes, DTO, ролям Keycloak и HTTP-контрактам текущего кода.

## Состав

- `TestApp.Api.postman_collection.json` — Collection v2.1 с последовательным end-to-end сценарием;
- `TestApp.Local.postman_environment.json` — environment полного локального Compose stack;
- `scripts/validate.mjs` — dependency-free проверка JSON, JavaScript snippets и покрытия route catalog.

Обычный collection run содержит более 70 запросов и проходит следующий сценарий:

```text
health/OpenAPI
  -> Keycloak tokens: author, admin, student
  -> create/edit/publish test
  -> single + bulk assignment
  -> start/answer/clear/submit + idempotent replay
  -> second attempt + admin timeout
  -> student-safe result + reviewer result
  -> audit/Outbox reads
  -> remove-operation sandbox + archive cleanup
```

Дополнительно проверяются `401`, `403`, `400`, `409`, `412`, `428`, ownership/role boundaries, strong `ETag`, обязательный `If-Match`, `Idempotency-Key`, request fingerprint и deprecated `/api` compatibility path.

## Запуск в Postman

1. Поднимите локальный development stack из корня репозитория:

   ```bash
   docker compose up --build -d api
   curl --fail http://localhost:8080/health/ready
   ```

2. В Postman выберите **Import** и импортируйте оба JSON-файла из этой директории.
3. Выберите environment **TestApp Local — beta-ddd**.
4. Откройте collection **TestApp API — beta-ddd** и запустите её через Collection Runner в исходном порядке.
5. Включите сохранение response bodies/console output, если нужно разбирать конкретный контракт.

Коллекция сама получает JWT через локальный Keycloak, извлекает `sub`, ID ресурсов и `ETag`, затем передаёт их следующим запросам. Копировать токены и GUID вручную не требуется.

## Newman

Тот же набор можно выполнить без GUI:

```bash
docker run --rm --network host \
  -v "$PWD/tests/TestApp.Postman:/etc/newman:ro" \
  postman/newman:6-alpine \
  run TestApp.Api.postman_collection.json \
  --environment TestApp.Local.postman_environment.json \
  --reporters cli
```

Команда рассчитана на Linux. Для Docker Desktop используйте адрес API/Keycloak, доступный из контейнера, через `--env-var baseUrl=...` и `--env-var keycloakUrl=...`, либо запускайте `newman`/Postman непосредственно на host.

## Проверка структуры проекта

```bash
node tests/TestApp.Postman/scripts/validate.mjs
```

Validator проверяет:

- корректность обоих JSON;
- синтаксис pre-request/test scripts;
- наличие test script у каждого запроса;
- все реализованные canonical routes, health и OpenAPI;
- три Keycloak token flow и обязательные environment variables.

## Данные и cleanup

Каждый run использует уникальный `runId` и idempotency keys. В конце оба созданных tests архивируются, а group assignment отменяется. Direct assignment и две завершённые attempts остаются в БД как проверяемая история; для полностью чистого local state выполните:

```bash
docker compose down -v
```

Эта команда удаляет local Compose volumes и все находящиеся в них данные.

## Эндпоинты dead-letter

На чистом стенде dead-letter сообщений обычно нет, поэтому destructive recovery requests не запускаются автоматически:

- detail выполняется только при наличии `deadLetterEventId`;
- requeue требует явного `deadLetterRequeueEventId`;
- discard требует отдельного явного `deadLetterDiscardEventId`.

`deadLetterEventId` автоматически заполняется первым элементом `recentDeadLetters`, если он есть. Requeue и discard намеренно требуют ручного opt-in, потому что это взаимоисключающие operational действия.

## Локальные учётные данные

Environment использует fixture-учётные данные из `deploy/keycloak/testapp-realm.json`:

| User | Password | Role/group |
|---|---|---|
| `author` | `author` | `test-author` |
| `admin` | `admin` | `test-admin` |
| `student` | `student` | group `students` |

Они предназначены только для локальной разработки. Для другого стенда создайте отдельный Postman environment и не коммитьте реальные пароли или access tokens.
