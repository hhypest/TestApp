# PostgreSQL backup / restore runbook

> Scope: TestApp PostgreSQL 18. Repository scripts дают portable logical baseline; platform snapshots, WAL archiving и PITR остаются deployment responsibility.

## Objectives

- RPO target: <= 24 hours;
- RTO target: <= 4 hours;
- backup сопровождается SHA-256;
- drill никогда не восстанавливает поверх source database.

Два recovery layers:

1. platform-native snapshots/WAL/PITR;
2. custom-format archive из `scripts/postgresql-backup.sh`.

## Backup

Variables:

```text
POSTGRES_PASSWORD  required
POSTGRES_HOST      default 127.0.0.1
POSTGRES_PORT      default 5432
POSTGRES_DATABASE  default testapp
POSTGRES_USER      default testapp
POSTGRES_IMAGE     default postgres:18
BACKUP_DIR         default backups
```

```bash
POSTGRES_PASSWORD=testapp \
  bash scripts/postgresql-backup.sh backups/testapp.dump
```

Script выполняет `pg_dump --format=custom --compress=9` с serializable deferrable snapshot, исключает deployment-specific owners/ACLs, проверяет archive через `pg_restore --list`, atomically завершает файл и пишет `.sha256` sidecar.

Production credentials и unencrypted backups не хранятся в repository.

## Restore verification

Variables:

```text
POSTGRES_ADMIN_PASSWORD   required
POSTGRES_ADMIN_USER       default testapp
POSTGRES_ADMIN_DATABASE   default postgres
POSTGRES_SOURCE_DATABASE  default testapp
POSTGRES_HOST             default 127.0.0.1
POSTGRES_PORT             default 5432
POSTGRES_IMAGE            default postgres:18
VERIFY_QUERY              optional scalar marker query
VERIFY_EXPECTED           expected result
```

```bash
POSTGRES_ADMIN_PASSWORD=testapp \
  bash scripts/postgresql-restore-verify.sh \
    backups/testapp.dump testapp_restore_verify
```

Script:

- запрещает source/admin target;
- проверяет checksum и archive;
- создаёт disposable database из `template0`;
- запускает `pg_restore --exit-on-error`;
- сравнивает public table count;
- сравнивает non-zero `__EFMigrationsHistory` count;
- выполняет optional marker query;
- удаляет target на exit.

Он завершает только sessions target database и не изменяет source.

## CI drill

`dotnet` workflow запускает production image `--migrate` против PostgreSQL 18, вставляет marker, создаёт archive, восстанавливает `testapp_restore_verify`, проверяет schema/history/marker и удаляет target.

Это проверяет repository tooling, но не platform snapshot/PITR.

## Production policy

Deployment owner задаёт schedule, encrypted offsite/immutable storage, retention, WAL/PITR при более строгом RPO, separated restore credentials, alerting и measured staging drills.

Suggested minimum:

```text
daily   14
weekly   8
monthly 12
```

Restore accepted только после checksum/archive/schema/history/marker checks, healthy API, representative business flow, Outbox/audit verification и записи actual RTO.

## Engine-cutover warning

Scripts принимают только PostgreSQL custom archives. MariaDB dump ими не восстанавливается и не конвертируется. Existing MariaDB data требует reviewed ETL/cutover из [PERSISTENCE.md](PERSISTENCE.md#engine-cutover-и-существующие-mariadb-данные). Старый backup/volume хранится как rollback point до acceptance и конца согласованного retention window.
