# Persistence и PostgreSQL

> Статус: **implemented persistence contract**. Runtime/CI baseline — PostgreSQL 18.

## Stack

- PostgreSQL 18;
- EF Core 9.0.18;
- `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.4;
- Npgsql 9.0.4;
- `UseNpgsql(connectionString)`;
- application target — `net10.0`.

EF/Npgsql остаются на compatible stable major line 9. Upgrade до EF/Npgsql 10 должен быть отдельным compatibility change.

Development connection:

```text
Host=localhost;Port=5432;Database=testapp;Username=testapp;Password=testapp;
```

Вне Development `ConnectionStrings:Database` обязателен; production credentials не хранятся в repository.

## Source of truth

PostgreSQL — единственный transactional store для mutable tests, immutable revisions, assignments, attempts/responses, idempotency records, Outbox, dead-letter actions и HTTP audit. Отдельной read database/cache layer сейчас нет.

## Provider mappings

| .NET/domain value | PostgreSQL |
|---|---|
| `Guid` и typed IDs | `uuid` |
| `DateTimeOffset` | `timestamp with time zone` |
| `bool` | `boolean` |
| score/percentage | `numeric(precision, scale)` |
| bounded strings | `character varying(n)` |
| payload/result/error | `text` |
| published questions snapshot | `jsonb` |
| enums | `integer` |
| concurrency version | `bigint` |

Имена таблиц lower snake case. Property-derived column names сохраняют EF casing, поэтому raw PostgreSQL SQL должен quote такие identifiers.

PostgreSQL `timestamptz` хранит instant, а не исходный timezone. Все сохраняемые `DateTimeOffset` канонизируются через `ToUniversalTime()`; API может принять эквивалентный ISO-8601 offset, но persisted/read representation имеет `+00:00`.

## Schema invariants

### Tests и revisions

`tests` хранит owner, title, status, settings и `ConcurrencyVersion`. Index: `(OwnerId, Status)`. Owned `questions` и `answer_options` связаны cascade FK.

`published_test_revisions` использует unique `(TestId, Version)` и `questions_json jsonb`. Snapshot immutable; historical question analytics следует строить отдельной projection, не изменяя transactional revision.

### Assignments и attempts

Assignment indexes:

```text
(TargetType, TargetId, Status)
RevisionId
```

Attempt indexes:

```text
(AssignmentId, UserId)
UNIQUE (AssignmentId, UserId, StartRequestId)
(RevisionId, Status, Outcome)
```

Unique start key обеспечивает retry-safe start; attempt limit дополнительно защищён PostgreSQL advisory lease на assignment/user.

`question_responses` имеет PK `(TestAttemptId, Id)`. `selected_answer_options` имеет PK `(TestAttemptId, QuestionId, OptionId)` и cascade FK к response.

### Idempotency, Outbox и audit

`idempotency_records`:

- unique `(Operation, ActorId, RequestId)`;
- optional SHA-256 request fingerprint;
- serialized result;
- `CreatedAt` retention index.

`outbox_messages` хранит delivery state; queue index покрывает:

```text
(ProcessedAt, DeadLetteredAt, DiscardedAt, NextAttemptAt, OccurredAt)
```

`outbox_dead_letter_actions` — immutable requeue/discard audit. `audit_entries` не хранит request/response bodies и индексируется по occurred time, actor/time и status/time.

## Migrations

После смены engine история rebased на PostgreSQL baseline:

```text
20260813183117_PostgreSqlBaseline
```

Baseline сгенерирован из EF-модели. В repository теперь есть `AppDbContextModelSnapshot`, designer metadata и `AppDbContextDesignFactory`.

Следующая migration:

```bash
dotnet tool install --global dotnet-ef --version 9.0.18
dotnet ef migrations add <Name> \
  --project src/TestApp.Infrastructure \
  --startup-project src/TestApp.Infrastructure \
  --context AppDbContext \
  --output-dir Persistence/Migrations
