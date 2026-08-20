# Persistence и PostgreSQL

> Статус: **implemented persistence contract**. Runtime/CI baseline — PostgreSQL 18.

## Stack

- PostgreSQL 18;
- EF Core 10.0.11;
- `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3;
- Npgsql 10.0.3;
- `UseNpgsql(connectionString)`;
- целевая платформа приложения — `net10.0`.

EF/Npgsql находятся на stable major line 10, соответствующей application target `net10.0`. Все Microsoft EF Core packages и local `dotnet-ef` используют одинаковую patch-версию; prerelease line 11 не входит в runtime baseline. Следующий major upgrade должен выполняться отдельным compatibility change.

Строка подключения для разработки:

```text
Host=localhost;Port=5432;Database=testapp;Username=testapp;Password=testapp;
```

Вне Development `ConnectionStrings:Database` обязателен; production credentials не хранятся в repository.

## Источник истины

PostgreSQL — единственный transactional store для mutable tests, immutable revisions, assignments, attempts/responses, idempotency records, Outbox, dead-letter actions и HTTP audit. Отдельной read database/cache layer сейчас нет.

## Сопоставление типов провайдера

| Значение .NET/домена | PostgreSQL |
|---|---|
| `Guid` и typed IDs | `uuid` |
| `DateTimeOffset` | `timestamp with time zone` |
| `bool` | `boolean` |
| score/percentage | `numeric(precision, scale)` |
| строки с ограничением длины | `character varying(n)` |
| payload/result/error | `text` |
| снимок опубликованных вопросов | `jsonb` |
| enums | `integer` |
| версия параллельного доступа | `bigint` |

Имена таблиц lower snake case. Property-derived column names сохраняют EF casing, поэтому raw PostgreSQL SQL должен quote такие identifiers.

PostgreSQL `timestamptz` хранит instant, а не исходный timezone. Все сохраняемые `DateTimeOffset` канонизируются через `ToUniversalTime()`; API может принять эквивалентный ISO-8601 offset, но persisted/read representation имеет `+00:00`.

## Инварианты схемы

### Tests и revisions

`tests` хранит owner, title, status, settings и `ConcurrencyVersion`. Index: `(OwnerId, Status)`. Owned `questions` и `answer_options` связаны cascade FK.

`published_test_revisions` использует unique `(TestId, Version)` и `questions_json jsonb`. Snapshot immutable; historical question analytics следует строить отдельной projection, не изменяя transactional revision.

### Assignments и attempts

Индексы назначений:

```text
(TargetType, TargetId, Status)
RevisionId
```

Индексы попыток:

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
- необязательный SHA-256 отпечаток запроса;
- сериализованный результат;
- индекс `CreatedAt` для очистки по сроку хранения.

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
dotnet tool restore
dotnet ef migrations add <Name> \
  --project src/TestApp.Infrastructure \
  --startup-project src/TestApp.Infrastructure \
  --context AppDbContext \
  --output-dir Persistence/Migrations
```

Версия `dotnet-ef` 10.0.11 зафиксирована в repository tool manifest. `TESTAPP_DESIGN_CONNECTION` переопределяет design connection; generation не требует доступной database.

Переход EF/Npgsql 9 -> 10 не меняет текущую relational model, поэтому отдельная schema migration не создаётся. Baseline migration и её designer metadata сохраняют версию инструмента, которой они были сгенерированы; CI на EF Core 10 отдельно выполняет `migrations has-pending-model-changes`, а затем применяет baseline к пустой PostgreSQL 18 database из production image.

Production-режим только миграций:

```bash
dotnet TestApp.Api.dll --migrate
```

Production запускает отдельный migration/init job до replicas. `Database:ApplyMigrationsOnStartup=true` разрешён только в Development.

Новая migration должна проходить empty-database и production-image gates, соответствовать snapshot и иметь existing-data path для уже развернутой PostgreSQL schema.

### Что проверяется в CI

`scripts/verify-migration-job.sh` (шаг `Verify the migration job from the production image` в workflow `dotnet`) проверяет задание целиком, а не только успешность одного прогона:

