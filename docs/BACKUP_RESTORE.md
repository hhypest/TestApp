# Резервное копирование и восстановление PostgreSQL

> Область: PostgreSQL 18 в TestApp. Скрипты репозитория дают переносимый логический baseline; снимки платформы, архивирование WAL и PITR остаются ответственностью развёртывания.

## Цели

- целевой RPO: <= 24 часов;
- целевой RTO: <= 4 часов;
- резервная копия сопровождается контрольной суммой SHA-256;
- учебное восстановление никогда не выполняется поверх исходной базы.

Два уровня восстановления:

1. штатные для платформы снимки/WAL/PITR;
2. архив в custom-формате из `scripts/postgresql-backup.sh`.

## Резервное копирование

Переменные:

```text
POSTGRES_PASSWORD  обязательна
POSTGRES_HOST      по умолчанию 127.0.0.1
POSTGRES_PORT      по умолчанию 5432
POSTGRES_DATABASE  по умолчанию testapp
POSTGRES_USER      по умолчанию testapp
POSTGRES_IMAGE     по умолчанию postgres:18
BACKUP_DIR         по умолчанию backups
```

```bash
POSTGRES_PASSWORD=testapp \
  bash scripts/postgresql-backup.sh backups/testapp.dump
```

Скрипт выполняет `pg_dump --format=custom --compress=9` с serializable deferrable снимком, исключает специфичных для развёртывания владельцев и ACL, проверяет архив через `pg_restore --list`, атомарно завершает файл и пишет sidecar-файл `.sha256`.

Production-учётные данные и незашифрованные резервные копии в репозитории не хранятся.

## Проверка восстановления

Переменные:

```text
POSTGRES_ADMIN_PASSWORD   обязательна
POSTGRES_ADMIN_USER       по умолчанию testapp
POSTGRES_ADMIN_DATABASE   по умолчанию postgres
POSTGRES_SOURCE_DATABASE  по умолчанию testapp
POSTGRES_HOST             по умолчанию 127.0.0.1
POSTGRES_PORT             по умолчанию 5432
POSTGRES_IMAGE            по умолчанию postgres:18
VERIFY_QUERY              необязательный скалярный запрос-маркер
VERIFY_EXPECTED           ожидаемый результат
RESTORE_EVIDENCE_PATH     необязательный путь для JSON evidence
RESTORE_EXPECTED_TABLE_COUNT      ожидаемое число таблиц без чтения source database
RESTORE_EXPECTED_MIGRATION_COUNT  ожидаемое число миграций без чтения source database
RESTORE_PROMOTE_TARGET            0 по умолчанию; 1 только для чистого recovery instance
```

```bash
POSTGRES_ADMIN_PASSWORD=testapp \
RESTORE_EVIDENCE_PATH=artifacts/restore/staging-database-restore.json \
  bash scripts/postgresql-restore-verify.sh \
    backups/testapp.dump testapp_restore_verify
```

Скрипт:

- запрещает исходную и административную базу в качестве цели;
- проверяет контрольную сумму и архив;
- создаёт одноразовую базу из `template0`;
- запускает `pg_restore --exit-on-error`;
- сравнивает количество таблиц в схеме public;
- сравнивает ненулевое количество записей `__EFMigrationsHistory`;
- выполняет необязательный запрос-маркер;
- в обычном режиме удаляет целевую базу до публикации успешного evidence;
- атомарно записывает JSON с SHA-256/размером архива, числом таблиц и миграций,
  монотонным временем restore и проверки; файл создаётся с режимом `0600`.

Он завершает только сессии целевой базы и не изменяет исходную.

`RESTORE_PROMOTE_TARGET=1` — узкий внутренний контракт application drill. Он требует
одновременно expected counts и evidence path, дважды подтверждает отсутствие source database,
а затем переименовывает проверенный temporary target в исходное имя. Запускать этот режим
против live/shared PostgreSQL запрещено: `staging_restore_drill.py` направляет его только в
созданный с нуля одноразовый recovery instance, а после проверки удаляет весь volume.

Evidence имеет `scope: database-restore-verified`. Это намеренно **не полный RTO сервиса**:
`databaseRecoverySeconds` заканчивается после восстановления и DB-level проверок. Для закрытия
issue #18 используется отдельный application-level drill ниже. Самостоятельный запуск этого
скрипта не выдаёт нижнюю границу за production RTO.

Запись fail-closed: при несовпадении маркера, ошибке restore или невозможности удалить
одноразовую базу новый JSON не публикуется, а существующий evidence не перезаписывается.
Путь evidence не может совпадать с архивом или его `.sha256`.

## Staging application restore drill (`#18`)

`scripts/staging_restore_drill.py` оркестрирует полный проверяемый путь на машине staging:

1. проверяет доступность live-контура, сверяет его `/api/v1/operations/version` с
   `TESTAPP_IMAGE_DIGEST` и **только читает** его PostgreSQL;
2. сверяет заранее зафиксированные нижние границы размера/числа тестов/idempotency records,
   пустой Outbox и отсутствие advisory locks;
