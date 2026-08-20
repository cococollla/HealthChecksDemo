using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace ServiceB;

// Проверка подтверждает и подключение к PostgreSQL, и выполнение простого запроса.
internal sealed class PostgreSqlHealthCheck(
    NpgsqlDataSource dataSource,
    DependencyHealthCheckSettings settings) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

            await using var command = new NpgsqlCommand("SELECT 1", connection);
            var result = await command.ExecuteScalarAsync(cancellationToken);

            if (result is not int value || value != 1)
            {
                return Failure(
                    context,
                    "PostgreSQL выполнил проверочный запрос, но вернул неожиданный результат.");
            }

            return HealthCheckResult.Healthy(
                "PostgreSQL доступен и успешно выполняет запросы.",
                CreateComponentData());
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Failure(context, "Истекло время ожидания проверки PostgreSQL.", exception);
        }
        catch (NpgsqlException exception)
        {
            return Failure(
                context,
                "PostgreSQL недоступен или не может выполнить проверочный запрос.",
                exception);
        }
    }

    private HealthCheckResult Failure(
        HealthCheckContext context,
        string description,
        Exception? exception = null) =>
        new(
            context.Registration.FailureStatus,
            description,
            exception,
            CreateComponentData());

    private IReadOnlyDictionary<string, object> CreateComponentData() =>
        new Dictionary<string, object>
        {
            ["componentId"] = settings.ComponentId,
            ["componentType"] = settings.ComponentType
        };
}
