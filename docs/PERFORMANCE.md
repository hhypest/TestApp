# Performance and capacity baseline

> Scope: `beta-ddd`. Эти значения являются CI regression gates, а не production SLA и не аппаратно-независимым обещанием throughput.

> Verification status: **VERIFYING**. Смена database engine инвалидировала прежний MariaDB capacity baseline. Полный D6 gate должен быть заново пройден на exact PostgreSQL implementation HEAD.

## Цель D6

Capacity baseline защищает наиболее важные пути:

- simultaneous attempt starts;
- answer write burst;
- bulk assignment creation;
- reviewer pagination;
- overdue-attempt expiration drain;
- Outbox backlog recovery через RabbitMQ.

Workflow `.github/workflows/performance.yml` использует production Docker image и реальные PostgreSQL 18, RabbitMQ и Keycloak. HTTP-сценарии находятся в `performance/k6/api-capacity.js` и получают JWT через локальный Keycloak realm.

## HTTP regression gates

| Scenario | Нагрузка | Gate |
|---|---|---|
| simultaneous starts | 6 VU, 20 s | p95 < 2000 ms |
| answer writes | 6 VU, 20 s | p95 < 1500 ms |
| bulk assignments | 1 batch/s, 20 targets, 20 s | p95 < 3000 ms |
| reviewer pagination | 4 VU, 20 s | p95 < 1200 ms |

Общие требования: checks > 99.5%, failed HTTP requests < 1%.

В performance compose override rate-limit ceilings увеличены, чтобы измерять application/database path, а не configured abuse-control ceiling.

## Worker probes

### Expiration storm

После HTTP-нагрузки оставшиеся `InProgress` attempts переводятся в overdue в изолированной CI PostgreSQL. Реальный expiration worker должен уменьшить backlog до нуля не более чем за 30 секунд. В performance environment poll interval равен 1 секунде.

### Outbox recovery

Workflow создаёт временную RabbitMQ queue, связанную с `testapp.events`, и после HTTP-нагрузки добавляет 100 synthetic `CapacityProbe` Outbox rows в изолированную CI database. Probe не получает приоритет: если workload или будущие integration contracts оставили pending rows, сохраняется обычный FIFO order. Реальный `OutboxProcessor` с publisher confirms должен дренировать очередь и отметить все 100 probe-сообщений как processed не более чем за 30 секунд.

Topology setup является частью gate: setup failure нельзя интерпретировать как application backlog failure.

## Evidence

Каждый run загружает artifact `testapp-capacity-results` на 30 дней. В нём сохраняются:

- `k6-summary.json`;
- `expiration.json`;
- `outbox.json` с размером backlog перед probe и результатом drain.

## Интерпретация

GitHub-hosted runner — shared и шумная среда. Эти thresholds предназначены для поиска крупных регрессий. Перед 1.0 staging soak должен отдельно зафиксировать production-like topology, CPU/RAM, dataset size, concurrent students, p95/p99, DB pool behavior и sustained Outbox traffic.

При падении gate сначала сравниваются artifacts и service logs с последним зелёным PostgreSQL baseline. Если failure произошёл до создания workload, отдельно диагностируется test harness. Threshold нельзя повышать только ради зелёного CI без документированного изменения capacity expectation.
