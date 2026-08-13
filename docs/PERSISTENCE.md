# Persistence и MariaDB

> Статус: **Implemented persistence contract**. Runtime/CI baseline — MariaDB 12.3.

## 1. Stack

- MariaDB 12.3;
- EF Core 9.0.18;
- Pomelo.EntityFrameworkCore.MySql 9.0.0;
- MySqlConnector через Pomelo/runtime infrastructure;
- `ServerVersion.AutoDetect(connectionString)` в production composition root;
- test baseline `MariaDbServerVersion(12, 3, 0)`.

Приложение таргетирует `net10.0`; EF provider сознательно остаётся на совместимой стабильной линии EF Core 9/Pomelo 9.

## 2. Source of truth

Основной transactional store — MariaDB.

В MariaDB находятся:

- mutable test definitions;
- published revision snapshots;
- assignments;
- attempts/responses;
- idempotency records;
- Outbox;
- audit trail.

Отдельной read database/caching layer сейчас нет.

## 3. Database schema

### 3.1 `tests`

| Column | Тип/semantics |
|---|---|
| `Id` | char(36), PK |
| `OwnerId` | varchar(256), immutable external owner |
| `Title` | varchar(300) |
| `Status` | int enum |
| `ConcurrencyVersion` | bigint concurrency token |
| `passing_percentage` | decimal(5,2), default 70 |
| `time_limit_minutes` | nullable int |

Relations:

- `questions` owned collection через FK `TestId`.

Index:

```text
(OwnerId, Status)
```

### 3.2 `questions`

| Column | Semantics |
|---|---|
| `Id` | QuestionId PK |
| `TestId` | owner test |
| `Text` | varchar(2000) |
| `Type` | int QuestionType |
| `Points` | decimal(18,2) |
| `Order` | application/domain order |

FK `questions -> tests` с cascade delete.

### 3.3 `answer_options`

| Column | Semantics |
|---|---|
| `Id` | AnswerOptionId PK |
| `QuestionId` | owner question |
| `Text` | varchar(2000) |
| `IsCorrect` | boolean/tinyint |
| `Order` | domain order |

FK к `questions`, cascade delete.

### 3.4 `published_test_revisions`

| Column | Semantics |
|---|---|
| `Id` | PublishedTestRevisionId PK |
| `TestId` | source TestId |
| `Version` | revision number |
| `Title` | title snapshot |
| `PassingPercentage` | decimal(5,2) snapshot |
| `TimeLimitMinutes` | nullable settings snapshot |
| `PublishedAt` | datetime(6) |
| `questions_json` | longtext immutable snapshot |
| `ConcurrencyVersion` | aggregate token |

Unique index:

```text
(TestId, Version)
```

### Почему questions snapshot — JSON

Published revision — immutable aggregate snapshot. Его questions/options не редактируются отдельно и всегда читаются как единая revision model. JSON исключает риск изменения historical correctness через mutable authoring tables.

Текущая trade-off:

- удобно для immutable snapshot;
- сложнее SQL analytics по отдельным historical questions;
- для heavy analytics в будущем предпочтительно построить отдельную projection/warehouse, а не ломать immutable transactional model.

### 3.5 `test_assignments`

| Column | Semantics |
|---|---|
| `Id` | TestAssignmentId PK |
| `RevisionId` | immutable revision reference |
| `TargetType` | User/Group enum |
| `TargetId` | varchar(256) external identity |
| `AssignedBy` | external user |
| `AssignedAt` | datetime(6) |
| `AvailableFrom` | datetime(6) |
| `AvailableUntil` | nullable datetime(6) |
| `AttemptLimit` | nullable int |
| `Status` | Active/Cancelled |
| cancellation fields | actor/time/reason |
| `ConcurrencyVersion` | optimistic token |

Indexes:

```text
(TargetType, TargetId, Status)
RevisionId
```

Assignment target специально нормализован в `TargetType + TargetId`, чтобы direct-user/group filtering выполнялся в SQL и индексировался.

### 3.6 `test_attempts`

