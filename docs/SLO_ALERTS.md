# TestApp SLO and alert baseline

> Status: initial engineering baseline for `0.9.3`. These thresholds are operational targets, not a contractual SLA. They must be recalibrated with production traffic before 1.0.

## 1. Principles

Alerts should represent user-visible risk or durable backlog, not individual log lines. Use low-cardinality labels only; never put user IDs, test IDs, attempt IDs or event IDs into metric labels.

The application exports:

- ASP.NET Core/OpenTelemetry request metrics for request count, status and duration;
- runtime/HttpClient telemetry;
- custom `TestApp.Operations` metrics for Outbox, expiration, retention and important API exception categories;
- `/health/live` and `/health/ready` for platform probes.

`OTEL_EXPORTER_OTLP_ENDPOINT` enables OTLP export. The actual query syntax depends on the selected observability backend; this document defines signals and thresholds rather than binding the application to Prometheus/Grafana-specific code.

## 2. Initial service objectives

### Availability

Target: **99.9% successful service availability over a rolling 30-day window** for canonical `/api/v1/*` traffic.

Count server-side `5xx` and sustained readiness failure against availability. Client validation/authentication responses (`400/401/403/404/409/412/428`) are not availability failures. `429` should be monitored separately because it can indicate either expected abuse protection or insufficient rate-limit capacity.

### Latency

Initial target for normal API traffic:

- p95 server request duration < **500 ms** over 15 minutes;
- p99 < **1.5 s** over 15 minutes.

Exclude health endpoints from user-latency SLOs. Expensive administrator/report queries may receive a separate objective after production measurements exist.

### Background processing

- Outbox oldest pending age < **60 s** during normal broker availability.
- No active Outbox dead letters under normal operation.
- Oldest overdue attempt expiration lag < **60 s**.
- Backup logical restore verification must remain green in CI; staging restore duration must satisfy the RTO target in `BACKUP_RESTORE.md`.

## 3. Custom metric contract

Meter: `TestApp.Operations`

| Metric | Type | Labels | Meaning |
|---|---|---|---|
| `testapp.api.exception` | counter | `kind` | Important API exception categories (`concurrency_conflict`, `idempotency_key_reuse`, `bad_request_binding`, `unhandled`) |
| `testapp.outbox.publish` | counter | `outcome` | Outbox publish result (`success`, `failure`, `dead_letter`) |
| `testapp.outbox.delivery_lag` | histogram, seconds | none | Occurrence-to-successful-delivery lag |
| `testapp.outbox.pending` | gauge | none | Active unprocessed Outbox messages |
| `testapp.outbox.dead_letters` | gauge | none | Active, non-discarded dead letters |
| `testapp.outbox.oldest_pending_age` | gauge, seconds | none | Age of oldest active pending Outbox message |
| `testapp.outbox.dead_letter_action` | counter | `action` | Explicit admin management (`requeue`, `discard`) |
| `testapp.attempt.expiration` | counter | `outcome` | Background expiration result (`success`, `failure`) |
| `testapp.attempt.overdue` | gauge | none | In-progress attempts already past deadline |
| `testapp.attempt.oldest_overdue_lag` | gauge, seconds | none | Age past deadline of the oldest overdue attempt |
| `testapp.retention.deleted` | counter | `kind` | Records deleted by retention (`audit`, `idempotency`, `processed_outbox`) |

The backlog gauges are sampled from MariaDB every 30 seconds and therefore remain meaningful across multiple API replicas.

## 4. Alert policy

### Page / critical

Trigger an urgent alert when any of the following is sustained:

1. `/health/ready` fails for **3 consecutive probes** on all serving replicas.
2. API 5xx rate exceeds **2% for 5 minutes** with at least 20 requests in the window.
3. `testapp.outbox.dead_letters > 0` for **5 minutes**.
4. `testapp.outbox.oldest_pending_age > 300 s` for **5 minutes** while Outbox transport is enabled.
5. `testapp.attempt.oldest_overdue_lag > 300 s` for **5 minutes**.
6. `testapp.api.exception{kind="unhandled"}` increases repeatedly for **5 minutes** rather than a single isolated request.

### Warning / ticket

Trigger a non-paging warning when:

1. p95 request duration > **500 ms for 15 minutes**.
2. p99 request duration > **1.5 s for 15 minutes**.
3. `testapp.outbox.oldest_pending_age > 60 s` for **10 minutes**.
4. `testapp.outbox.pending > 1000` for **10 minutes**.
5. `testapp.attempt.oldest_overdue_lag > 60 s` for **10 minutes**.
6. `testapp.attempt.expiration{outcome="failure"}` increases during two consecutive worker cycles.
7. concurrency conflicts increase materially above the normal baseline; this is initially a diagnostic signal, not a page, because valid competing writes can legitimately cause them.

## 5. Alert triage

### Readiness failure

Check in order:

1. MariaDB reachability and connection saturation;
2. RabbitMQ readiness when transport is enabled;
3. deployment/configuration errors;
4. dependency/network incident;
5. recent migration/deployment changes.

Do not restart repeatedly without identifying a dependency failure; readiness intentionally removes an unhealthy instance from service.

### Outbox backlog/dead letter

1. inspect `/api/v1/operations/outbox`;
2. inspect safe metadata with `/api/v1/operations/outbox/dead-letters/{eventId}`;
3. fix the dependency/schema/routing cause before replay;
4. use explicit audited `requeue` only after the cause is corrected;
5. use `discard` only when the message is proven obsolete and record a reason;
6. correlate using request/audit/trace IDs. Event payload is intentionally not returned by the management API.

### Expiration lag

1. check DB readiness and query latency;
2. inspect worker errors and `testapp.attempt.expiration{outcome="failure"}`;
3. compare overdue count/lag with configured expiration batch size and polling interval;
4. if sustained under healthy dependencies, capacity-test and tune batch/poll settings rather than bypassing attempt lifecycle invariants.

## 6. Dashboard minimum

A production dashboard should contain at least:

- request rate by HTTP status class;
- p50/p95/p99 request duration;
- readiness state;
- unhandled/concurrency/idempotency exception rate;
- Outbox pending count, oldest pending age and publish outcomes;
- active dead-letter count and explicit dead-letter actions;
- overdue attempt count and oldest overdue lag;
- runtime CPU, GC and memory signals;
- deployment/version annotation.

## 7. Pre-1.0 verification gate

Before 1.0:

- run a staging alert drill for readiness, Outbox dead-letter/backlog and expiration lag;
- verify each alert routes to an actionable destination;
- measure normal production-like latency/backlog and recalibrate thresholds;
- record evidence of the backup/restore drill and actual RTO;
- ensure dashboards do not expose high-cardinality identifiers or correctness/payload data.
