using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace ServiceB;

internal static class HealthCheckServiceCollectionExtensions
{
    // Регистрирует все зависимости, перечисленные в healthCheck.json.
    // Extension-метод скрывает технические детали, но не выполняет валидацию:
    // HealthCheckSettings.Validate() уже вызван в Program.cs.
    public static IServiceCollection AddConfiguredHealthChecks(
        this IServiceCollection services,
        IEnumerable<DependencyHealthCheckSettings> dependencies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dependencies);

        var healthChecks = services.AddHealthChecks();

        // Любой новый элемент уже поддерживаемого Type автоматически
        // регистрируется после изменения JSON и перезапуска приложения.
        foreach (var dependency in dependencies)
        {
            switch (dependency.Type)
            {
                case DependencyType.PostgreSql:
                    AddPostgreSql(healthChecks, dependency);
                    break;

                case DependencyType.Redis:
                    AddRedis(healthChecks, dependency);
                    break;

                case DependencyType.Http:
                    AddHttp(services, healthChecks, dependency);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Type '{dependency.Type}' для проверки " +
                        $"'{dependency.Name}' не поддерживается.");
            }
        }

        return services;
    }

    private static void AddPostgreSql(
        IHealthChecksBuilder healthChecks,
        DependencyHealthCheckSettings dependency)
    {
        // NpgsqlDataSource уже зарегистрирован приложением. Здесь создается
        // только сама health check registration.
        healthChecks.Add(new HealthCheckRegistration(
            dependency.Name,
            serviceProvider => new PostgreSqlHealthCheck(
                serviceProvider.GetRequiredService<NpgsqlDataSource>(),
                dependency),
            dependency.GetFailureStatus(),
            dependency.Tags,
            dependency.GetTimeout()));
    }

    private static void AddRedis(
        IHealthChecksBuilder healthChecks,
        DependencyHealthCheckSettings dependency)
    {
        healthChecks.AddRedis(
            connectionStringFactory: serviceProvider =>
                serviceProvider
                    .GetRequiredService<ExternalResourceSettings>()
                    .Redis
                    .ConnectionString,
            name: dependency.Name,
            failureStatus: dependency.GetFailureStatus(),
            tags: dependency.Tags,
            timeout: dependency.GetTimeout());
    }

    private static void AddHttp(
        IServiceCollection services,
        IHealthChecksBuilder healthChecks,
        DependencyHealthCheckSettings dependency)
    {
        var http = dependency.Http
            ?? throw new InvalidOperationException(
                $"Для HTTP-проверки '{dependency.Name}' требуется секция Http.");

        // HttpClient не имеет собственного timeout: единый бюджет отмены
        // задается в HealthCheckRegistration через TimeoutSeconds.
        services.AddHttpClient(dependency.Name, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;

            if (!string.IsNullOrWhiteSpace(http.UserAgent))
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(http.UserAgent);
            }
        });

        healthChecks.Add(new HealthCheckRegistration(
            dependency.Name,
            serviceProvider => new HttpDependencyHealthCheck(
                serviceProvider.GetRequiredService<IHttpClientFactory>(),
                dependency),
            dependency.GetFailureStatus(),
            dependency.Tags,
            dependency.GetTimeout()));
    }
}
