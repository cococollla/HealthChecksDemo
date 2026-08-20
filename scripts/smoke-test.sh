#!/usr/bin/env bash
set -euo pipefail

service_a_url="${SERVICE_A_URL:-http://localhost:8081}"
service_b_url="${SERVICE_B_URL:-http://localhost:8082}"

check_endpoint() {
  local name="$1"
  local url="$2"
  local expected_code="$3"
  local expected_health_status="${4:-}"
  local required_check="${5:-}"
  local response_file
  response_file="$(mktemp)"

  local actual_code
  actual_code="$(curl --silent --show-error --output "$response_file" --write-out '%{http_code}' "$url")"

  if [[ "$actual_code" != "$expected_code" ]]; then
    echo "FAIL: $name returned HTTP $actual_code, expected $expected_code"
    sed -n '1,80p' "$response_file"
    rm -f "$response_file"
    return 1
  fi

  if [[ -n "$expected_health_status" ]] &&
    ! jq --exit-status \
      --arg status "$expected_health_status" \
      --arg requiredCheck "$required_check" \
      '
        .status == $status
        and (.checks | type == "object")
        and (
          $requiredCheck == ""
          or .checks[$requiredCheck].status == "Healthy"
        )
      ' \
      "$response_file" >/dev/null; then
    echo "FAIL: $name returned an unexpected health+json document"
    sed -n '1,80p' "$response_file"
    rm -f "$response_file"
    return 1
  fi

  echo "PASS: $name returned HTTP $actual_code"
  sed -n '1,80p' "$response_file"
  rm -f "$response_file"
}

check_endpoint "Service A liveness" "$service_a_url/health/live" "200"
check_endpoint "Service A readiness" "$service_a_url/health/ready" "200"
check_endpoint "Service A detailed" "$service_a_url/health" "200" "Healthy" "redis-cache"
check_endpoint "Service B liveness" "$service_b_url/health/live" "200"
check_endpoint "Service B readiness" "$service_b_url/health/ready" "200"
check_endpoint "Service B detailed" "$service_b_url/health" "200" "Healthy" "redis-cache"
