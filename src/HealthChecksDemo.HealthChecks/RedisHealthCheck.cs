using System.Collections.Concurrent;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace HealthChecksDemo.HealthChecks;

internal sealed class RedisHealthCheck(
    RedisHealthCheckSettings settings) : IHealthCheck
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<ConnectionMultiplexer>>> Connections =
        new(StringComparer.Ordinal);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await GetConnectionAsync(cancellationToken);

            foreach (var endPoint in connection.GetEndPoints(configuredOnly: true))
            {
                var server = connection.GetServer(endPoint);

                if (server.ServerType != ServerType.Cluster)
                {
                    await connection.GetDatabase().PingAsync().WaitAsync(cancellationToken);
                    await server.PingAsync().WaitAsync(cancellationToken);
                    continue;
                }

                var clusterInfo = await server
                    .ExecuteAsync("CLUSTER", "INFO")
                    .WaitAsync(cancellationToken);

                if (clusterInfo.IsNull)
                {
                    return Failure(
                        context,
                        $"Redis cluster info недоступен для endpoint '{endPoint}'.");
                }

                if (!clusterInfo.ToString()!.Contains("cluster_state:ok", StringComparison.OrdinalIgnoreCase))
                {
                    return Failure(
                        context,
                        $"Redis cluster для endpoint '{endPoint}' не находится в состоянии OK.");
                }
            }

            return HealthCheckResult.Healthy(
                "Redis доступен и успешно прошел проверку endpoint'ов.",
                CreateComponentData());
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Failure(context, "Истекло время ожидания проверки Redis.", exception);
        }
        catch (RedisException exception)
        {
            ResetCachedConnection();
            return Failure(context, "Redis недоступен или не отвечает на ping.", exception);
        }
        catch (Exception exception)
        {
            ResetCachedConnection();
            return Failure(context, "Redis завершил проверку с неожиданной ошибкой.", exception);
        }
    }

    private async Task<IConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken)
    {
        var lazyConnection = Connections.GetOrAdd(
            settings.ConnectionString,
            static connectionString => new Lazy<Task<ConnectionMultiplexer>>(
                () => ConnectionMultiplexer.ConnectAsync(connectionString)));

        try
        {
            return await lazyConnection.Value.WaitAsync(cancellationToken);
        }
        catch
        {
            Connections.TryRemove(settings.ConnectionString, out _);
            throw;
        }
    }

    private void ResetCachedConnection()
    {
        if (!Connections.TryRemove(settings.ConnectionString, out var lazyConnection)
            || !lazyConnection.IsValueCreated
            || !lazyConnection.Value.IsCompletedSuccessfully)
        {
            return;
        }

        lazyConnection.Value.Result.Dispose();
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
