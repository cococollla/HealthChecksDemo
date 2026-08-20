using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace ServiceA;

internal static class HealthCheckServiceCollectionExtensions
{
    // Регистрирует все зависимости, перечисленные в healthCheck.json.
    // Extension-метод скрывает технические детали, но не выполняет валидацию:
    // HealthCheckSettings.Validate() уже вызван в Program.cs.
    public static IServiceCollection AddConfiguredHealthChecks(
        this IServiceCollection services,
        IConfigurationSection[] dependencies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dependencies);

        var healthChecks = services.AddHealthChecks();

        // Любой новый элемент уже поддерживаемого Type автоматически
        // регистрируется после изменения JSON и перезапуска приложения.
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
                    
                    healthChecks.AddRedis(
                        connectionStringFactory: serviceProvider =>
                            serviceProvider
                                .GetRequiredService<ExternalResourceSettings>()
                                .Redis
                                .ConnectionString,
                        name: options.Name,
                        failureStatus: HealthStatus.Healthy,
                        tags: options.Tags);
                    break;
                }
                case DependencyType.Service:
                    AddHttp(services, healthChecks, dependency);
                    break;

                default:
                    throw new InvalidOperationException();
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

        // HttpClient не имеет собственного timeout: единый бюджет отмены
        // задается в HealthCheckRegistration через TimeoutSeconds.
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
