# Performance and capacity baseline

> Scope: `beta-ddd`. Эти значения являются CI regression gates, а не production SLA и не аппаратно-независимым обещанием throughput.

> Verification status: **PASSED**. Полный PostgreSQL/RabbitMQ D6 gate green на implementation commit [`9916b98`](https://github.com/hhypest/TestApp/commit/9916b980bc9a8ab0dd007b568207c78ef50382e4): [performance #9](https://github.com/hhypest/TestApp/actions/runs/31779887626), [dotnet #388](https://github.com/hhypest/TestApp/actions/runs/31779887611) и [security #20](https://github.com/hhypest/TestApp/actions/runs/31779887588) завершились успешно.

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

Последний verified baseline (`9916b98`):

| Scenario | Result |
|---|---:|
| checks | 8771 / 8771 |
| failed HTTP requests | 0 / 8774 |
| simultaneous starts p95 | 340.91 ms |
| answer writes p95 | 30.59 ms |
| bulk assignments p95 | 68.76 ms |
| reviewer pagination p95 | 33.67 ms |
| expiration drain | 1247 -> 0, 14 s |
| Outbox drain | 100 -> 0, 1 s |

## Worker probes

### Expiration storm

После HTTP-нагрузки оставшиеся `InProgress` attempts переводятся в overdue в изолированной CI PostgreSQL. Реальный expiration worker должен уменьшить backlog до нуля не более чем за 30 секунд. В performance environment poll interval равен 1 секунде.

### Outbox recovery

Workflow создаёт CI-scoped durable RabbitMQ queue, связанную с `testapp.events`, и после HTTP-нагрузки добавляет 100 synthetic `CapacityProbe` Outbox rows в изолированную CI database. Durable topology нужна для совместимости с RabbitMQ 4.3, где deprecated transient non-exclusive queues запрещены; queue удаляется вместе с одноразовым Compose volume. Probe не получает приоритет: если workload или будущие integration contracts оставили pending rows, сохраняется обычный FIFO order. Реальный `OutboxProcessor` с publisher confirms должен дренировать очередь и отметить все 100 probe-сообщений как processed не более чем за 30 секунд.

Topology setup является частью gate: setup failure нельзя интерпретировать как application backlog failure.

## Evidence

Каждый run загружает artifact `testapp-capacity-results` на 30 дней. В нём сохраняются:

- `k6-summary.json`;
- `expiration.json`;
- `outbox.json` с размером backlog перед probe и результатом drain.

## Интерпретация

GitHub-hosted runner — shared и шумная среда. Эти thresholds предназначены для поиска крупных регрессий. Перед 1.0 staging soak должен отдельно зафиксировать production-like topology, CPU/RAM, dataset size, concurrent students, p95/p99, DB pool behavior и sustained Outbox traffic.

При падении gate сначала сравниваются artifacts и service logs с последним зелёным PostgreSQL baseline. Если failure произошёл до создания workload, отдельно диагностируется test harness. Threshold нельзя повышать только ради зелёного CI без документированного изменения capacity expectation.
