using HealthChecksDemo.HealthChecks;
using Npgsql;
using ServiceB;

var builder = WebApplication.CreateBuilder(args);

// Вся конфигурация health checks находится в отдельном healthCheck.json.
// Переменные окружения имеют больший приоритет и подходят для секретов.
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

app.UseConfiguredHealthChecks(healthSettings);

app.Run();
