using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HealthChecksDemo.HealthChecks;

public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

    public static Task WriteAsync(
        HttpContext context,
        HealthReport report,
        ServiceMetadataSettings service)
    {
        context.Response.ContentType = "application/health+json";

        var response = new
        {
            status = report.Status.ToString(),
            service = new
            {
                id = service.ServiceId,
                description = service.Description
            },
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2),
                    tags = entry.Value.Tags.OrderBy(tag => tag),
                    data = entry.Value.Data
                })
        };

        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            response,
            SerializerOptions,
            context.RequestAborted);
    }
}
