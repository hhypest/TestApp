// Засев контура данными эксплуатационного объёма (issue #25, блокирует #18 и #20).
//
// Данные создаются через публичный API, а не INSERT'ами: только так возникают связанные
// записи Outbox, audit и idempotency, и только их целостность потом обязан подтвердить
// restore drill. База, набитая напрямую, проверит пагинацию и планы запросов — но не то,
// ради чего снимается дамп.
//
// Скрипт разбит на фазы, потому что вторая зависит от результата первой: студент не может
// начать попытку по назначению, которого ещё нет. Порядок задаёт scripts/seed-staging.sh —
// запускать k6 руками не нужно.
//
//   PHASE=authoring  — автор создаёт тесты, публикует ревизии, админ раздаёт назначения;
//   PHASE=attempts   — студенты проходят назначенное.
//
// Профиль объёма задаётся переменными SEED_* и зафиксирован в docs/PERFORMANCE.md.
// Цифры по умолчанию — предположение о внутреннем пилоте, а не измерение: их следует
// заменить продуктовыми, потому что от них зависят и baseline #20, и пороги #19.

import http from 'k6/http';
import { check, fail } from 'k6';
import exec from 'k6/execution';
import { studentIndex } from './seed-plan.js';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const KEYCLOAK_URL = __ENV.KEYCLOAK_URL || 'http://localhost:8081';
const REALM = __ENV.KEYCLOAK_REALM || 'testapp';
const CLIENT_ID = __ENV.KEYCLOAK_CLIENT_ID || 'testapp-api';
const CLIENT_SECRET = __ENV.KEYCLOAK_CLIENT_SECRET || '';

const AUTHOR = { username: __ENV.AUTHOR_USERNAME || 'author', password: __ENV.AUTHOR_PASSWORD || 'author' };
const ADMIN = { username: __ENV.ADMIN_USERNAME || 'admin', password: __ENV.ADMIN_PASSWORD || 'admin' };

// Пометка засева. Она же — защита от повторного запуска: тесты называются с этим префиксом,
// и фаза authoring отказывается работать, если такие тесты уже есть.
const SEED_TAG = __ENV.SEED_TAG || 'seed-v1';

const PHASE = __ENV.PHASE || 'authoring';
const VUS = Number(__ENV.SEED_VUS || 8);

const TESTS = Number(__ENV.SEED_TESTS || 200);
const QUESTIONS_PER_TEST = Number(__ENV.SEED_QUESTIONS_PER_TEST || 20);
const OPTIONS_PER_QUESTION = Number(__ENV.SEED_OPTIONS_PER_QUESTION || 4);
const BULK_TARGETS_PER_TEST = Number(__ENV.SEED_BULK_TARGETS_PER_TEST || 100);

const STUDENTS = Number(__ENV.SEED_STUDENTS || 500);
const ATTEMPTS_PER_STUDENT = Number(__ENV.SEED_ATTEMPTS_PER_STUDENT || 4);
const SUBMITTED_SHARE = Number(__ENV.SEED_SUBMITTED_SHARE || 0.7);
const STUDENT_PASSWORD = __ENV.SEED_STUDENT_PASSWORD || '';

