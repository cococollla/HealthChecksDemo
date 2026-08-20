using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Npgsql;
using ServiceA;

var builder = WebApplication.CreateBuilder(args);

// Health checks вынесены в отдельный файл, чтобы не расширять appsettings.json.
// Переменные окружения добавляются повторно после JSON: так connection string
// можно заменить при развертывании, не сохраняя пароль в Docker config.
builder.Configuration
    .AddJsonFile("healthCheck.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables();

var healthSection = builder.Configuration.GetRequiredSection(HealthCheckSettings.SectionName);
var healthSettings = healthSection.Get<HealthCheckSettings>()
    ?? throw new InvalidOperationException("Секция HealthChecks не найдена.");

healthSettings = healthSettings with
{
    Dependencies = healthSection
        .GetSection(nameof(HealthCheckSettings.Dependencies))
        .GetChildren()
        .ToArray()
};

healthSettings.Validate();

var externalResources = builder.Configuration
    .GetRequiredSection(ExternalResourceSettings.SectionName)
    .Get<ExternalResourceSettings>()
    ?? throw new InvalidOperationException("Секция ExternalResources не найдена.");

externalResources.Validate();

// Подключение к PostgreSQL принадлежит приложению и регистрируется независимо
// от health checks. Проверка только использует готовый NpgsqlDataSource из DI.
builder.Services.AddSingleton(externalResources);
builder.Services.AddSingleton<NpgsqlDataSource>(_ =>
    NpgsqlDataSource.Create(externalResources.PostgreSql.ConnectionString));

// Extension-метод регистрирует только проверки. Он не создает БД и не знает
// connection string PostgreSQL.
builder.Services.AddConfiguredHealthChecks(healthSettings.Dependencies);

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    service = "service-a",
    message = "Service A is running."
}));

// Liveness намеренно не запускает проверки зависимостей. Если БД недоступна,
// Docker Swarm не должен бесконечно перезапускать исправный процесс API.
MapHealthChecksIfEnabled(healthSettings.Endpoints.Live, new HealthCheckOptions
{
    Predicate = _ => false
});

// Readiness выполняет только проверки с тегом readiness.
MapHealthChecksIfEnabled(healthSettings.Endpoints.Ready, new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("readiness")
});

// Detailed endpoint выполняет все проверки. ResponseWriter получает стандартный
// HealthReport от ASP.NET Core и превращает его в удобный JSON.
MapHealthChecksIfEnabled(healthSettings.Endpoints.Detailed, new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = (context, report) =>
        HealthResponseWriter.WriteAsync(context, report, healthSettings.Service)
});

MapHealthChecksIfEnabled(healthSettings.Endpoints.Cache, new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("cache"),
    ResponseWriter = (context, report) =>
        HealthResponseWriter.WriteAsync(context, report, healthSettings.Service)
});

MapHealthChecksIfEnabled(healthSettings.Endpoints.Database, new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("database"),
    ResponseWriter = (context, report) =>
        HealthResponseWriter.WriteAsync(context, report, healthSettings.Service)
});

app.Run();

void MapHealthChecksIfEnabled(EndpointOptions endpoint, HealthCheckOptions options)
{
    if (endpoint.Disabled)
    {
        return;
    }

    app.MapHealthChecks(endpoint.Url!, options);
}
