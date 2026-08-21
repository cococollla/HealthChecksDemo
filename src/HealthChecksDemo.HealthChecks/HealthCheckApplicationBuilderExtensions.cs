using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace HealthChecksDemo.HealthChecks;

public static class HealthCheckApplicationBuilderExtensions
{
    public static WebApplication UseConfiguredHealthChecks(
        this WebApplication app,
        HealthCheckSettings healthSettings)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(healthSettings);

        if (!healthSettings.Endpoints.Live.Disabled)
        {
            app.MapHealthChecks(healthSettings.Endpoints.Live.Url ?? "/health/live", new HealthCheckOptions
            {
                Predicate = _ => false
            });
        }

        if (!healthSettings.Endpoints.Ready.Disabled)
        {
            app.MapHealthChecks(healthSettings.Endpoints.Ready.Url ?? "/health/ready", new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("readiness")
            });
        }

        if (!healthSettings.Endpoints.Detailed.Disabled)
        {
            app.MapHealthChecks(healthSettings.Endpoints.Detailed.Url ?? "/health", new HealthCheckOptions
            {
                Predicate = _ => true,
                ResponseWriter = (context, report) =>
                    HealthResponseWriter.WriteAsync(context, report, healthSettings.Service)
            });
        }

        if (!healthSettings.Endpoints.Cache.Disabled)
        {
            app.MapHealthChecks(healthSettings.Endpoints.Cache.Url ?? "/health/cache", new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("cache"),
                ResponseWriter = (context, report) =>
                    HealthResponseWriter.WriteAsync(context, report, healthSettings.Service)
            });
        }

        if (!healthSettings.Endpoints.Database.Disabled)
        {
            app.MapHealthChecks(healthSettings.Endpoints.Database.Url ?? "/health/database", new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("database"),
                ResponseWriter = (context, report) =>
                    HealthResponseWriter.WriteAsync(context, report, healthSettings.Service)
            });
        }

        return app;
    }
}
