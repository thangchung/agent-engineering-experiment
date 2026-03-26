using Microsoft.OpenApi.Models;

namespace McpServer.OpenApi;

/// <summary>
/// Loads and parses one OpenAPI document from a local file path or remote URL.
/// </summary>
internal interface IOpenApiDocumentLoader
{
    /// <summary>
    /// Loads a parsed OpenAPI document together with its source metadata.
    /// </summary>
    Task<OpenApiSourceDocument> LoadAsync(OpenApiToolCatalogBuilder.OpenApiSourceDefinition source, CancellationToken ct);
}

/// <summary>
/// Parsed OpenAPI source together with its originating document URI, if any.
/// </summary>
internal sealed record OpenApiSourceDocument(
    OpenApiToolCatalogBuilder.OpenApiSourceDefinition Source,
    OpenApiDocument Document,
    Uri? DocumentUri);