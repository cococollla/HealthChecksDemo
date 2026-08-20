using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Npgsql;
using ServiceB;

var builder = WebApplication.CreateBuilder(args);

// Вся конфигурация health checks находится в отдельном healthCheck.json.
// Переменные окружения имеют больший приоритет и подходят для секретов.
builder.Configuration
    .AddJsonFile("healthCheck.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables();

var healthSettings = builder.Configuration
    .GetRequiredSection(HealthCheckSettings.SectionName)
    .Get<HealthCheckSettings>()
    ?? throw new InvalidOperationException("Секция HealthChecks не найдена.");

healthSettings.Validate();

var externalResources = builder.Configuration
    .GetRequiredSection(ExternalResourceSettings.SectionName)
    .Get<ExternalResourceSettings>()
    ?? throw new InvalidOperationException("Секция ExternalResources не найдена.");

externalResources.Validate();

// NpgsqlDataSource является инфраструктурной зависимостью самого приложения.
// Health-check extension только получает уже зарегистрированный объект из DI.
builder.Services.AddSingleton(externalResources);
builder.Services.AddSingleton<NpgsqlDataSource>(_ =>
    NpgsqlDataSource.Create(externalResources.PostgreSql.ConnectionString));

// Extension-метод регистрирует только проверки, но не подключения приложения.
builder.Services.AddConfiguredHealthChecks(healthSettings.Dependencies);

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    service = "service-b",
    message = "Service B is running."
}));

// Liveness не проверяет БД и Service A, поэтому отказ зависимости
// не приводит к автоматическому перезапуску контейнера Service B.
app.MapHealthChecks(healthSettings.Endpoints.Live, new HealthCheckOptions
{
    Predicate = _ => false
});

// Readiness выполняет проверки, которые помечены тегом readiness.
app.MapHealthChecks(healthSettings.Endpoints.Ready, new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("readiness")
});

// Detailed endpoint выполняет все проверки и сериализует стандартный HealthReport.
app.MapHealthChecks(healthSettings.Endpoints.Detailed, new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = (context, report) =>
        HealthResponseWriter.WriteAsync(
            context,
            report,
            healthSettings.Service,
            healthSettings.Dependencies)
});

app.Run();