1. **применение на чистой базе** — и то, что в `__EFMigrationsHistory` оказалось ровно столько миграций, сколько лежит в репозитории. Расхождение означает, что схема и код разошлись, и это состояние обязано быть красным до выпуска, а не после;
2. **идемпотентность повторного прогона** — release job перезапускают после сетевого сбоя, и «применилось второй раз» было бы порчей схемы в момент выпуска. Сравнивается отпечаток схемы до и после, а не код возврата;
3. **отказ автоприменения на старте вне Development** — проверяется поведение процесса, а не строка в исходнике: успешный старт означал бы, что обычный перезапуск API способен менять схему production-базы.

Отпечаток снимается `pg_dump --schema-only` с отфильтрованными строками `\restrict`/`\unrestrict`: PostgreSQL 18 вставляет в них случайный nonce, поэтому побайтовое сравнение дампов нестабильно само по себе и дало бы мигающую проверку вместо содержательной.

Переменная `MIGRATE_CMD` подменяет команду запуска задания целиком — тем же скриптом задание проверяется против образа контура.

### Политика изменений схемы начиная с 1.0

До 1.0 история схлопывалась: `PostgreSqlBaseline` — единственная миграция, предыдущей поддерживаемой схемы не существует. С 1.0 это перестаёт быть допустимым, потому что появляется развёрнутая база, которую нельзя пересоздать.

- каждое изменение схемы поставляется **аддитивной** миграцией: добавление колонки/таблицы/индекса, но не переименование и не удаление в том же выпуске. Удаление становится отдельной миграцией следующего выпуска, когда ни одна работающая версия кода больше не читает удаляемое;
- у каждой миграции документируется откат: либо обратная миграция, либо явная запись «откат невозможен, откатывается только код». Второе допустимо для аддитивных изменений — неиспользуемая колонка не мешает старому коду;
- миграция, не выдерживающая параллельной работы двух версий кода (старой и новой), к выпуску не допускается: развёртывание не атомарно, и в момент подмены образа обе версии работают одновременно;
- история не схлопывается. С 1.1 «предыдущая поддерживаемая схема» становится содержательным понятием, и прогон миграции против дампа предыдущего выпуска входит в гейт.

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

## Распределённые advisory-блокировки

Idempotency, Outbox и retention используют единый helper с session-level PostgreSQL locks:

```sql
SELECT pg_try_advisory_lock(@key);
SELECT pg_advisory_unlock(@key);
```

Resource material включает database и namespace. SHA-256 детерминированно сокращается до signed 64-bit key. Dedicated Npgsql connection определяет lifetime; explicit unlock выполняется при dispose, а close остаётся safety net.

- idempotency timeout — 30 секунд;
- таймаут Outbox — настраиваемое ограниченное значение;
- retention — immediate try, занятый цикл пропускается.

Так replicas получают общую coordination semantics без Redis. Теоретическая 64-bit hash collision приводит к лишней сериализации, не к concurrent critical section.

## Политика стороны чтения

Queries используют `AsNoTracking`, SQL-side filtering/count/order/paging, deterministic tie-breakers и не materialize `questions_json` без необходимости. High-cardinality paths требуют query-plan/performance evidence.

## Backup/restore

Baseline репозитория:

- архив в custom-формате `pg_dump -Fc`;
- SHA-256 и `pg_restore --list` validation;
- restore в disposable target;
- table/migration counts и optional marker;
- CI recovery drill после production-image migration.

Targets: RPO <= 24h, RTO <= 4h. Snapshots/WAL/PITR, encryption, offsite retention и measured staging restore — deployment responsibility. См. [BACKUP_RESTORE.md](BACKUP_RESTORE.md).

## Политика версий PostgreSQL

Baseline — PostgreSQL 18 stable. CI выполняет `SHOW server_version_num` и требует `>= 180000`. Перед major upgrade обязательны verified backup, isolated restore/upgrade, migration/integration/concurrency/lock/Outbox suites, production-image `--migrate` и performance evidence. Смена Docker tag не является production upgrade process.
