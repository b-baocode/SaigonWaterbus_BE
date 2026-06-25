using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace SaigonWaterbus.Web.Infrastructure;

///
internal sealed class ApiExceptionOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        operation.Responses ??= [];
        operation.Responses.TryAdd("400", new OpenApiResponse { Description = "Bad Request" });

        return Task.CompletedTask;
    }
}
