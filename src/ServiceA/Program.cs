using HealthChecksDemo.HealthChecks;
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

app.UseConfiguredHealthChecks(healthSettings);

app.Run();