```

`TESTAPP_DESIGN_CONNECTION` переопределяет design connection; generation не требует доступной database.

Production migration-only mode:

```bash
dotnet TestApp.Api.dll --migrate
```

Production запускает отдельный migration/init job до replicas. `Database:ApplyMigrationsOnStartup=true` разрешён только в Development.

Новая migration должна проходить empty-database и production-image gates, соответствовать snapshot и иметь existing-data path для уже развернутой PostgreSQL schema.

## Engine cutover и существующие MariaDB данные

PostgreSQL baseline предназначен для новой PostgreSQL database. MariaDB migration history/dump нельзя применить напрямую: engine, SQL dialect и physical types различаются.

Repository не удаляет старые MariaDB volumes и не выполняет скрытый data conversion. Compose создаёт новый `testapp-postgres` volume.

Если значимые MariaDB данные существуют:

1. создать и проверить MariaDB backup;
2. остановить writes или зафиксировать согласованный snapshot;
3. применить baseline к пустой PostgreSQL 18 database;
4. выполнить explicit ETL, включая `char(36) -> uuid`, UTC timestamps и revision JSON;
5. сохранить FK order и immutable revision/attempt relationships;
6. сравнить row counts, unique constraints и business invariants;
7. выполнить create/publish/assign/start/submit smoke flow;
8. переключить connection string после acceptance;
9. удерживать MariaDB backup/volume как rollback point до конца retention window.

ETL зависит от реального deployment/data volume и не подменяется PostgreSQL backup scripts.

## Optimistic и attempt-limit concurrency

`ConcurrencyVersion` — EF concurrency token для Test, PublishedTestRevision, TestAssignment и TestAttempt:

```text
UPDATE ... WHERE ConcurrencyVersion = old
0 rows -> DbUpdateConcurrencyException
-> ConcurrencyConflictException -> HTTP 409
```

После 409 клиент перечитывает state и повторно применяет intent.

Attempt repository берёт session-level advisory lease на `(AssignmentId, UserId)`, затем в короткой transaction проверяет replay key, current count, limit и выполняет insert. Unique start index остаётся дополнительной race protection. Lease устраняет PostgreSQL SSI abort storm при одновременных стартах и действует между API replicas.

## Distributed advisory locks

Idempotency, Outbox и retention используют единый helper с session-level PostgreSQL locks:

```sql
SELECT pg_try_advisory_lock(@key);
SELECT pg_advisory_unlock(@key);
```

Resource material включает database и namespace. SHA-256 детерминированно сокращается до signed 64-bit key. Dedicated Npgsql connection определяет lifetime; explicit unlock выполняется при dispose, а close остаётся safety net.

- idempotency timeout — 30 секунд;
- Outbox timeout — configured bounded value;
- retention — immediate try, занятый цикл пропускается.

Так replicas получают общую coordination semantics без Redis. Теоретическая 64-bit hash collision приводит к лишней сериализации, не к concurrent critical section.

## Read-side policy

Queries используют `AsNoTracking`, SQL-side filtering/count/order/paging, deterministic tie-breakers и не materialize `questions_json` без необходимости. High-cardinality paths требуют query-plan/performance evidence.

## Backup/restore

Repository baseline:

- custom-format archive `pg_dump -Fc`;
- SHA-256 и `pg_restore --list` validation;
- restore в disposable target;
- table/migration counts и optional marker;
- CI recovery drill после production-image migration.

Targets: RPO <= 24h, RTO <= 4h. Snapshots/WAL/PITR, encryption, offsite retention и measured staging restore — deployment responsibility. См. [BACKUP_RESTORE.md](BACKUP_RESTORE.md).

## PostgreSQL version policy

Baseline — PostgreSQL 18 stable. CI выполняет `SHOW server_version_num` и требует `>= 180000`. Перед major upgrade обязательны verified backup, isolated restore/upgrade, migration/integration/concurrency/lock/Outbox suites, production-image `--migrate` и performance evidence. Смена Docker tag не является production upgrade process.
