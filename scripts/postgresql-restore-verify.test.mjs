import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { chmod, mkdir, mkdtemp, readFile, rm, stat, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const restoreScript = path.join(repositoryRoot, 'scripts', 'postgresql-restore-verify.sh');

async function fixture(t) {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'testapp-restore-evidence-'));
  t.after(() => rm(directory, { recursive: true, force: true }));

  const binDirectory = path.join(directory, 'bin');
  const dropCounter = path.join(directory, 'drop-count');
  const dockerLog = path.join(directory, 'docker.log');
  const backupPath = path.join(directory, 'testapp.dump');
  const evidencePath = path.join(directory, 'restore-evidence.json');
  await mkdir(binDirectory);
  await writeFile(backupPath, 'fixture custom-format archive\n', 'utf8');

  const digest = createHash('sha256').update(await readFile(backupPath)).digest('hex');
  await writeFile(`${backupPath}.sha256`, `${digest}  ${backupPath}\n`, 'utf8');

  const dockerStub = path.join(binDirectory, 'docker');
  await writeFile(dockerStub, `#!/usr/bin/env bash
set -euo pipefail
printf '%s\\n' "$*" >> "$FAKE_DOCKER_LOG"

sql=''
for argument in "$@"; do
  case "$argument" in
    --command=*) sql="\${argument#--command=}" ;;
  esac
done

if [[ "$*" == *" psql "* ]]; then
  if [[ "$sql" == *'DROP DATABASE IF EXISTS'* ]]; then
    count=0
    if [[ -f "$FAKE_DROP_COUNTER" ]]; then
      count="$(<"$FAKE_DROP_COUNTER")"
    fi
    count=$((count + 1))
    printf '%s' "$count" > "$FAKE_DROP_COUNTER"
    if [[ "\${FAKE_FAIL_DROP_ON_CALL:-0}" = "$count" ]]; then
      exit 77
    fi
  fi
  case "$sql" in
    *pg_database*datname*) printf '%s\\n' "\${FAKE_SOURCE_EXISTS:-0}" ;;
    *information_schema.tables*) printf '%s\\n' "\${FAKE_TABLE_COUNT:-13}" ;;
    *'__EFMigrationsHistory'*) printf '%s\\n' "\${FAKE_MIGRATION_COUNT:-1}" ;;
    *'ci.restore.marker'*) printf '%s\\n' "$FAKE_VERIFY_RESULT" ;;
  esac
fi
`, 'utf8');
  await chmod(dockerStub, 0o755);

  return { backupPath, binDirectory, digest, directory, dockerLog, dropCounter, evidencePath };
}

function runRestore(paths, overrides = {}) {
  return spawnSync(
    'bash',
    [restoreScript, paths.backupPath, 'testapp_restore_drill'],
    {
      cwd: repositoryRoot,
      encoding: 'utf8',
      env: {
        ...process.env,
        PATH: `${paths.binDirectory}:${process.env.PATH}`,
        FAKE_DOCKER_LOG: paths.dockerLog,
        FAKE_DROP_COUNTER: paths.dropCounter,
        FAKE_VERIFY_RESULT: '1',
        FAKE_SOURCE_EXISTS: '0',
        FAKE_TABLE_COUNT: '13',
        FAKE_MIGRATION_COUNT: '1',
        POSTGRES_ADMIN_PASSWORD: 'restore-test-password',
        POSTGRES_SOURCE_DATABASE: 'testapp',
        RESTORE_EVIDENCE_PATH: paths.evidencePath,
        VERIFY_QUERY: "SELECT COUNT(*) FROM idempotency_records WHERE \"Operation\" = 'ci.restore.marker';",
        VERIFY_EXPECTED: '1',
        ...overrides,
      },
    },
  );
}

test('publishes private machine-readable evidence only after a verified restore', async (t) => {
  const paths = await fixture(t);

  const result = runRestore(paths);

  assert.equal(result.status, 0, result.stderr);
  const evidenceText = await readFile(paths.evidencePath, 'utf8');
  const evidence = JSON.parse(evidenceText);
  assert.deepEqual(
    {
      schemaVersion: evidence.schemaVersion,
      status: evidence.status,
      scope: evidence.scope,
      sourceDatabase: evidence.sourceDatabase,
      targetDatabase: evidence.targetDatabase,
      backupSha256: evidence.backupSha256,
      backupBytes: evidence.backupBytes,
      tableCount: evidence.tableCount,
      migrationCount: evidence.migrationCount,
      customVerificationExecuted: evidence.customVerificationExecuted,
    },
    {
      schemaVersion: 1,
      status: 'passed',
      scope: 'database-restore-verified',
      sourceDatabase: 'testapp',
      targetDatabase: 'testapp_restore_drill',
      backupSha256: paths.digest,
      backupBytes: 30,
      tableCount: 13,
      migrationCount: 1,
      customVerificationExecuted: true,
    },
  );
  assert.match(evidence.startedAt, /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/);
  assert.match(evidence.completedAt, /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/);
  for (const metric of [
    'databaseRestoreSeconds',
    'databaseVerificationSeconds',
    'databaseRecoverySeconds',
  ]) {
    assert.equal(Number.isInteger(evidence[metric]), true, `${metric} must be an integer`);
    assert.equal(evidence[metric] >= 0, true, `${metric} must not be negative`);
  }
  assert.equal(evidenceText.includes('restore-test-password'), false);
  assert.equal(evidenceText.includes('ci.restore.marker'), false);
  assert.equal((await stat(paths.evidencePath)).mode & 0o777, 0o600);

  const dockerLog = await readFile(paths.dockerLog, 'utf8');
  assert.match(dockerLog, /DROP DATABASE IF EXISTS "testapp_restore_drill"/);
});

