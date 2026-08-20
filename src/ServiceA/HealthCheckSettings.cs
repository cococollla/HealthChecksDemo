using Microsoft.Extensions.Configuration;

namespace ServiceA;

public sealed record HealthCheckSettings
{
    public const string SectionName = "HealthChecks";

    public ServiceMetadataSettings Service { get; init; } = new();

    public HealthEndpointOptions Endpoints { get; init; } = new();

    public IConfigurationSection[] Dependencies { get; init; } = [];

    public void Validate()
    {
        Service.Validate();
        Endpoints.Validate();

        if (Dependencies.Length == 0)
        {
            throw new InvalidOperationException(
                "HealthChecks:Dependencies должен содержать хотя бы одну проверку.");
        }

        foreach (var dependency in Dependencies)
        {
            ValidateDependency(dependency);
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

    private static void ValidateDependency(IConfigurationSection dependency)
    {
        switch (dependency.GetValue<DependencyType>("Type"))
        {
            case DependencyType.PostgreSql:
                dependency.Get<PostgresHealthCheckSettings>()?.Validate();
                break;

            case DependencyType.Redis:
                dependency.Get<RedisHealthCheckSettings>()?.Validate();
                break;

            case DependencyType.Service:
                dependency.Get<ExternalApplicationHealthCheckSettings>()?.Validate();
                break;

            default:
                throw new InvalidOperationException(
                    $"HealthChecks:Dependencies:{dependency.Key}:Type должен быть поддерживаемым.");
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

    public void Validate()
    {
        if (Type != DependencyType.Redis)
        {
            throw new InvalidOperationException(
                $"Проверка '{Name}' должна иметь Type = Redis.");
        }

        HealthCheckValidation.ValidateCommon(Name, ComponentId, ComponentType, Tags);

        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionString для Redis-проверки '{Name}' обязателен.");
        }
    }
}

public sealed record PostgresHealthCheckSettings
{
    public DependencyType Type { get; init; }

    public string Name { get; init; } = string.Empty;

    public string ConnectionString { get; init; } = string.Empty;

    public string ComponentId { get; init; } = string.Empty;

    public string ComponentType { get; init; } = string.Empty;

    public string[] Tags { get; init; } = ["readiness", "database"];

    public void Validate()
    {
        if (Type != DependencyType.PostgreSql)
        {
            throw new InvalidOperationException(
                $"Проверка '{Name}' должна иметь Type = PostgreSql.");
        }

        HealthCheckValidation.ValidateCommon(Name, ComponentId, ComponentType, Tags);

        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionString для PostgreSQL-проверки '{Name}' обязателен.");
        }
    }
}

public sealed record ExternalApplicationHealthCheckSettings
{
    public DependencyType Type { get; init; }

    public string Name { get; init; } = string.Empty;

    public string ComponentId { get; init; } = string.Empty;

    public string ComponentType { get; init; } = string.Empty;

    public string[] Tags { get; init; } = ["readiness", "service"];

    public string Endpoint { get; init; } = string.Empty;

    public void Validate()
    {
        if (Type != DependencyType.Service)
        {
            throw new InvalidOperationException(
                $"Проверка '{Name}' должна иметь Type = Service.");
        }

        HealthCheckValidation.ValidateCommon(Name, ComponentId, ComponentType, Tags);

        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                $"Endpoint для сервисной проверки '{Name}' должен быть абсолютным HTTP(S) URL.");
        }
    }
}

public sealed record ServiceMetadataSettings
{
    public string ServiceId { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string ReleaseId { get; init; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ServiceId))
        {
            throw new InvalidOperationException("HealthChecks:Service:ServiceId обязателен.");
        }
    }
}

public sealed record EndpointOptions
{
    public bool Disabled { get; init; }

    public string? Url { get; init; }

    public void Validate(string endpointName)
    {
        if (Disabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Url) || !Url.StartsWith('/'))
        {
            throw new InvalidOperationException(
                $"HealthChecks:Endpoints:{endpointName}:Url должен начинаться с '/'.");
        }
    }
}

public sealed record HealthEndpointOptions
{
    public EndpointOptions Live { get; init; } = new();

    public EndpointOptions Ready { get; init; } = new();

    public EndpointOptions Detailed { get; init; } = new();

    public EndpointOptions Cache { get; init; } = new();

    public EndpointOptions Database { get; init; } = new();

    public void Validate()
    {
        Live.Validate(nameof(Live));
        Ready.Validate(nameof(Ready));
        Detailed.Validate(nameof(Detailed));
        Cache.Validate(nameof(Cache));
        Database.Validate(nameof(Database));

        var enabledPaths = new[] { Live, Ready, Detailed, Cache, Database }
            .Where(static endpoint => !endpoint.Disabled)
            .Select(static endpoint => endpoint.Url!)
            .ToArray();

        if (enabledPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != enabledPaths.Length)
        {
            throw new InvalidOperationException("Health-check endpoints должны быть уникальны.");
        }
    }
}

internal static class HealthCheckValidation
{
    public static void ValidateCommon(
        string name,
        string componentId,
        string componentType,
        string[] tags)
    {
        if (string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(componentId)
            || string.IsNullOrWhiteSpace(componentType))
        {
            throw new InvalidOperationException(
                "Для каждой проверки обязательны Name, ComponentId и ComponentType.");
        }

        if (tags.Length == 0)
        {
            throw new InvalidOperationException(
                $"Проверка '{name}' должна содержать хотя бы один тег.");
        }
    }
}
