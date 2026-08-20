using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ServiceB;

// Универсальная HTTP-проверка. В текущей конфигурации она вызывает readiness
// Service A, но тот же класс можно использовать для любого HTTP-компонента.
internal sealed class HttpDependencyHealthCheck(
    IHttpClientFactory httpClientFactory,
    DependencyHealthCheckSettings settings) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var http = settings.Http
                ?? throw new InvalidOperationException(
                    $"Для HTTP-проверки '{settings.Name}' требуется секция Http.");

            using var request = new HttpRequestMessage(
                new HttpMethod(http.Method),
                http.Url);

            var client = httpClientFactory.CreateClient(settings.Name);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var statusCode = (int)response.StatusCode;
            return http.ExpectedStatusCodes.Contains(statusCode)
                ? HealthCheckResult.Healthy(
                    $"Компонент '{settings.Name}' вернул ожидаемый HTTP status {statusCode}.",
                    CreateComponentData())
                : Failure(
                    context,
                    $"Компонент '{settings.Name}' вернул неожиданный HTTP status {statusCode}.");
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            return Failure(
                context,
                $"Истекло время ожидания ответа компонента '{settings.Name}'.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            return Failure(
                context,
                $"Компонент '{settings.Name}' недоступен по HTTP.",
                exception);
        }
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
