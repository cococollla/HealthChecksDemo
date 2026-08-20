using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ServiceB;

// Microsoft рекомендует строить пользовательский JSON из HealthReport.
// RFC draft учитывается только частично: используется media type
// application/health+json и дополнительные сведения о компонентах.
internal static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

    public static Task WriteAsync(
        HttpContext context,
        HealthReport report,
        ServiceMetadataSettings service,
        IReadOnlyCollection<DependencyHealthCheckSettings> dependencies)
    {
        context.Response.ContentType = "application/health+json";

        // Статусы не преобразуются: клиент получает нативные значения ASP.NET Core
        // Healthy, Degraded или Unhealthy.
        var response = new
        {
            status = report.Status.ToString(),
            service = new
            {
                id = service.ServiceId,
                description = service.Description,
                version = service.Version,
                releaseId = service.ReleaseId
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
                    // Метаданные добавляются из конфигурации даже для готовых
                    // проверок вроде AddRedis, которые сами Data не заполняют.
                    data = CreateData(
                        entry.Value.Data,
                        dependencies.First(dependency =>
                            string.Equals(
                                dependency.Name,
                                entry.Key,
                                StringComparison.OrdinalIgnoreCase)))
                })
        };

        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            response,
            SerializerOptions,
            context.RequestAborted);
    }

    private static IReadOnlyDictionary<string, object> CreateData(
        IReadOnlyDictionary<string, object> healthCheckData,
        DependencyHealthCheckSettings dependency)
    {
        var data = new Dictionary<string, object>(
            healthCheckData,
            StringComparer.OrdinalIgnoreCase)
        {
            ["componentId"] = dependency.ComponentId,
            ["componentType"] = dependency.ComponentType
        };

        return data;
    }
}