| Column | Semantics |
|---|---|
| `Id` | TestAttemptId PK |
| `AssignmentId` | assignment |
| `RevisionId` | immutable revision |
| `UserId` | external user |
| `StartRequestId` | start idempotency key |
| `Status` | InProgress/Submitted/TimedOut |
| `StartedAt` | datetime(6) |
| `DeadlineAt` | nullable datetime(6) |
| `CompletedAt` | nullable datetime(6) |
| `score_earned` | nullable decimal(18,2) |
| `score_maximum` | nullable decimal(18,2) |
| `Outcome` | nullable Passed/Failed |
| `ConcurrencyVersion` | optimistic token |

Indexes:

```text
(AssignmentId, UserId)
UNIQUE (AssignmentId, UserId, StartRequestId)
(RevisionId, Status, Outcome)
```

Unique start key обеспечивает retry-safe start attempt. Application выполняет ранний actor-scoped ID lookup для стабильного replay после изменения assignment state; repository повторяет тот же lookup внутри serializable transaction перед проверкой attempt limit и insert.

### 3.7 `question_responses`

Composite PK:

```text
(TestAttemptId, QuestionId)
```

Хранит `AnsweredAt`.

### 3.8 `selected_answer_options`

Composite PK:

```text
(TestAttemptId, QuestionId, OptionId)
```

FK к `question_responses` с cascade delete.

Correctness здесь не хранится: оно берётся из immutable revision snapshot.

### 3.9 `idempotency_records`

| Column | Semantics |
|---|---|
| `Id` | Guid PK |
| `Operation` | varchar(200) |
| `ActorId` | varchar(256) |
| `RequestId` | Guid |
| `ResultType` | varchar(512) |
| `ResultJson` | serialized result |
| `CreatedAt` | datetime(6) |
| `RequestFingerprint` | nullable varchar(64), SHA-256 canonical request hash |

Unique:

```text
(Operation, ActorId, RequestId)
```

### 3.10 `outbox_messages`

Основные поля:

- `Id` = integration `EventId`;
- `OccurredAt`;
- `Type`;
- `Payload`;
- `ProcessedAt`;
- `Error`;
- `AttemptCount`;
- `LastAttemptAt`;
- `NextAttemptAt`;
- `DeadLetteredAt`;
- `DiscardedAt`.

Queue index:

```text
(ProcessedAt, DeadLetteredAt, DiscardedAt, NextAttemptAt, OccurredAt)
```

### 3.11 `outbox_dead_letter_actions`

Immutable operational audit для explicit `requeue`/`discard`:

- action ID;
- event ID;
- action enum;
- actor ID;
- mandatory reason;
- occurred time;
- correlation ID.

Indexes: `(EventId, OccurredAt)` и `OccurredAt`.

### 3.12 `audit_entries`

Хранит:

- ID;
- occurred time;
- actor ID;
- HTTP method;
- route;
- status code;
- correlation ID;
- trace ID;
- duration ms.

Indexes:

```text
OccurredAt
(ActorId, OccurredAt)
(StatusCode, OccurredAt)
```

Request/response body не хранится.

## 4. Migrations

Текущая migration chain:

```text
20260812110000_MariaDbBaseline
20260812115000_OutboxDeliveryState
20260812124500_AuditTrail
20260812133000_TestOwnership
20260812140000_IdempotencyFingerprint
20260812150000_OperationalRetentionIndexes
20260812151000_OutboxDeadLetterManagement
```

`MariaDbBaseline` — новая baseline история после отказа от SQLite development history.

### 4.1 Migration-only mode

```bash
dotnet TestApp.Api.dll --migrate
```

Flow:

1. composition root создаёт app/service provider;
2. `Database.MigrateAsync()`;
3. process завершается до HTTP listener.

Текущий composition root не загружает Keycloak/transport security в migrate-only mode, но всё ещё валидирует RabbitMQ/worker/CORS/rate-limit/OpenAPI/proxy options. Целевой production migration job должен требовать только database configuration; это known stabilization gap.

### 4.2 Startup migrations

Текущее правило:

```text
Database:ApplyMigrationsOnStartup
```

Если настройка отсутствует:

- Development -> true;
- Production -> false.

