using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace HealthChecksDemo.HealthChecks;

public static class HealthCheckServiceCollectionExtensions
{
    public static IServiceCollection AddConfiguredHealthChecks(
        this IServiceCollection services,
        IConfigurationSection[] dependencies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dependencies);

        var healthChecks = services.AddHealthChecks();

        foreach (var dependency in dependencies)
        {
            switch (dependency.GetValue<DependencyType>("Type"))
            {
                case DependencyType.PostgreSql:
                {
                    PostgresHealthCheckSettings options = new();
                    dependency.Bind(options);

                    healthChecks.Add(new HealthCheckRegistration(
                        options.Name,
                        serviceProvider => new PostgreSqlHealthCheck(
                            serviceProvider.GetRequiredService<NpgsqlDataSource>(),
                            options),
                        HealthStatus.Healthy,
                        options.Tags));
                    break;
                }

                case DependencyType.Redis:
                {
                    RedisHealthCheckSettings options = new();
                    dependency.Bind(options);

                    healthChecks.Add(new HealthCheckRegistration(
                        options.Name,
                        _ => new RedisHealthCheck(options),
                        HealthStatus.Healthy,
                        options.Tags));
                    break;
                }

                case DependencyType.Service:
                    AddHttp(services, healthChecks, dependency);
                    break;

                default:
                    throw new InvalidOperationException("Неизвестный тип health check.");
            }
        }

        return services;
    }

    private static void AddHttp(
        IServiceCollection services,
        IHealthChecksBuilder healthChecks,
        IConfigurationSection dependency)
    {
        ExternalApplicationHealthCheckSettings options = new();
        dependency.Bind(options);

        services.AddHttpClient(options.Name, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        healthChecks.Add(new HealthCheckRegistration(
            options.Name,
            serviceProvider => new HttpDependencyHealthCheck(
                serviceProvider.GetRequiredService<IHttpClientFactory>(),
                options),
            HealthStatus.Healthy,
            options.Tags));
    }
}
