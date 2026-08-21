namespace ServiceA;

// Это конфигурация подключений приложения, а не параметры health checks.
public sealed record ExternalResourceSettings
{
    public const string SectionName = "ExternalResources";

    public PostgreSqlConnectionSettings PostgreSql { get; init; } = new();

    public RedisConnectionSettings Redis { get; init; } = new();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PostgreSql.ConnectionString))
        {
            throw new InvalidOperationException(
                "ExternalResources:PostgreSql:ConnectionString обязателен.");
        }

        if (string.IsNullOrWhiteSpace(Redis.ConnectionString))
        {
            throw new InvalidOperationException(
                "ExternalResources:Redis:ConnectionString обязателен.");
        }
    }
}

public sealed record PostgreSqlConnectionSettings
{
    public string ConnectionString { get; init; } = string.Empty;
}

public sealed record RedisConnectionSettings
{
    public string ConnectionString { get; init; } = string.Empty;
}