3. снимает новый custom-format backup существующим `postgresql-backup.sh`;
4. поднимает отдельные PostgreSQL и RabbitMQ с одноразовыми project volumes;
5. восстанавливает backup в чистый PostgreSQL существующим
   `postgresql-restore-verify.sh` и запускает API из того же immutable image digest, что live;
6. ждёт `/health/ready`, читает засеянный бизнес-набор, проверяет Outbox, audit и replay
   существующей операции публикации с тем же `Idempotency-Key`;
7. удаляет recovery containers **вместе с volumes** и только после этого атомарно публикует
   приватный JSON evidence (`0600`).

Live PostgreSQL не подключается к recovery services и не получает ни одного write-запроса.
Внешним остаётся только staging Keycloak: recovery API входит в staging-сеть ради того же
OIDC issuer, но наружу публикуется исключительно loopback-порт API. Promotion-режим низкого
уровня требует отсутствия source database в чистом экземпляре и недоступен без ожидаемых
чисел таблиц/миграций и отдельного evidence path.

Перед первым прогоном контур должен быть засеян по `docs/PERFORMANCE.md`; значения
`RTO_MIN_*` фиксируются **до** восстановления по отчёту засева. Это защита от правдоподобного
«успеха» на пустом или случайно урезанном backup. Точная read-only команда получения
baseline приведена в `docs/OPERATIONS.md` §16.0.5. Образ в `.env` обязан быть digest-ссылкой.

```bash
set -a
. ./.env
set +a

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
export CONFIRM_STAGING_RESTORE_DRILL=isolated-recovery

python3 scripts/staging_restore_drill.py \
  --backup "/var/backups/testapp/rto-${stamp}.dump" \
  --evidence /var/backups/testapp/staging-rto-latest.json \
  --check

python3 scripts/staging_restore_drill.py \
  --backup "/var/backups/testapp/rto-${stamp}.dump" \
  --evidence /var/backups/testapp/staging-rto-latest.json
```

`--check` проверяет только конфигурацию и не обращается к Docker/сети. Реальный запуск
fail-closed: частичный `compose up`, ошибка restore, неверный бизнес-результат, непустой
Outbox, оставшийся advisory lock, превышение цели или невозможность удалить volumes не
перезаписывают предыдущее успешное evidence. `RTO_TIMEOUT_SECONDS` не может быть меньше
целевого RTO, чтобы ранний timeout не подменил фактическое измерение. Backup и checksum
создаются приватными.

В evidence:

- `backup.observedRpoSeconds` — консервативная верхняя граница возраста согласованного
  снимка к моменту начала recovery (`recoveryStartedAt - backupStartedAt`; фактический
  snapshot `pg_dump` берёт уже после старта); она проверяет этот restore point, но не
  доказывает расписание, offsite-доставку или WAL/PITR;
- `recovery.totalRtoSeconds` — монотонное время от старта чистого recovery-контура до
  первого успешного показательного бизнес-запроса, включая PostgreSQL restore и API ready;
- `verification` — idempotency replay, Outbox, audit, advisory locks и подтверждение cleanup;
- фактические значения сравниваются с целями RPO 86 400 с / RTO 14 400 с.

JSON не содержит токены или credentials, но содержит операционные метаданные и не должен
коммититься. Issue `#18` остаётся открытым до реального прогона на staging-объёме: к нему
прикладывают evidence, digest образа, UTC-время, описание машины и ссылку на baseline засева.

## Учебное восстановление в CI

Workflow `dotnet` запускает production-образ в режиме `--migrate` против PostgreSQL 18,
вставляет маркер, создаёт архив, восстанавливает `testapp_restore_verify`, проверяет схему,
историю миграций и маркер, затем удаляет целевую базу. JSON сохраняется как artifact
`ci-database-restore-evidence`. Там же выполняются контрактные тесты application-level
оркестратора, включая частичный startup и неуспешный cleanup.

Это проверяет инструментарий репозитория, но не является реальным staging application drill,
не проверяет снимки платформы и не доказывает PITR.

## Политика для production

Владелец развёртывания задаёт расписание, зашифрованное внешнее/неизменяемое хранилище, сроки хранения, WAL/PITR при более строгом RPO, отдельные учётные данные для восстановления, алертинг и учебные восстановления на staging с измерением времени.

Рекомендуемый минимум:

```text
ежедневных   14
еженедельных  8
ежемесячных  12
```

Восстановление считается принятым только после проверок контрольной суммы, архива, схемы, истории миграций и маркера, при работоспособном API, прохождении показательного бизнес-сценария, проверке Outbox/аудита и фиксации фактического RTO.

## Предупреждение о смене СУБД

Скрипты принимают только архивы PostgreSQL в custom-формате. Дамп MariaDB ими не восстанавливается и не конвертируется. Существующие данные MariaDB требуют отдельно проверенного ETL/перехода — см. [PERSISTENCE.md](PERSISTENCE.md#engine-cutover-и-существующие-mariadb-данные). Старая резервная копия/том сохраняются как точка отката до приёмки и до конца согласованного срока хранения.
