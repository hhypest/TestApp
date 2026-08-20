// per-vu-iterations даёт каждому VU собственный последовательный номер итерации.
// Сначала берём одинаковый номер «раунда» всех VU, затем смещаемся на ID текущего VU:
//   round 0 -> students 0..vus-1, round 1 -> students vus..2*vus-1.
// Последний неполный раунд отсекается вызывающим кодом по STUDENTS.
//
// scenario.iterationInTest здесь использовать нельзя: это глобальный номер итерации
// сценария. Его повторное умножение на vus пропускает и повторяет индексы при некоторых
// сочетаниях SEED_STUDENTS/SEED_VUS.
export function studentIndex(iterationInScenario, vuIdInTest, vus) {
  return iterationInScenario * vus + (vuIdInTest - 1);
}
