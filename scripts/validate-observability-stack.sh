#!/usr/bin/env bash
set -euo pipefail

# Validates the Prometheus/Grafana observability stack added to compose.yaml:
#   1. deploy/prometheus/prometheus.yml and alerts.yml are syntactically valid
#      (promtool check config / check rules).
#   2. Prometheus has successfully scraped the TestApp target (otel-collector's
#      Prometheus exporter) and itself.
#   3. The alert rule groups documented in docs/SLO_ALERTS.md §4 loaded without error.
#   4. Expected metric families are actually present (custom TestApp.Operations
#      metrics, ASP.NET Core request metrics, .NET runtime metrics).
#   5. The blackbox readiness probe reaches /health/ready and reports success,
#      and the alert rule that depends on it is loaded.
#   6. Grafana is healthy, the provisioned Prometheus datasource works end to end,
#      and the "TestApp Overview" dashboard was provisioned.
#
# Expects the stack from compose.yaml (otel-collector, prometheus, grafana, and
# ideally api generating real traffic) to already be running and reachable at
# the URLs below.

PROMETHEUS_IMAGE="${PROMETHEUS_IMAGE:-prom/prometheus:v3.7.3}"
PROMETHEUS_URL="${PROMETHEUS_URL:-http://localhost:9090}"
GRAFANA_URL="${GRAFANA_URL:-http://localhost:3000}"
GRAFANA_USER="${GRAFANA_USER:-admin}"
GRAFANA_PASSWORD="${GRAFANA_PASSWORD:-admin}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-120}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

echo "== 1. Validating Prometheus config/rule syntax with promtool =="
docker run --rm \
  -v "$REPO_ROOT/deploy/prometheus:/etc/prometheus:ro" \
  --entrypoint promtool \
  "$PROMETHEUS_IMAGE" \
  check config /etc/prometheus/prometheus.yml \
  || fail "promtool check config reported a problem"

docker run --rm \
  -v "$REPO_ROOT/deploy/prometheus/alerts.yml:/alerts.yml:ro" \
  --entrypoint promtool \
  "$PROMETHEUS_IMAGE" \
  check rules /alerts.yml \
  || fail "promtool check rules reported a problem"

echo "== 2. Waiting for Prometheus targets to become healthy =="
deadline=$((SECONDS + TIMEOUT_SECONDS))
while true; do
  targets_json="$(curl --fail --silent --show-error "$PROMETHEUS_URL/api/v1/targets")"
  unhealthy="$(echo "$targets_json" | python3 -c '
import json, sys
data = json.load(sys.stdin)
targets = data["data"]["activeTargets"]
if not targets:
    print("no-targets")
    sys.exit(0)
bad = [t["labels"].get("job", "?") for t in targets if t["health"] != "up"]
print(",".join(bad))
')"
  if [ "$unhealthy" = "" ]; then
    echo "All Prometheus targets are up."
    break
  fi
  if [ "$SECONDS" -ge "$deadline" ]; then
    echo "$targets_json"
    fail "Prometheus targets did not become healthy within ${TIMEOUT_SECONDS}s: $unhealthy"
  fi
  sleep 3
done

echo "== 3. Checking alert rule groups loaded =="
rules_json="$(curl --fail --silent --show-error "$PROMETHEUS_URL/api/v1/rules")"
group_count="$(echo "$rules_json" | python3 -c 'import json,sys;print(len(json.load(sys.stdin)["data"]["groups"]))')"
[ "$group_count" -ge 2 ] || fail "Expected at least 2 alert rule groups (testapp-page, testapp-warning), found $group_count"
echo "Loaded $group_count alert rule groups."

