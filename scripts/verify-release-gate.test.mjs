import assert from 'node:assert/strict';
import test from 'node:test';

import { evaluateReleaseGate, requiredWorkflows } from './verify-release-gate.mjs';

function workflowRun(id, conclusion = 'success', createdAt = '2026-08-20T08:00:00Z') {
  return {
    id,
    event: 'workflow_dispatch',
    status: 'completed',
    conclusion,
    created_at: createdAt,
    html_url: `https://github.com/hhypest/TestApp/actions/runs/${id}`,
  };
}

test('release gate accepts five successful exact-head workflows', () => {
  const runs = Object.fromEntries(requiredWorkflows.map((workflow, index) => [workflow, [workflowRun(index + 1)]]));

  const result = evaluateReleaseGate(runs);

  assert.equal(result.failures.length, 0);
  assert.equal(result.rows.length, requiredWorkflows.length);
});

test('release gate rejects missing and unsuccessful workflows', () => {
  const runs = Object.fromEntries(requiredWorkflows.map((workflow, index) => [workflow, [workflowRun(index + 1)]]));
  runs.observability = [];
  runs.security = [workflowRun(100, 'failure')];

  const result = evaluateReleaseGate(runs);

  assert.deepEqual(result.failures, [
    'security: последний завершённый прогон 100 имеет заключение failure',
    'observability: нет завершённого прогона на релизном коммите',
  ]);
});

test('release gate evaluates the newest completed run', () => {
  const runs = Object.fromEntries(requiredWorkflows.map((workflow, index) => [workflow, [workflowRun(index + 1)]]));
  runs.performance = [
    workflowRun(200, 'success', '2026-08-20T07:00:00Z'),
    workflowRun(201, 'cancelled', '2026-08-20T09:00:00Z'),
    { ...workflowRun(202, null, '2026-08-20T10:00:00Z'), status: 'in_progress' },
  ];

  const result = evaluateReleaseGate(runs);

  assert.deepEqual(result.failures, [
    'performance: последний завершённый прогон 201 имеет заключение cancelled',
  ]);
});
