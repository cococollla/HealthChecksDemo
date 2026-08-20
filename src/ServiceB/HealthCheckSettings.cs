using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ServiceB;

// Модель полностью поддерживается стандартным ConfigurationBinder:
// один массив, один конкретный DTO и никаких ручных фабрик наследников.
public sealed class HealthCheckSettings
{
    public const string SectionName = "HealthChecks";

    public ServiceMetadataSettings Service { get; set; } = new();

    public HealthEndpointSettings Endpoints { get; set; } = new();

    public DependencyHealthCheckSettings[] Dependencies { get; set; } = [];

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
            dependency.Validate();
        }

        var duplicateName = Dependencies
            .GroupBy(dependency => dependency.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicateName is not null)
        {
            throw new InvalidOperationException(
                $"Имя health check '{duplicateName}' встречается больше одного раза.");
        }

        if (Dependencies.Count(dependency =>
                dependency.Type == DependencyType.Http
                && dependency.Http?.StartupWait?.Enabled == true) > 1)
        {
            throw new InvalidOperationException(
                "StartupWait может быть включен только для одной HTTP-зависимости.");
        }
    }
}

public sealed class ServiceMetadataSettings
{
    public string ServiceId { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string ReleaseId { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ServiceId))
        {
            throw new InvalidOperationException("HealthChecks:Service:ServiceId обязателен.");
        }
    }
}

public sealed class HealthEndpointSettings
{
    public string Live { get; set; } = "/health/live";

    public string Ready { get; set; } = "/health/ready";

    public string Detailed { get; set; } = "/health";

    public void Validate()
    {
        var paths = new[] { Live, Ready, Detailed };

        if (paths.Any(path => string.IsNullOrWhiteSpace(path) || !path.StartsWith('/')))
        {
            throw new InvalidOperationException(
                "Все health-check endpoints должны начинаться с '/'.");
        }

        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
        {
            throw new InvalidOperationException("Health-check endpoints должны быть уникальны.");
        }
    }
}

public enum DependencyType
{
    Unknown,
    PostgreSql,
    Redis,
    Http
}

/// <summary>
/// Общие параметры одной проверки из массива Dependencies.
/// </summary>
public sealed class DependencyHealthCheckSettings
{
    public DependencyType Type { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ComponentId { get; set; } = string.Empty;

    public string ComponentType { get; set; } = "component";

    public string FailureStatus { get; set; } = nameof(HealthStatus.Unhealthy);

    public int TimeoutSeconds { get; set; } = 3;

    public string[] Tags { get; set; } = ["readiness"];

    // Специфичные HTTP-параметры сгруппированы отдельно. Сам блок nullable,
    // поскольку он допустим только для Type = Http; Url внутри блока не nullable.
    public HttpHealthCheckSettings? Http { get; set; }

    public HealthStatus GetFailureStatus() =>
        Enum.TryParse<HealthStatus>(FailureStatus, ignoreCase: true, out var status)
            ? status
            : throw new InvalidOperationException(
                $"Неизвестный FailureStatus '{FailureStatus}' для проверки '{Name}'.");

    public TimeSpan GetTimeout() => TimeSpan.FromSeconds(TimeoutSeconds);

    public void Validate()
    {
        if (Type == DependencyType.Unknown)
        {
            throw new InvalidOperationException(
                $"Для проверки '{Name}' требуется поддерживаемый Type.");
        }

        if (string.IsNullOrWhiteSpace(Name)
            || string.IsNullOrWhiteSpace(ComponentId)
            || string.IsNullOrWhiteSpace(ComponentType))
        {
            throw new InvalidOperationException(
                "Для каждой проверки обязательны Name, ComponentId и ComponentType.");
        }

        if (TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"TimeoutSeconds для проверки '{Name}' должен быть больше нуля.");
        }

        if (Tags.Length == 0)
        {
            throw new InvalidOperationException(
                $"Проверка '{Name}' должна содержать хотя бы один тег.");
        }

        _ = GetFailureStatus();

        if (Type == DependencyType.Http)
        {
            if (Http is null)
            {
                throw new InvalidOperationException(
                    $"Для HTTP-проверки '{Name}' требуется секция Http.");
            }

            Http.Validate(Name);
        }
        else if (Http is not null)
        {
            throw new InvalidOperationException(
                $"Секция Http допустима только для HTTP-проверки, но указана у '{Name}'.");
        }
    }
}

/// <summary>
/// Параметры, которые нужны только проверке HTTP endpoint.
/// </summary>
public sealed class HttpHealthCheckSettings
{
    public string Url { get; set; } = string.Empty;

    public string Method { get; set; } = HttpMethod.Get.Method;

    public int[] ExpectedStatusCodes { get; set; } = [StatusCodes.Status200OK];

    public string? UserAgent { get; set; }

    public StartupWaitSettings? StartupWait { get; set; }

    public void Validate(string dependencyName)
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                $"Url для HTTP-проверки '{dependencyName}' должен быть абсолютным HTTP(S) URL.");
        }

        if (string.IsNullOrWhiteSpace(Method))
        {
            throw new InvalidOperationException(
                $"HTTP Method для проверки '{dependencyName}' обязателен.");
        }

        if (ExpectedStatusCodes.Length == 0
            || ExpectedStatusCodes.Any(code => code is < 100 or > 599))
        {
            throw new InvalidOperationException(
                $"ExpectedStatusCodes проверки '{dependencyName}' содержит некорректные значения.");
        }

        StartupWait?.Validate(dependencyName);
    }
}

public sealed class StartupWaitSettings
{
    public bool Enabled { get; set; }

    public int TimeoutSeconds { get; set; } = 120;

    public int RetryDelaySeconds { get; set; } = 2;

    public int RequestTimeoutSeconds { get; set; } = 5;

    public void Validate(string dependencyName)
    {
        if (!Enabled)
        {
            return;
        }

        if (TimeoutSeconds <= 0 || RetryDelaySeconds <= 0 || RequestTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"Параметры StartupWait зависимости '{dependencyName}' должны быть больше нуля.");
        }
    }
}
