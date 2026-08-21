using Microsoft.Extensions.Configuration;

namespace HealthChecksDemo.HealthChecks;

public sealed record HealthCheckSettings
{
    public const string SectionName = "HealthChecks";

    public ServiceMetadataSettings Service { get; init; } = new();

    public HealthEndpointOptions Endpoints { get; init; } = new();

    public IConfigurationSection[] Dependencies { get; init; } = [];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Service.ServiceId))
        {
            throw new InvalidOperationException("HealthChecks:Service:ServiceId обязателен.");
        }

        var enabledPaths = new[]
        {
            Endpoints.Live,
            Endpoints.Ready,
            Endpoints.Detailed,
            Endpoints.Cache,
            Endpoints.Database
        }
        .Where(static endpoint => !endpoint.Disabled)
        .Select(static endpoint => endpoint.Url)
        .ToArray();

        if (enabledPaths.Any(static path => string.IsNullOrWhiteSpace(path) || !path!.StartsWith('/')))
        {
            throw new InvalidOperationException(
                "Все включенные health-check endpoints должны начинаться с '/'.");
        }

        if (enabledPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != enabledPaths.Length)
        {
            throw new InvalidOperationException("Health-check endpoints должны быть уникальны.");
        }

        if (Dependencies.Length == 0)
        {
            throw new InvalidOperationException(
                "HealthChecks:Dependencies должен содержать хотя бы одну проверку.");
        }

        var duplicateName = Dependencies
            .Select(static dependency => dependency.GetValue<string>("Name"))
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .GroupBy(static name => name!, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1)
            ?.Key;

        if (duplicateName is not null)
        {
            throw new InvalidOperationException(
                $"Имя health check '{duplicateName}' встречается больше одного раза.");
        }
    }
}

public enum DependencyType
{
    Unknown,
    PostgreSql,
    Redis,
    Service
}

public sealed record RedisHealthCheckSettings
{
    public DependencyType Type { get; init; }

    public string Name { get; init; } = string.Empty;

    public string ConnectionString { get; init; } = string.Empty;

    public string ComponentId { get; init; } = string.Empty;

    public string ComponentType { get; init; } = string.Empty;

    public string[] Tags { get; init; } = ["readiness", "cache"];
}

public sealed record PostgresHealthCheckSettings
{
    public DependencyType Type { get; init; }

    public string Name { get; init; } = string.Empty;

    public string ConnectionString { get; init; } = string.Empty;

    public string ComponentId { get; init; } = string.Empty;

    public string ComponentType { get; init; } = string.Empty;

    public string[] Tags { get; init; } = ["readiness", "database"];
}

public sealed record ExternalApplicationHealthCheckSettings
{
    public DependencyType Type { get; init; }

    public string Name { get; init; } = string.Empty;

    public string ComponentId { get; init; } = string.Empty;

    public string ComponentType { get; init; } = string.Empty;

    public string[] Tags { get; init; } = ["readiness", "service"];

    public string Endpoint { get; init; } = string.Empty;
}

public sealed record ServiceMetadataSettings
{
    public string ServiceId { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string ReleaseId { get; init; } = string.Empty;
}

public sealed record EndpointOptions
{
    public bool Disabled { get; init; } = true;

    public string? Url { get; init; }
}

public sealed record HealthEndpointOptions
{
    public EndpointOptions Live { get; init; } = new();

    public EndpointOptions Ready { get; init; } = new();

    public EndpointOptions Detailed { get; init; } = new();

    public EndpointOptions Cache { get; init; } = new();

    public EndpointOptions Database { get; init; } = new();
}
