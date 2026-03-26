using System.Text.Json;

namespace McpServer.OpenApi;

/// <summary>
/// Executes one normalized OpenAPI endpoint request.
/// </summary>
internal interface IOpenApiRequestInvoker
{
    /// <summary>
    /// Executes a normalized endpoint using the provided JSON arguments.
    /// </summary>
    Task<object?> InvokeAsync(OpenApiToolCatalogBuilder.EndpointDefinition endpoint, JsonElement arguments, CancellationToken ct);
}