// Пороги здесь не про производительность: засев не измеряет систему, он её наполняет.
// Но молча наполнить наполовину нельзя — частично засеянная база даст baseline, который
// невозможно воспроизвести, поэтому любая ошибка обязана уронить прогон.
export const options = {
  discardResponseBodies: false,
  thresholds: {
    checks: ['rate==1.0'],
    http_req_failed: ['rate==0.0'],
  },
  scenarios: PHASE === 'authoring'
    ? {
        authoring: {
          executor: 'shared-iterations',
          exec: 'authorOneTest',
          vus: VUS,
          iterations: TESTS,
          maxDuration: __ENV.SEED_MAX_DURATION || '2h',
        },
      }
    : {
        attempts: {
          executor: 'per-vu-iterations',
          exec: 'studentSession',
          vus: Math.min(VUS, STUDENTS),
          iterations: Math.ceil(STUDENTS / Math.min(VUS, STUDENTS)),
          maxDuration: __ENV.SEED_MAX_DURATION || '2h',
        },
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
  const form = { client_id: CLIENT_ID, grant_type: 'password', username, password };
  if (CLIENT_SECRET !== '') {
    form.client_secret = CLIENT_SECRET;
  }
  const response = http.post(
    `${KEYCLOAK_URL}/realms/${REALM}/protocol/openid-connect/token`,
    form,
    { tags: { phase: 'auth' } },
  );
  if (response.status !== 200) {
    fail(`Keycloak token failed for ${username}: ${response.status} ${response.body}`);
  }
  return response.json('access_token');
}

function jsonHeaders(accessToken, extra = {}) {
  return { headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json', ...extra } };
}

function requireStatus(response, expected, operation) {
  if (!check(response, { [`${operation}: HTTP ${expected}`]: (r) => r.status === expected })) {
    fail(`${operation} failed: ${response.status} ${response.body}`);
  }
  return response;
}

function guidFrom(response, operation) {
  const body = response.json();
  const value = typeof body === 'string' ? body : (body.value || body.Value);
  if (!value) {
    fail(`${operation} did not return a Guid-shaped value: ${response.body}`);
  }
  return value;
}

function studentName(index) {
  return `${SEED_TAG}-student-${String(index).padStart(5, '0')}`;
}

// ETag теста — числовая версия, растущая на каждой записи. Перечитывать редактор перед
// каждой мутацией дорого: у теста с 20 вопросами это сотня лишних чтений всего документа.
// Поэтому версия кешируется и увеличивается локально, а расхождение чинится по 412 —
// то есть предположение о шаге проверяется сервером, а не принимается на веру.
const etagCache = {};

function readEtag(testId, authorToken) {
  const response = requireStatus(
    http.get(`${BASE_URL}/api/v1/tests/${testId}/editor`, jsonHeaders(authorToken)),
    200,
    'read editor ETag',
  );
  const etag = response.headers.ETag || response.headers.Etag || response.headers.etag;
  if (!etag) {
    fail(`Editor returned no ETag for test ${testId}`);
  }
  etagCache[testId] = etag;
  return etag;
}

function nextEtag(testId) {
  const current = etagCache[testId];
  const match = /^"(\d+)"$/.exec(current);
  if (!match) {
    return null;
  }
  const bumped = `"${Number(match[1]) + 1}"`;
  etagCache[testId] = bumped;
  return bumped;
}

function mutate(path, body, testId, authorToken, operation, extraHeaders = {}) {
  const etag = etagCache[testId] || readEtag(testId, authorToken);
  let response = http.post(
    `${BASE_URL}${path}`,
    JSON.stringify(body),
    jsonHeaders(authorToken, { 'If-Match': etag, ...extraHeaders }),
  );

  if (response.status === 412) {
    // Кеш разошёлся с сервером — перечитываем и повторяем один раз. Дальше повторять
    // нечего: 412 после свежего ETag означает конкурирующую запись, а её при засеве быть
    // не должно, и молча продолжать нельзя.
    response = http.post(
      `${BASE_URL}${path}`,
      JSON.stringify(body),
      jsonHeaders(authorToken, { 'If-Match': readEtag(testId, authorToken), ...extraHeaders }),
    );
  }

  requireStatus(response, 200, operation);
  nextEtag(testId);
  return response;
}

export function setup() {
  const authorToken = token(AUTHOR.username, AUTHOR.password);

  if (PHASE === 'authoring') {
    const existing = requireStatus(
      http.get(`${BASE_URL}/api/v1/tests?search=${encodeURIComponent(SEED_TAG)}&page=1&pageSize=1`, jsonHeaders(authorToken)),
      200,
      'check for a previous seed',
    ).json();

    const already = existing.totalCount || existing.TotalCount || 0;
    if (already > 0 && __ENV.SEED_ALLOW_APPEND !== '1') {
      fail(
        `Каталог уже содержит ${already} тест(ов) с меткой '${SEED_TAG}'. Повторный засев удвоил бы объём ` +
        'и сделал baseline невоспроизводимым. Смените SEED_TAG для отдельного набора либо задайте ' +
        'SEED_ALLOW_APPEND=1, если наращивание объёма — именно то, что нужно.',
      );
    }

    return { authorToken, adminToken: token(ADMIN.username, ADMIN.password) };
  }

  if (STUDENT_PASSWORD === '') {
    fail('PHASE=attempts требует SEED_STUDENT_PASSWORD — пароль учётных записей студентов, заведённых scripts/seed-staging.sh.');
  }

  return { authorToken };
}

export function authorOneTest(data) {
  const ordinal = exec.scenario.iterationInTest + 1;
  const title = `${SEED_TAG} тест ${String(ordinal).padStart(5, '0')}`;

  const testId = guidFrom(
    requireStatus(
      http.post(`${BASE_URL}/api/v1/tests`, JSON.stringify({ title }), jsonHeaders(data.authorToken)),
      200,
      'create test',
    ),
    'create test',
  );
  readEtag(testId, data.authorToken);

  for (let q = 1; q <= QUESTIONS_PER_TEST; q += 1) {
    // Каждый третий вопрос — с несколькими правильными ответами: подсчёт баллов по ним
    // идёт другой веткой домена, и в дампе она должна быть представлена.
    const multiple = q % 3 === 0;
    const questionId = guidFrom(
      mutate(
        `/api/v1/tests/${testId}/questions`,
        { text: `Вопрос ${q} (${title})`, type: multiple ? 2 : 1, points: 1, order: q },
        testId,
        data.authorToken,
        'add question',
      ),
      'add question',
    );

    for (let o = 1; o <= OPTIONS_PER_QUESTION; o += 1) {
      mutate(
        `/api/v1/tests/${testId}/questions/${questionId}/options`,
        { text: `Вариант ${o}`, isCorrect: multiple ? o <= 2 : o === 1, order: o },
        testId,
        data.authorToken,
        'add option',
      );
    }
  }

  const revisionId = guidFrom(
    mutate(
      `/api/v1/tests/${testId}/publish`,
      { idempotencyKey: '00000000-0000-0000-0000-000000000000' },
      testId,
      data.authorToken,
      'publish test',
      { 'Idempotency-Key': uuid() },
    ),
    'publish test',
  );

  const availableFrom = new Date(Date.now() - 3_600_000).toISOString();
  const availableUntil = new Date(Date.now() + 30 * 24 * 3_600_000).toISOString();

  // Групповое назначение покрывает всех заведённых студентов разом — по нему они и будут
  // проходить тест в фазе attempts.
  requireStatus(
    http.post(
      `${BASE_URL}/api/v1/assignments`,
      JSON.stringify({
        revisionId,
        userId: null,
        groupId: 'students',
        availableFrom,
        availableUntil,
        attemptLimit: null,
        idempotencyKey: '00000000-0000-0000-0000-000000000000',
      }),
      jsonHeaders(data.adminToken, { 'Idempotency-Key': uuid() }),
    ),
    200,
    'create group assignment',
  );

  // Персональные назначения на несуществующих пользователей — это не фикция, а нормальная
  // эксплуатация: назначают всем, проходят не все. Они дают таблице назначений реальный
  // объём и селективность, которых одно групповое назначение не даёт.
  for (let batch = 0; batch < BULK_TARGETS_PER_TEST; batch += 100) {
    const targets = [];
    for (let i = batch; i < Math.min(batch + 100, BULK_TARGETS_PER_TEST); i += 1) {
      targets.push({ userId: `${SEED_TAG}-roster-${String(ordinal).padStart(5, '0')}-${i}`, groupId: null });
    }
    if (targets.length === 0) {
      break;
    }
    requireStatus(
      http.post(
        `${BASE_URL}/api/v1/assignments/bulk`,
        JSON.stringify({
          revisionId,
          targets,
          availableFrom,
          availableUntil,
          attemptLimit: 1,
          idempotencyKey: '00000000-0000-0000-0000-000000000000',
        }),
        jsonHeaders(data.adminToken, { 'Idempotency-Key': uuid() }),
      ),
      200,
      'bulk assignment',
    );
  }
}

export function studentSession() {
  const vus = Math.min(VUS, STUDENTS);
  const index = studentIndex(exec.vu.iterationInScenario, exec.vu.idInTest, vus);
  if (index >= STUDENTS) {
    return;
  }

  const studentToken = token(studentName(index), STUDENT_PASSWORD);

  const assignments = requireStatus(
    http.get(`${BASE_URL}/api/v1/me/assignments?page=1&pageSize=${ATTEMPTS_PER_STUDENT}`, jsonHeaders(studentToken)),
    200,
    'list my assignments',
  ).json();

  const items = assignments.items || assignments.Items || [];
  if (items.length === 0) {
    fail(`Студенту ${studentName(index)} не назначено ни одного теста — фаза authoring не отработала.`);
  }

  for (const assignment of items) {
    const assignmentId = assignment.id.value || assignment.id;
    const attemptId = guidFrom(
      requireStatus(
        http.post(
          `${BASE_URL}/api/v1/assignments/${assignmentId}/attempts`,
          JSON.stringify({ idempotencyKey: '00000000-0000-0000-0000-000000000000' }),
          jsonHeaders(studentToken, { 'Idempotency-Key': uuid() }),
        ),
        200,
        'start attempt',
      ),
      'start attempt',
    );

    const presentation = requireStatus(
      http.get(`${BASE_URL}/api/v1/attempts/${attemptId}/presentation`, jsonHeaders(studentToken)),
      200,
      'get presentation',
    ).json();

    for (const question of presentation.questions || presentation.Questions || []) {
      const questionId = question.id.value || question.id;
      const optionIds = (question.options || question.Options || [])
        .slice(0, question.type === 2 ? 2 : 1)
        .map((option) => option.id.value || option.id);

      requireStatus(
        http.put(
          `${BASE_URL}/api/v1/attempts/${attemptId}/answers/${questionId}`,
          JSON.stringify({ optionIds }),
          jsonHeaders(studentToken),
        ),
        200,
        'answer question',
      );
    }

    // Часть попыток остаётся незавершённой намеренно: в эксплуатации так и есть, а восстановление
    // и запросы отчётности обязаны работать на смешанном наборе, а не только на завершённом.
    if (Math.random() < SUBMITTED_SHARE) {
      requireStatus(
        http.post(
          `${BASE_URL}/api/v1/attempts/${attemptId}/submit`,
          JSON.stringify({ idempotencyKey: '00000000-0000-0000-0000-000000000000' }),
          jsonHeaders(studentToken, { 'Idempotency-Key': uuid() }),
        ),
        200,
        'submit attempt',
      );
    }
  }
}