echo "== 4. Checking expected metric families are scraped =="
# The app's OTLP metric exporter runs on a periodic timer (default 60s), so a
# freshly started stack may not have exported testapp.*/dotnet.* series yet
# even though the Prometheus target itself is already reachable — retry
# instead of a single-shot check.
expected_metrics=(
  "up"
  "http_server_request_duration_seconds_count"
  "dotnet_process_cpu_time_seconds_total"
  "dotnet_process_memory_working_set_bytes"
  "testapp_outbox_pending"
  "testapp_attempt_overdue"
)
deadline=$((SECONDS + TIMEOUT_SECONDS))
while true; do
  missing=()
  for metric in "${expected_metrics[@]}"; do
    result="$(curl --fail --silent --show-error --get "$PROMETHEUS_URL/api/v1/query" --data-urlencode "query=$metric" \
      | python3 -c 'import json,sys;print(len(json.load(sys.stdin)["data"]["result"]))')"
    if [ "$result" = "0" ]; then
      missing+=("$metric")
    fi
  done
  if [ "${#missing[@]}" -eq 0 ]; then
    echo "All expected metric families are present."
    break
  fi
  if [ "$SECONDS" -ge "$deadline" ]; then
    fail "Missing expected metrics in Prometheus (no series) after ${TIMEOUT_SECONDS}s: ${missing[*]}"
  fi
  sleep 5
done

echo "== 5. Checking the readiness probe actually probes the API =="
# probe_success приходит от blackbox_exporter, а не от приложения: он ходит в /health/ready
# снаружи, как это делал бы балансировщик. Единица здесь означает, что цель отвечает 200 —
# то есть page-правило 1 из docs/SLO_ALERTS.md §4 имеет источник данных, а не только текст.
deadline=$((SECONDS + TIMEOUT_SECONDS))
while true; do
  probe="$(curl --fail --silent --show-error --get "$PROMETHEUS_URL/api/v1/query" \
    --data-urlencode 'query=probe_success{job="testapp-readiness"}' \
    | python3 -c 'import json,sys
result = json.load(sys.stdin)["data"]["result"]
print(result[0]["value"][1] if result else "missing")')"
  if [ "$probe" = "1" ]; then
    echo "Readiness probe reports the API as ready."
    break
  fi
  if [ "$SECONDS" -ge "$deadline" ]; then
    fail "probe_success{job=\"testapp-readiness\"} is '$probe' after ${TIMEOUT_SECONDS}s (expected 1)"
  fi
  sleep 3
done

# Правило без источника данных молчит ровно так же, как правило, у которого всё хорошо,
# поэтому проверяется и наличие самого правила в загруженном наборе.
rules_json="$(curl --fail --silent --show-error "$PROMETHEUS_URL/api/v1/rules")"
echo "$rules_json" | grep -q "TestAppReadinessProbeFailing" \
  || fail "Alert rule TestAppReadinessProbeFailing is not loaded in Prometheus"
echo "Readiness alert rule is loaded."

echo "== 6. Checking Grafana health =="
grafana_health="$(curl --fail --silent --show-error "$GRAFANA_URL/api/health" | python3 -c 'import json,sys;print(json.load(sys.stdin)["database"])')"
[ "$grafana_health" = "ok" ] || fail "Grafana health check reported database=$grafana_health"

echo "== 7. Checking the provisioned Prometheus datasource is queryable end to end =="
ds_health_json="$(curl --fail --silent --show-error -u "$GRAFANA_USER:$GRAFANA_PASSWORD" \
  "$GRAFANA_URL/api/datasources/uid/testapp-prometheus/health")"
ds_status="$(echo "$ds_health_json" | python3 -c 'import json,sys;print(json.load(sys.stdin).get("status",""))')"
[ "$ds_status" = "OK" ] || fail "Grafana Prometheus datasource health is not OK: $ds_health_json"

echo "== 8. Checking the TestApp Overview dashboard is provisioned =="
dashboard_json="$(curl --fail --silent --show-error -u "$GRAFANA_USER:$GRAFANA_PASSWORD" \
  "$GRAFANA_URL/api/dashboards/uid/testapp-overview")"
panel_count="$(echo "$dashboard_json" | python3 -c 'import json,sys;d=json.load(sys.stdin);print(len([p for p in d["dashboard"]["panels"] if p.get("type") != "row"]))')"
[ "$panel_count" -ge 15 ] || fail "Expected at least 15 non-row panels on the TestApp Overview dashboard, found $panel_count"
echo "Dashboard provisioned with $panel_count panels."

echo "Observability stack validation passed."
