import http from 'k6/http';
import { check, fail, sleep } from 'k6';
import exec from 'k6/execution';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const KEYCLOAK_URL = __ENV.KEYCLOAK_URL || 'http://localhost:8081';
const CLIENT_ID = __ENV.KEYCLOAK_CLIENT_ID || 'testapp-api';

export const options = {
  discardResponseBodies: false,
  scenarios: {
    simultaneous_starts: {
      executor: 'constant-vus',
      exec: 'startAttempts',
      vus: 6,
      duration: '20s',
      startTime: '0s',
    },
    answer_write_burst: {
      executor: 'constant-vus',
      exec: 'answerWrites',
      vus: 6,
      duration: '20s',
      startTime: '0s',
    },
    bulk_assignments: {
      executor: 'constant-arrival-rate',
      exec: 'bulkAssignments',
      rate: 1,
      timeUnit: '1s',
      duration: '20s',
      preAllocatedVUs: 2,
      maxVUs: 4,
      startTime: '0s',
    },
    reviewer_pagination: {
      executor: 'constant-vus',
      exec: 'reviewerPagination',
      vus: 4,
      duration: '20s',
      startTime: '0s',
    },
  },
  thresholds: {
    checks: ['rate>0.995'],
    http_req_failed: ['rate<0.01'],
    'http_req_duration{scenario:simultaneous_starts}': ['p(95)<2000'],
    'http_req_duration{scenario:answer_write_burst}': ['p(95)<1500'],
    'http_req_duration{scenario:bulk_assignments}': ['p(95)<3000'],
    'http_req_duration{scenario:reviewer_pagination}': ['p(95)<1200'],
  },
};

function uuid() {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = Math.floor(Math.random() * 16);
    const v = c === 'x' ? r : ((r & 0x3) | 0x8);
    return v.toString(16);
  });
}

function token(username, password) {
  const response = http.post(
    `${KEYCLOAK_URL}/realms/testapp/protocol/openid-connect/token`,
    {
      client_id: CLIENT_ID,
      grant_type: 'password',
      username,
      password,
    },
    { tags: { phase: 'setup', operation: 'token' } },
  );
  if (response.status !== 200) {
    fail(`Keycloak token failed for ${username}: ${response.status} ${response.body}`);
  }
  return response.json('access_token');
}

function jsonHeaders(accessToken, extra = {}) {
  return {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Content-Type': 'application/json',
      ...extra,
    },
  };
}

function requireStatus(response, expected, operation) {
  if (!check(response, { [`${operation}: HTTP ${expected}`]: (r) => r.status === expected })) {
    fail(`${operation} failed: ${response.status} ${response.body}`);
  }
  return response;
}

function guidFromResponse(response, operation) {
  const body = response.json();
  const value = typeof body === 'string' ? body : (body.value || body.Value);
  if (!value) {
    fail(`${operation} did not return a Guid-shaped value: ${response.body}`);
  }
  return value;
}

function etagFor(testId, authorToken) {
  const response = requireStatus(
    http.get(`${BASE_URL}/api/v1/tests/${testId}/editor`, jsonHeaders(authorToken)),
    200,
    'get editor ETag',
  );
  const etag = response.headers.ETag || response.headers.Etag || response.headers.etag;
  if (!etag) {
    fail(`Editor did not return ETag for test ${testId}`);
  }
  return etag;
}

function postTestMutation(path, body, testId, authorToken, operation, extraHeaders = {}) {
  const etag = etagFor(testId, authorToken);
  return requireStatus(
    http.post(
      `${BASE_URL}${path}`,
      JSON.stringify(body),
      jsonHeaders(authorToken, { 'If-Match': etag, ...extraHeaders }),
    ),
    200,
    operation,
  );
}

function startAttempt(assignmentId, studentToken, operation = 'start attempt') {
  const key = uuid();
  const response = requireStatus(
    http.post(
      `${BASE_URL}/api/v1/assignments/${assignmentId}/attempts`,
      JSON.stringify({ idempotencyKey: '00000000-0000-0000-0000-000000000000' }),
      jsonHeaders(studentToken, { 'Idempotency-Key': key }),
    ),
    200,
    operation,
  );
  return guidFromResponse(response, operation);
}

