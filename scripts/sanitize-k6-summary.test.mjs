import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm, stat, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

import { sanitizeSummary, sanitizeSummaryFile } from './sanitize-k6-summary.mjs';

function fixtureToken() {
  return [`eyJ${'a'.repeat(21)}`, 'b'.repeat(24), 'c'.repeat(24)].join('.');
}

async function temporaryDirectory(t) {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'testapp-k6-summary-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  return directory;
}

test('removes setup_data and writes only upload-safe metrics', async (t) => {
  const directory = await temporaryDirectory(t);
  const inputPath = path.join(directory, 'raw.json');
  const outputPath = path.join(directory, 'safe.json');
  const token = fixtureToken();
  const raw = {
    setup_data: {
      adminToken: token,
      authorToken: token,
      studentToken: token,
      answerAttemptIds: ['attempt-1'],
    },
    metrics: {
      checks: { passes: 42, fails: 0 },
      'http_req_duration{operation:token}': { 'p(95)': 25 },
    },
  };
  await writeFile(inputPath, JSON.stringify(raw), 'utf8');

  const sanitized = await sanitizeSummaryFile(inputPath, outputPath);
  const serialized = await readFile(outputPath, 'utf8');

  assert.equal(Object.hasOwn(sanitized, 'setup_data'), false);
  assert.deepEqual(sanitized.metrics, raw.metrics);
  assert.equal(serialized.includes(token), false);
  await assert.rejects(stat(inputPath), { code: 'ENOENT' });
});

test('rejects sensitive values that survive outside setup_data', () => {
  const fixtures = [
    fixtureToken(),
    `Bearer ${fixtureToken()}`,
    `-----BEGIN ${'PRIVATE KEY-----'}\nfixture`,
  ];

  for (const value of fixtures) {
    assert.throws(
      () => sanitizeSummary({ metrics: { accidentalLeak: value } }),
      /still contains a sensitive value at \$\.metrics\.accidentalLeak/,
    );
  }

  assert.throws(
    () => sanitizeSummary({ metrics: { access_token: 'opaque-fixture' } }),
    /still contains a sensitive value at \$\.metrics\.access_token/,
  );
});

test('invalid input does not replace an existing safe artifact', async (t) => {
  const directory = await temporaryDirectory(t);
  const inputPath = path.join(directory, 'raw.json');
  const outputPath = path.join(directory, 'safe.json');
  const existing = '{"metrics":{"checks":{"passes":1}}}\n';
  await writeFile(inputPath, '{not-json', 'utf8');
  await writeFile(outputPath, existing, 'utf8');

  await assert.rejects(
    sanitizeSummaryFile(inputPath, outputPath),
    /raw k6 summary is not valid JSON/,
  );
  assert.equal(await readFile(outputPath, 'utf8'), existing);
  assert.equal(await readFile(inputPath, 'utf8'), '{not-json');
});

test('refuses to sanitize in place', async () => {
  await assert.rejects(
    sanitizeSummaryFile('/tmp/testapp-summary.json', '/tmp/testapp-summary.json'),
    /raw and sanitized k6 summary paths must be different/,
  );
});
