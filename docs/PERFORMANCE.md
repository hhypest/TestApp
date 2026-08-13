# Performance and capacity baseline

> Scope: `beta-ddd`. Эти значения являются CI regression gates, а не production SLA и не аппаратно-независимым обещанием throughput.

> Verification status на code baseline `ff33a95`: **VERIFYING**. [`performance` run 31668024062](https://github.com/hhypest/TestApp/actions/runs/31668024062) прошёл production-shaped startup, authenticated k6 и expiration storm, но RabbitMQ Management API вернул HTTP 400 во время probe queue/binding setup до вставки synthetic Outbox rows. Полного green D6 evidence пока нет.

## Цель D6

Capacity baseline защищает наиболее важные пути:

- simultaneous attempt starts;
- answer write burst;
- bulk assignment creation;
- reviewer pagination;
- overdue-attempt expiration drain;
- Outbox backlog recovery через RabbitMQ.

Workflow `.github/workflows/performance.yml` использует production Docker image и реальные MariaDB 12.3, RabbitMQ и Keycloak. HTTP-сценарии находятся в `performance/k6/api-capacity.js` и получают JWT через локальный Keycloak realm.

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

После HTTP-нагрузки оставшиеся `InProgress` attempts переводятся в overdue в изолированной CI MariaDB. Реальный expiration worker должен уменьшить backlog до нуля не более чем за 30 секунд. В performance environment poll interval равен 1 секунде.

### Outbox recovery

Workflow создаёт временную RabbitMQ queue, связанную с `testapp.events`, и добавляет 100 synthetic `CapacityProbe` Outbox rows в изолированную CI database. Реальный `OutboxProcessor` с publisher confirms должен отметить все 100 сообщений как processed не более чем за 30 секунд.

Topology setup является частью gate: при ошибке workflow должен сохранять HTTP status/response body, явно проверить exchange/queue/binding и не интерпретировать setup failure как application backlog failure.

## Evidence

Каждый run загружает artifact `testapp-capacity-results` на 30 дней. В нём сохраняются:

- `k6-summary.json`;
- `expiration.json`;
- `outbox.json`.

## Интерпретация

GitHub-hosted runner — shared и шумная среда. Эти thresholds предназначены для поиска крупных регрессий. Перед 1.0 staging soak должен отдельно зафиксировать production-like topology, CPU/RAM, dataset size, concurrent students, p95/p99, DB pool behavior и sustained Outbox traffic.

При падении gate сначала сравниваются artifacts и service logs с последним зелёным baseline. Если failure произошёл до создания workload (как на `ff33a95`), отдельно исправляется/диагностируется test harness. Threshold нельзя повышать только ради зелёного CI без документированного изменения capacity expectation.
