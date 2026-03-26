using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace McpServer.OpenApi;

/// <summary>
/// Default OpenAPI document loader for local files and remote HTTP(S) URLs.
/// </summary>
internal sealed class OpenApiDocumentLoader : IOpenApiDocumentLoader
{
    private static readonly HttpClient SpecHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10),  // 10-second timeout for remote specs
    };

    /// <inheritdoc/>
    public async Task<OpenApiSourceDocument> LoadAsync(OpenApiToolCatalogBuilder.OpenApiSourceDefinition source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Location);

        Stream stream;
        Uri? documentUri = null;

        if (Uri.TryCreate(source.Location, UriKind.Absolute, out Uri? absoluteUri)
            && (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
        {
            stream = await SpecHttpClient.GetStreamAsync(absoluteUri, ct);
            documentUri = absoluteUri;
        }
        else
        {
            string fullPath = Path.GetFullPath(source.Location);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Configured OpenAPI spec was not found: '{fullPath}'.");
            }

            stream = File.OpenRead(fullPath);
        }

        await using (stream.ConfigureAwait(false))
        {
            OpenApiDocument document = new OpenApiStreamReader().Read(stream, out OpenApiDiagnostic diagnostic);

            if (diagnostic.Errors.Count > 0)
            {
                string errors = string.Join("; ", diagnostic.Errors.Select(error => error.Message));
                throw new InvalidOperationException($"OpenAPI parse failed for '{source.Name}': {errors}");
            }

            return new OpenApiSourceDocument(source, document, documentUri);
        }
    }
}