test('keeps previous evidence intact when custom verification fails', async (t) => {
  const paths = await fixture(t);
  const previousEvidence = '{"status":"previous-success"}\n';
  await writeFile(paths.evidencePath, previousEvidence, 'utf8');

  const result = runRestore(paths, { FAKE_VERIFY_RESULT: '0' });

  assert.equal(result.status, 1);
  assert.match(result.stderr, /expected '1', got '0'/);
  assert.equal(await readFile(paths.evidencePath, 'utf8'), previousEvidence);
  const dockerLog = await readFile(paths.dockerLog, 'utf8');
  assert.match(dockerLog, /DROP DATABASE IF EXISTS "testapp_restore_drill"/);
});

test('does not publish success when final disposable-database cleanup fails', async (t) => {
  const paths = await fixture(t);
  const previousEvidence = '{"status":"previous-success"}\n';
  await writeFile(paths.evidencePath, previousEvidence, 'utf8');

  const result = runRestore(paths, { FAKE_FAIL_DROP_ON_CALL: '2' });

  assert.notEqual(result.status, 0);
  assert.equal(await readFile(paths.evidencePath, 'utf8'), previousEvidence);
  assert.equal(Number(await readFile(paths.dropCounter, 'utf8')) >= 3, true);
});

test('refuses to overwrite the backup with its evidence', async (t) => {
  const paths = await fixture(t);

  const result = runRestore(paths, { RESTORE_EVIDENCE_PATH: paths.backupPath });

  assert.equal(result.status, 2);
  assert.match(result.stderr, /evidence path must differ from backup and checksum paths/i);
  assert.equal(await readFile(paths.backupPath, 'utf8'), 'fixture custom-format archive\n');
  await assert.rejects(readFile(paths.dockerLog), { code: 'ENOENT' });
});

test('promotes a verified restore only inside a clean instance', async (t) => {
  const paths = await fixture(t);

  const result = runRestore(paths, {
    RESTORE_PROMOTE_TARGET: '1',
    RESTORE_EXPECTED_TABLE_COUNT: '13',
    RESTORE_EXPECTED_MIGRATION_COUNT: '1',
  });

  assert.equal(result.status, 0, result.stderr);
  const evidenceText = await readFile(paths.evidencePath, 'utf8');
  const evidence = JSON.parse(evidenceText);
  assert.deepEqual(
    {
      schemaVersion: evidence.schemaVersion,
      status: evidence.status,
      scope: evidence.scope,
      sourceDatabase: evidence.sourceDatabase,
      temporaryDatabase: evidence.temporaryDatabase,
      restoredDatabase: evidence.restoredDatabase,
      targetRetained: evidence.targetRetained,
      tableCount: evidence.tableCount,
      migrationCount: evidence.migrationCount,
    },
    {
      schemaVersion: 2,
      status: 'passed',
      scope: 'database-restore-promoted',
      sourceDatabase: 'testapp',
      temporaryDatabase: 'testapp_restore_drill',
      restoredDatabase: 'testapp',
      targetRetained: true,
      tableCount: 13,
      migrationCount: 1,
    },
  );
  assert.equal((await stat(paths.evidencePath)).mode & 0o777, 0o600);

  const dockerLog = await readFile(paths.dockerLog, 'utf8');
  assert.match(dockerLog, /ALTER DATABASE "testapp_restore_drill" RENAME TO "testapp"/);
  assert.doesNotMatch(dockerLog, /DROP DATABASE IF EXISTS "testapp"/);
});

test('refuses promotion when the source database already exists', async (t) => {
  const paths = await fixture(t);
  const previousEvidence = '{"status":"previous-success"}\n';
  await writeFile(paths.evidencePath, previousEvidence, 'utf8');

  const result = runRestore(paths, {
    FAKE_SOURCE_EXISTS: '1',
    RESTORE_PROMOTE_TARGET: '1',
    RESTORE_EXPECTED_TABLE_COUNT: '13',
    RESTORE_EXPECTED_MIGRATION_COUNT: '1',
  });

  assert.equal(result.status, 1);
  assert.match(result.stderr, /source database 'testapp' already exists/i);
  assert.equal(await readFile(paths.evidencePath, 'utf8'), previousEvidence);
  const dockerLog = await readFile(paths.dockerLog, 'utf8');
  assert.doesNotMatch(dockerLog, /CREATE DATABASE/);
  assert.doesNotMatch(dockerLog, /ALTER DATABASE/);
});

test('keeps previous evidence and drops the target when promoted restore verification fails', async (t) => {
  const paths = await fixture(t);
  const previousEvidence = '{"status":"previous-success"}\n';
  await writeFile(paths.evidencePath, previousEvidence, 'utf8');

  const result = runRestore(paths, {
    FAKE_TABLE_COUNT: '12',
    RESTORE_PROMOTE_TARGET: '1',
    RESTORE_EXPECTED_TABLE_COUNT: '13',
    RESTORE_EXPECTED_MIGRATION_COUNT: '1',
  });

  assert.equal(result.status, 1);
  assert.match(result.stderr, /source tables=13 target tables=12/);
  assert.equal(await readFile(paths.evidencePath, 'utf8'), previousEvidence);
  const dockerLog = await readFile(paths.dockerLog, 'utf8');
  assert.match(dockerLog, /DROP DATABASE IF EXISTS "testapp_restore_drill"/);
  assert.doesNotMatch(dockerLog, /ALTER DATABASE/);
});
