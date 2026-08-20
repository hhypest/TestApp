import assert from 'node:assert/strict';
import test from 'node:test';

import { studentIndex } from './seed-plan.js';

const profiles = [
  { students: 1, vus: 1 },
  { students: 7, vus: 3 },
  { students: 500, vus: 8 },
  { students: 500, vus: 10 },
  { students: 503, vus: 16 },
  { students: 500, vus: 500 },
];

test('per-VU seed plan covers every student exactly once', () => {
  for (const { students, vus: requestedVus } of profiles) {
    const vus = Math.min(requestedVus, students);
    const iterationsPerVu = Math.ceil(students / vus);
    const actual = [];

    for (let iteration = 0; iteration < iterationsPerVu; iteration += 1) {
      for (let vuId = 1; vuId <= vus; vuId += 1) {
        const index = studentIndex(iteration, vuId, vus);
        if (index < students) {
          actual.push(index);
        }
      }
    }

    assert.deepEqual(
      actual,
      Array.from({ length: students }, (_, index) => index),
      `students=${students}, vus=${requestedVus}`,
    );
  }
});
