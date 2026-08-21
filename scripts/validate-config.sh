#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
service_a_config="$project_root/src/ServiceA/healthCheck.json"
service_b_config="$project_root/src/ServiceB/healthCheck.json"

for config in "$service_a_config" "$service_b_config"; do
  jq --exit-status '
    def validCommonCheck:
      (.Name | length > 0)
      and (.Tags | length > 0)
      and (.ComponentId | length > 0)
      and (.ComponentType | length > 0);

    def validEndpoint:
      if (.Disabled // false) == true
      then true
      else (.Url | type == "string") and (.Url | startswith("/"))
      end;

    (.HealthChecks.Service.ServiceId | length > 0)
    and (.ExternalResources.PostgreSql.ConnectionString | length > 0)
    and (.ExternalResources.Redis.ConnectionString | length > 0)
    and (
      .HealthChecks.Endpoints
      | to_entries
      | all(.[]; (.value | validEndpoint))
    )
    and (
      [
        .HealthChecks.Endpoints.Live,
        .HealthChecks.Endpoints.Ready,
        .HealthChecks.Endpoints.Detailed,
        .HealthChecks.Endpoints.Cache?,
        .HealthChecks.Endpoints.Database?
      ]
      | map(select(. != null and ((.Disabled // false) == false)) | .Url)
      | length == (unique | length)
    )
    and (
      .HealthChecks.Dependencies as $dependencies
      | ($dependencies | length > 0)
        and all($dependencies[];
          validCommonCheck
          and (.Type as $type | ["PostgreSql", "Redis", "Http", "Service"] | index($type) != null)
          and (
            if .Type == "Http"
            then
              (.TimeoutSeconds > 0)
              and (
                .FailureStatus as $failureStatus
                | ["Healthy", "Degraded", "Unhealthy"]
                | index($failureStatus) != null
              )
              and (.Http.Url | test("^https?://"))
              and (.Http.Method | length > 0)
              and (.Http.ExpectedStatusCodes | length > 0)
            elif .Type == "Service"
            then
              (.Endpoint | test("^https?://"))
            else
              true
            end
          )
        )
        and (
          [$dependencies[].Name]
          | length == (unique | length)
        )
    )
  ' "$config" >/dev/null
done

jq --exit-status '
  [
    .HealthChecks.Dependencies[]
    | select(.Type == "Http" and .Http.StartupWait.Enabled == true)
  ] as $startupDependencies
  | ($startupDependencies | length == 1)
    and ($startupDependencies[0].Name == "service-a")
    and ($startupDependencies[0].Http.Url == "http://service-a:8080/health/ready")
    and ($startupDependencies[0].FailureStatus == "Unhealthy")
    and ($startupDependencies[0].Http.StartupWait.TimeoutSeconds > 0)
    and ($startupDependencies[0].Http.StartupWait.RetryDelaySeconds > 0)
    and ($startupDependencies[0].Http.StartupWait.RequestTimeoutSeconds > 0)
' "$service_b_config" >/dev/null

echo "healthCheck.json validation passed."