export function setup() {
  const authorToken = token('author', 'author');
  const adminToken = token('admin', 'admin');
  const studentToken = token('student', 'student');

  const create = requireStatus(
    http.post(
      `${BASE_URL}/api/v1/tests`,
      JSON.stringify({ title: `Capacity baseline ${Date.now()}` }),
      jsonHeaders(authorToken),
    ),
    200,
    'create test',
  );
  const testId = guidFromResponse(create, 'create test');

  const question = postTestMutation(
    `/api/v1/tests/${testId}/questions`,
    { text: 'Capacity question', type: 1, points: 1, order: 1 },
    testId,
    authorToken,
    'add question',
  );
  const questionId = guidFromResponse(question, 'add question');

  const correctOption = postTestMutation(
    `/api/v1/tests/${testId}/questions/${questionId}/options`,
    { text: 'Correct', isCorrect: true, order: 1 },
    testId,
    authorToken,
    'add correct option',
  );
  const correctOptionId = guidFromResponse(correctOption, 'add correct option');

  postTestMutation(
    `/api/v1/tests/${testId}/questions/${questionId}/options`,
    { text: 'Wrong', isCorrect: false, order: 2 },
    testId,
    authorToken,
    'add wrong option',
  );

  const publishKey = uuid();
  const revision = postTestMutation(
    `/api/v1/tests/${testId}/publish`,
    { idempotencyKey: '00000000-0000-0000-0000-000000000000' },
    testId,
    authorToken,
    'publish test',
    { 'Idempotency-Key': publishKey },
  );
  const revisionId = guidFromResponse(revision, 'publish test');

  const assignmentKey = uuid();
  const assignmentResponse = requireStatus(
    http.post(
      `${BASE_URL}/api/v1/assignments`,
      JSON.stringify({
        revisionId,
        userId: null,
        groupId: 'students',
        availableFrom: new Date(Date.now() - 60_000).toISOString(),
        availableUntil: new Date(Date.now() + 3_600_000).toISOString(),
        attemptLimit: null,
        idempotencyKey: '00000000-0000-0000-0000-000000000000',
      }),
      jsonHeaders(adminToken, { 'Idempotency-Key': assignmentKey }),
    ),
    200,
    'create group assignment',
  );
  const assignmentId = guidFromResponse(assignmentResponse, 'create group assignment');

  const answerAttemptIds = [];
  for (let i = 0; i < 32; i += 1) {
    answerAttemptIds.push(startAttempt(assignmentId, studentToken, 'seed answer attempt'));
  }

  for (let i = 0; i < 20; i += 1) {
    const attemptId = startAttempt(assignmentId, studentToken, 'seed reviewer attempt');
    requireStatus(
      http.put(
        `${BASE_URL}/api/v1/attempts/${attemptId}/answers/${questionId}`,
        JSON.stringify({ optionIds: [correctOptionId] }),
        jsonHeaders(studentToken),
      ),
      200,
      'seed reviewer answer',
    );
    requireStatus(
      http.post(
        `${BASE_URL}/api/v1/attempts/${attemptId}/submit`,
        JSON.stringify({ idempotencyKey: '00000000-0000-0000-0000-000000000000' }),
        jsonHeaders(studentToken, { 'Idempotency-Key': uuid() }),
      ),
      200,
      'seed reviewer submit',
    );
  }

  return {
    authorToken,
    adminToken,
    studentToken,
    testId,
    revisionId,
    assignmentId,
    questionId,
    correctOptionId,
    answerAttemptIds,
  };
}

export function startAttempts(data) {
  const key = uuid();
  const response = http.post(
    `${BASE_URL}/api/v1/assignments/${data.assignmentId}/attempts`,
    JSON.stringify({ idempotencyKey: '00000000-0000-0000-0000-000000000000' }),
    jsonHeaders(data.studentToken, { 'Idempotency-Key': key }),
  );
  check(response, { 'start attempt succeeds': (r) => r.status === 200 });
}

export function answerWrites(data) {
  const index = (exec.vu.idInTest - 1) % data.answerAttemptIds.length;
  const attemptId = data.answerAttemptIds[index];
  const response = http.put(
    `${BASE_URL}/api/v1/attempts/${attemptId}/answers/${data.questionId}`,
    JSON.stringify({ optionIds: [data.correctOptionId] }),
    jsonHeaders(data.studentToken),
  );
  check(response, { 'answer write succeeds': (r) => r.status === 200 });
}

export function bulkAssignments(data) {
  const suffix = `${Date.now()}-${exec.scenario.iterationInTest}-${exec.vu.idInTest}`;
  const targets = [];
  for (let i = 0; i < 20; i += 1) {
    targets.push({ userId: `capacity-${suffix}-${i}`, groupId: null });
  }

  const response = http.post(
    `${BASE_URL}/api/v1/assignments/bulk`,
    JSON.stringify({
      revisionId: data.revisionId,
      targets,
      availableFrom: new Date(Date.now() - 60_000).toISOString(),
      availableUntil: new Date(Date.now() + 3_600_000).toISOString(),
      attemptLimit: 1,
      idempotencyKey: '00000000-0000-0000-0000-000000000000',
    }),
    jsonHeaders(data.adminToken, { 'Idempotency-Key': uuid() }),
  );
  check(response, { 'bulk assignment succeeds': (r) => r.status === 200 });
}

export function reviewerPagination(data) {
  const response = http.get(
    `${BASE_URL}/api/v1/results?testId=${data.testId}&page=1&pageSize=20`,
    jsonHeaders(data.authorToken),
  );
  check(response, { 'reviewer page succeeds': (r) => r.status === 200 });
  sleep(0.05);
}
