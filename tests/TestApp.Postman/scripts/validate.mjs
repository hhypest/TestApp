import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const projectDirectory = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const collectionPath = path.join(projectDirectory, 'TestApp.Api.postman_collection.json');
const environmentPath = path.join(projectDirectory, 'TestApp.Local.postman_environment.json');

const collection = JSON.parse(fs.readFileSync(collectionPath, 'utf8'));
const environment = JSON.parse(fs.readFileSync(environmentPath, 'utf8'));

assert.equal(
  collection.info.schema,
  'https://schema.getpostman.com/json/collection/v2.1.0/collection.json',
  'The collection must use Postman Collection v2.1.',
);
assert.equal(environment._postman_variable_scope, 'environment');

const requests = [];
const scripts = [];

function visit(items, parents = []) {
  for (const item of items || []) {
    const location = [...parents, item.name].join(' / ');
    for (const event of item.event || []) {
      assert.ok(['test', 'prerequest'].includes(event.listen), `${location}: unsupported event ${event.listen}`);
      assert.ok(Array.isArray(event.script?.exec), `${location}: script.exec must be an array`);
      scripts.push({ location: `${location} [${event.listen}]`, source: event.script.exec.join('\n') });
    }

    if (item.request) {
      const rawUrl = typeof item.request.url === 'string' ? item.request.url : item.request.url?.raw;
      assert.ok(rawUrl, `${location}: request URL is missing`);
      assert.ok(item.request.method, `${location}: request method is missing`);
      assert.ok((item.event || []).some((event) => event.listen === 'test'), `${location}: test script is missing`);
      requests.push({ location, method: item.request.method, rawUrl });
    }

    if (item.item) visit(item.item, [...parents, item.name]);
  }
}

for (const event of collection.event || []) {
  scripts.push({ location: `collection [${event.listen}]`, source: event.script.exec.join('\n') });
}
visit(collection.item);

for (const { location, source } of scripts) {
  try {
    new Function(source);
  } catch (error) {
    throw new Error(`${location}: invalid JavaScript: ${error.message}`);
  }
}

const duplicateNames = requests
  .map((request) => request.location)
  .filter((name, index, all) => all.indexOf(name) !== index);
assert.deepEqual(duplicateNames, [], 'Request names must be unique within their folder path.');

const routeKeys = new Set(
  requests
    .filter(({ rawUrl }) => rawUrl.startsWith('{{baseUrl}}'))
    .map(({ method, rawUrl }) => `${method} ${rawUrl.slice('{{baseUrl}}'.length).split('?')[0]}`),
);

const requiredRoutes = [
  'GET /health/live',
  'GET /health/ready',
  'GET /openapi/v1.json',
  'GET /api/v1/tests',
  'POST /api/v1/tests',
  'PATCH /api/v1/tests/{{testId}}/title',
  'PATCH /api/v1/tests/{{testId}}/settings',
  'POST /api/v1/tests/{{testId}}/questions',
  'PUT /api/v1/tests/{{testId}}/questions/{{questionId}}',
  'DELETE /api/v1/tests/{{sandboxTestId}}/questions/{{sandboxQuestionId}}',
  'PATCH /api/v1/tests/{{testId}}/questions/{{questionId}}/order',
  'POST /api/v1/tests/{{testId}}/questions/{{questionId}}/options',
  'PUT /api/v1/tests/{{testId}}/questions/{{questionId}}/options/{{correctOptionId}}',
  'DELETE /api/v1/tests/{{sandboxTestId}}/questions/{{sandboxQuestionId}}/options/{{sandboxOptionId}}',
  'PATCH /api/v1/tests/{{testId}}/questions/{{questionId}}/options/{{correctOptionId}}/order',
  'POST /api/v1/tests/{{testId}}/publish',
  'POST /api/v1/tests/{{testId}}/archive',
  'GET /api/v1/tests/{{testId}}/editor',
  'GET /api/v1/tests/{{testId}}/revisions',
  'GET /api/v1/assignments',
  'GET /api/v1/assignments/{{assignmentId}}',
  'GET /api/v1/assignments/{{assignmentId}}/attempts',
  'POST /api/v1/assignments',
  'POST /api/v1/assignments/bulk',
  'PATCH /api/v1/assignments/{{assignmentId}}/window',
  'PATCH /api/v1/assignments/{{assignmentId}}/attempt-limit',
  'POST /api/v1/assignments/{{bulkAssignmentId}}/cancel',
  'POST /api/v1/assignments/{{assignmentId}}/attempts',
  'GET /api/v1/me/assignments',
  'GET /api/v1/me/attempts',
  'GET /api/v1/results',
  'GET /api/v1/results/{{attemptId}}',
  'GET /api/v1/operations/outbox',
  'GET /api/v1/operations/audit',
  'GET /api/v1/operations/outbox/dead-letters/{{deadLetterEventId}}',
  'POST /api/v1/operations/outbox/dead-letters/{{deadLetterRequeueEventId}}/requeue',
  'POST /api/v1/operations/outbox/dead-letters/{{deadLetterDiscardEventId}}/discard',
  'PUT /api/v1/attempts/{{attemptId}}/answers/{{questionId}}',
  'DELETE /api/v1/attempts/{{attemptId}}/answers/{{questionId}}',
  'POST /api/v1/attempts/{{attemptId}}/submit',
  'POST /api/v1/attempts/{{timeoutAttemptId}}/timeout',
  'GET /api/v1/attempts/{{attemptId}}',
  'GET /api/v1/attempts/{{attemptId}}/presentation',
  'GET /api/v1/assignments/{{assignmentId}}/attempts/active',
  'GET /api/v1/attempts/{{attemptId}}/result',
];

const missingRoutes = requiredRoutes.filter((route) => !routeKeys.has(route));
assert.deepEqual(missingRoutes, [], `Collection does not cover routes: ${missingRoutes.join(', ')}`);

const environmentKeys = new Set(environment.values.map((entry) => entry.key));
for (const key of [
  'baseUrl',
  'keycloakUrl',
  'realm',
  'clientId',
  'authorUsername',
  'authorPassword',
  'adminUsername',
  'adminPassword',
  'studentUsername',
  'studentPassword',
]) {
  assert.ok(environmentKeys.has(key), `Environment variable ${key} is missing.`);
}

const tokenRequests = requests.filter(({ rawUrl }) => rawUrl.includes('/protocol/openid-connect/token'));
assert.equal(tokenRequests.length, 3, 'Author, admin and student token requests are required.');
assert.ok(requests.length >= 70, `Expected a comprehensive collection, found only ${requests.length} requests.`);

console.log(`Postman project is valid: ${requests.length} requests, ${collection.item.length} folders, ${requiredRoutes.length} required routes.`);
