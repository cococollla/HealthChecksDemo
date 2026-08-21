using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HealthChecksDemo.HealthChecks;

internal sealed class HttpDependencyHealthCheck(
    IHttpClientFactory httpClientFactory,
    ExternalApplicationHealthCheckSettings settings) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, settings.Endpoint);

            var client = httpClientFactory.CreateClient(settings.Name);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var statusCode = (int)response.StatusCode;
            return statusCode == StatusCodes.Status200OK
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