Production deployment должен запускать отдельный migration job перед replicas.

### 4.3 Требования к новым migrations

Новая migration должна:

- быть MariaDB-compatible;
- проходить на пустой database;
- проходить migration-only CI из production image;
- иметь явный `[DbContext]` и `[Migration]` metadata;
- учитывать existing-data upgrade path;
- не предполагать SQLite semantics;
- при изменении critical indexes иметь regression integration test.

### Технический долг

Полный стандартный EF `AppDbContextModelSnapshot` workflow пока не зафиксирован. До его внедрения migration files являются explicit source of truth и должны ревьюиться вручную вместе с EF mappings.

## 5. Optimistic concurrency

EF config:

```text
ConcurrencyVersion.IsConcurrencyToken()
```

Используется на:

- Test;
- PublishedTestRevision;
- TestAssignment;
- TestAttempt.

`Touch()` увеличивает version на domain mutation.

Conflict path:

```text
UPDATE ... WHERE ConcurrencyVersion = old
0 rows affected
-> DbUpdateConcurrencyException
-> ConcurrencyConflictException
-> HTTP 409
```

### Что обязан делать клиент после 409

- перечитать current representation;
- повторно применить intent пользователя;
- не делать blind infinite retry.

## 6. Attempt-limit concurrency

Attempt limit нельзя корректно защитить только `CountAsync + Insert` без transaction.

Repository использует serializable transaction и проверяет:

1. существующий attempt с тем же `(AssignmentId, UserId, StartRequestId)` после раннего handler-level replay lookup;
2. текущий count для `(AssignmentId, UserId)`;
3. limit;
4. insert.

Unique start index является дополнительной защитой retry race.

## 7. Distributed idempotency locks

`IdempotencyStore.AcquireAsync()` открывает отдельное MariaDB connection и использует:

```sql
SELECT GET_LOCK(@name, 30);
SELECT RELEASE_LOCK(@name);
```

Lock key — SHA-256 от:

```text
database + operation + actorId + requestId
```

Преимущества:

- работает между несколькими API instances;
- lock lifetime привязан к dedicated DB connection;
- serialized cache re-check после lease устраняет concurrent cache miss race.

Ограничение: MariaDB становится coordination dependency для этих unsafe operations, что приемлемо, потому что она уже transactional source of truth.

## 8. Read-side SQL policy

Read queries должны:

- использовать `AsNoTracking()`;
- применять filters до `ToArray/ToList`;
- считать `TotalCount` SQL-side;
- применять `OrderBy/Skip/Take` SQL-side;
- не materialize immutable `questions_json`, если list-view этого не требует;
- вычисляемые non-translatable properties (например score percentage) рассчитывать после materialization из persisted scalar fields.

Текущий долг:

- часть paged queries сортирует только по timestamp без deterministic ID tie-breaker;
- reviewer/admin queries сначала materialize matching revision IDs;
- assignment statistics materialize score rows в process memory.

До 1.0 эти hot paths должны перейти на stable ordering и SQL joins/aggregates с high-cardinality regression evidence.

## 9. Backup/restore — implemented repository baseline

Реализованы:

- compressed logical MariaDB dump;
- SHA-256 sidecar и gzip validation;
- restore только в isolated target database;
- table-count, EF migration history и optional business-marker verification;
- CI recovery drill после production-image migration;
- baseline RPO <= 24h и RTO <= 4h;
- portable retention guidance.

До production 1.0 deployment owner должен выполнить staging/platform restore drill с measured RTO, настроить schedule/encrypted offsite storage и при более строгом RPO включить provider-native snapshot/binlog/PITR. Подробно: `BACKUP_RESTORE.md`.

## 10. MariaDB version policy

Baseline: **12.3+ в пределах согласованной LTS/compatible line**.

CI выполняет `SELECT VERSION()` regression check и падает при runtime ниже 12.3.

При обновлении major/minor database version требуется полный gate:

```text
empty migrations
persistence tests
concurrency tests
idempotency/advisory locks
Outbox tests
API E2E
production image --migrate
```

Не следует обновлять existing production data volume только заменой Docker tag без отдельного tested upgrade/backup plan.
