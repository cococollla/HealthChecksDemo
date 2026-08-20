#!/bin/sh
set -eu

config_path="${HEALTH_CHECK_CONFIG_PATH:-/app/healthCheck.json}"

# Из массива выбирается единственная HTTP-зависимость с включенным StartupWait.
# Если такой зависимости нет, startup gate не используется.
wait_dependency="$(
  jq -c '
    [
      .HealthChecks.Dependencies[]
      | select(
          ((.Type | ascii_downcase) == "http")
          and (.Http.StartupWait.Enabled == true)
        )
    ]
    | if length > 1
      then error("StartupWait может быть включен только для одной HTTP-зависимости")
      else (.[0] // null)
      end
  ' "$config_path"
)"

if [ "$wait_dependency" = "null" ]; then
  echo "Ожидание HTTP-зависимости при старте отключено."
  exec dotnet ServiceB.dll
fi

dependency_name="$(printf '%s' "$wait_dependency" | jq -er '.Name')"
dependency_url="$(printf '%s' "$wait_dependency" | jq -er '.Http.Url')"
timeout_seconds="$(printf '%s' "$wait_dependency" | jq -er '.Http.StartupWait.TimeoutSeconds')"
retry_delay_seconds="$(printf '%s' "$wait_dependency" | jq -er '.Http.StartupWait.RetryDelaySeconds')"
request_timeout_seconds="$(printf '%s' "$wait_dependency" | jq -er '.Http.StartupWait.RequestTimeoutSeconds')"
started_at="$(date +%s)"

echo "Ожидание Healthy от $dependency_name: $dependency_url"

while true; do
  # /health/ready возвращает стандартное тело Healthy, Degraded или Unhealthy.
  # Проверяем и HTTP 2xx через --fail, и тело, чтобы Degraded не разрешал запуск.
  response="$(
    curl \
      --fail \
      --silent \
      --max-time "$request_timeout_seconds" \
      "$dependency_url" 2>/dev/null \
      || true
  )"

  if [ "$response" = "Healthy" ]; then
    echo "$dependency_name готов. Запуск Service B."

    # exec заменяет shell процессом dotnet, поэтому SIGTERM от Swarm
    # передается приложению напрямую и graceful shutdown продолжает работать.
    exec dotnet ServiceB.dll
  fi

  now="$(date +%s)"
  elapsed_seconds=$((now - started_at))

  if [ "$elapsed_seconds" -ge "$timeout_seconds" ]; then
    echo "$dependency_name не стал Healthy за ${timeout_seconds} секунд." >&2
    exit 1
  fi

  echo "$dependency_name пока не готов; повтор через ${retry_delay_seconds} с."
  sleep "$retry_delay_seconds"
done
