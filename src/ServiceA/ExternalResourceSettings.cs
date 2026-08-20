namespace ServiceA;

// Это конфигурация подключений приложения, а не параметры health checks.
// В реальном сервисе эти настройки обычно уже существуют отдельно.
public sealed record ExternalResourceSettings
{
    public const string SectionName = "ExternalResources";

    public PostgreSqlConnectionSettings PostgreSql { get; init; } = new();

    public RedisConnectionSettings Redis { get; init; } = new();

    public void Validate()
    {
        PostgreSql.Validate();
        Redis.Validate();
    }
}

public sealed record PostgreSqlConnectionSettings
{
    public string ConnectionString { get; init; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException(
                "ExternalResources:PostgreSql:ConnectionString обязателен.");
        }
    }
}

public sealed record RedisConnectionSettings
{
    public string ConnectionString { get; init; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException(
                "ExternalResources:Redis:ConnectionString обязателен.");
        }
    }
}
