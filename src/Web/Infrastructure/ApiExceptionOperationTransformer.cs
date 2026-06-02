using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace SaigonWaterbus.Web.Infrastructure;

/// <summary>
/// Adds standard error responses to every OpenAPI operation. A 400 Bad Request is added to all
/// operations because application services validate request DTOs before executing.
/// </summary>
internal sealed class ApiExceptionOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        operation.Responses ??= [];
        operation.Responses.TryAdd("400", new OpenApiResponse { Description = "Bad Request" });

        return Task.CompletedTask;
    }
}
