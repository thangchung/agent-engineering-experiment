using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using McpServer.Registry;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace McpServer.OpenApi;

/// <summary>
/// Builds dynamic tool descriptors from an OpenAPI document.
/// </summary>
public static class OpenApiToolCatalogBuilder
{
    private static readonly IOpenApiDocumentLoader DefaultDocumentLoader = new OpenApiDocumentLoader();
    private static readonly IOpenApiRequestInvoker DefaultRequestInvoker = new OpenApiRequestInvoker();

    /// <summary>
    /// Describes one OpenAPI source loaded from a local file path or remote URL.
    /// </summary>
    /// <param name="Name">Stable source name used when disambiguating duplicate tool names.</param>
    /// <param name="Location">Absolute or relative file path, or absolute HTTP(S) URL, for the OpenAPI document.</param>
    /// <param name="BaseUrlOverride">Optional explicit base URL override used instead of the document's servers list.</param>
    public sealed record OpenApiSourceDefinition(string Name, string Location, string? BaseUrlOverride = null);

    /// <summary>
    /// Resolves the OpenAPI spec path from configuration, environment, output, or repository layout.
    /// </summary>
    public static string ResolveSpecPath(IConfiguration configuration, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        string? configured = configuration["OpenApi:SpecPath"] ?? Environment.GetEnvironmentVariable("OPENAPI_SPEC_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string explicitPath = Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(baseDirectory, configured));

            if (!File.Exists(explicitPath))
            {
                throw new FileNotFoundException($"Configured OpenAPI spec was not found: '{explicitPath}'.");
            }

            return explicitPath;
        }

        IReadOnlyList<string> discoveredSpecs = DiscoverSpecPaths(baseDirectory);

        if (discoveredSpecs.Count == 1)
        {
            return discoveredSpecs[0];
        }

        string found = discoveredSpecs.Count == 0
            ? "none"
            : string.Join(", ", discoveredSpecs.OrderBy(static s => s, StringComparer.OrdinalIgnoreCase));

        throw new FileNotFoundException(
            "Could not resolve an OpenAPI contract file. " +
            "Set OpenApi:SpecPath (or OPENAPI_SPEC_PATH) or keep exactly one OpenAPI file in a contracts directory. " +
            $"Discovered: {found}.");
    }

    /// <summary>
    /// Resolves all configured OpenAPI sources from configuration, environment, output, or repository layout.
    /// </summary>
    public static IReadOnlyList<OpenApiSourceDefinition> ResolveSources(IConfiguration configuration, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        List<OpenApiSourceDefinition> configuredSources = ResolveConfiguredSources(configuration, baseDirectory);
        if (configuredSources.Count > 0)
        {
            return configuredSources;
        }

        string? configured = configuration["OpenApi:SpecPath"] ?? Environment.GetEnvironmentVariable("OPENAPI_SPEC_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string specPath = ResolveSpecPath(configuration, baseDirectory);
            return
            [
                new OpenApiSourceDefinition(
                    InferSourceName(specPath),
                    specPath,
                    configuration["OpenApi:BaseUrl"]),
            ];
        }

        IReadOnlyList<string> discoveredSpecs = DiscoverSpecPaths(baseDirectory);
        if (discoveredSpecs.Count > 0)
        {
            return discoveredSpecs
                .Select(static specPath => new OpenApiSourceDefinition(InferSourceName(specPath), specPath))
                .ToArray();
        }

        throw new FileNotFoundException(
            "Could not resolve any OpenAPI contract files. " +
            "Configure OpenApi:Sources or keep one or more OpenAPI files in a contracts directory.");
    }

    /// <summary>
    /// Parses an OpenAPI file and builds executable tool descriptors.
    /// </summary>
    /// <summary>
    /// Parses multiple OpenAPI files or URLs and builds executable tool descriptors.
    /// </summary>
    public static async Task<IReadOnlyList<ToolDescriptor>> BuildToolsAsync(
        IReadOnlyList<OpenApiSourceDefinition> sources,
        CancellationToken ct = default)
    {
        return await BuildToolsAsync(sources, DefaultDocumentLoader, DefaultRequestInvoker, ct);
    }

    /// <summary>
    /// Parses multiple OpenAPI files or URLs and builds executable tool descriptors.
    /// </summary>
    private static async Task<IReadOnlyList<ToolDescriptor>> BuildToolsAsync(
        IReadOnlyList<OpenApiSourceDefinition> sources,
        IOpenApiDocumentLoader documentLoader,
        IOpenApiRequestInvoker requestInvoker,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(documentLoader);
        ArgumentNullException.ThrowIfNull(requestInvoker);

        if (sources.Count == 0)
        {
            return [];
        }

        // Load sources with per-source error handling to gracefully fall back if one fails.
        List<OpenApiSourceDocument> loadedSources = [];
        foreach (var source in sources)
        {
            try
            {
                var loadedSource = await documentLoader.LoadAsync(source, ct);
                loadedSources.Add(loadedSource);
            }
            catch (Exception ex)
            {
                // If a remote or local source fails to load, log and skip it (don't crash the app).
                // This allows brewery to load even if petstore times out.
                System.Diagnostics.Debug.WriteLine(
                    $"[OpenAPI] Failed to load '{source.Name}' from '{source.Location}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (loadedSources.Count == 0)
        {
            throw new InvalidOperationException(
                $"No OpenAPI sources loaded. Configured sources: {string.Join(", ", sources.Select(s => $"'{s.Name}' ({s.Location})"))}");
        }

        List<ToolDescriptor> tools = [];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (OpenApiSourceDocument loadedSource in loadedSources)
        {
            Uri? overrideBaseUri = ResolveOverrideBaseUri(loadedSource.Source.BaseUrlOverride);

            foreach ((string routeTemplate, OpenApiPathItem pathItem) in loadedSource.Document.Paths)
            {
                foreach ((OperationType operationType, OpenApiOperation operation) in pathItem.Operations)
                {
                    EndpointDefinition endpoint = BuildEndpointDefinition(
                        loadedSource.Document,
                        routeTemplate,
                        pathItem,
                        operationType,
                        operation,
                        overrideBaseUri,
                        loadedSource.DocumentUri);

                    string primaryToolName = ReserveUniqueToolName(names, endpoint.ToolName, loadedSource.Source.Name);
                    tools.Add(BuildToolDescriptor(endpoint, primaryToolName, isAlias: false, requestInvoker));

                    foreach (string alias in endpoint.Aliases)
                    {
                        string uniqueAlias = ReserveUniqueToolName(names, alias, loadedSource.Source.Name);
                        tools.Add(BuildToolDescriptor(endpoint, uniqueAlias, isAlias: true, requestInvoker));
                    }
                }
            }
        }

        return tools;
    }

    /// <summary>
    /// Builds one endpoint definition from an OpenAPI operation.
    /// </summary>
    private static EndpointDefinition BuildEndpointDefinition(
        OpenApiDocument document,
        string routeTemplate,
        OpenApiPathItem pathItem,
        OperationType operationType,
        OpenApiOperation operation,
        Uri? overrideBaseUri,
        Uri? documentUri)
    {
        string operationId = operation.OperationId ?? $"{operationType}_{routeTemplate}";
        string toolName = ResolvePrimaryToolName(operationId, operationType, routeTemplate);
        string description = string.IsNullOrWhiteSpace(operation.Summary)
            ? (operation.Description ?? operationId)
            : operation.Summary;

        List<OpenApiParameter> mergedParameters = MergeParameters(pathItem.Parameters, operation.Parameters);
        List<EndpointParameter> parameters = mergedParameters
            .Select(static parameter => new EndpointParameter(
                parameter.Name,
                parameter.In ?? ParameterLocation.Query,
                parameter.Required,
                InferParameterType(parameter.Schema),
                parameter.Description ?? string.Empty))
            .ToList();

        RequestBodyDefinition requestBody = BuildRequestBody(operation.RequestBody);

        Uri? endpointBaseUri = ResolveEndpointBaseUri(operation.Servers, pathItem.Servers, document.Servers, overrideBaseUri, documentUri);

        IReadOnlyList<string> tags = operation.Tags.Count > 0
            ? operation.Tags.Select(tag => tag.Name).ToArray()
            : BuildFallbackTags(routeTemplate);

        IReadOnlyList<string> aliases = ResolveCompatibilityAliases(routeTemplate, operationType, toolName);

        return new EndpointDefinition(
            toolName,
            description,
            routeTemplate,
            operationType,
            parameters,
            tags,
            aliases,
            requestBody,
            endpointBaseUri);
    }

    /// <summary>
    /// Builds one executable tool descriptor from a normalized endpoint definition.
    /// </summary>
    private static ToolDescriptor BuildToolDescriptor(EndpointDefinition endpoint, string toolName, bool isAlias, IOpenApiRequestInvoker requestInvoker)
    {
        string schema = BuildInputSchema(endpoint.Parameters, endpoint.RequestBody);
        string description = isAlias
            ? $"{endpoint.Description} (compatibility alias)."
            : endpoint.Description;

        return new ToolDescriptor(
            Name: toolName,
            Description: description,
            InputJsonSchema: schema,
            Tags: endpoint.Tags,
            IsPinned: false,
            IsSynthetic: false,
            IsVisible: _ => true,
            Handler: async (arguments, ct) => await requestInvoker.InvokeAsync(endpoint, arguments, ct));
    }

    /// <summary>
    /// Builds JSON schema for tool input from OpenAPI parameter metadata.
    /// </summary>
    private static string BuildInputSchema(IReadOnlyList<EndpointParameter> parameters, RequestBodyDefinition requestBody)
    {
        JsonObject properties = [];
        JsonArray required = [];

        foreach (EndpointParameter parameter in parameters)
        {
            var property = new JsonObject
            {
                ["type"] = parameter.Type,
            };

            if (!string.IsNullOrWhiteSpace(parameter.Description))
            {
                property["description"] = parameter.Description;
            }

            properties[parameter.Name] = property;

            if (parameter.Required)
            {
                required.Add(parameter.Name);
            }
        }

        if (requestBody.SupportsJson)
        {
            properties["body"] = new JsonObject
            {
                ["type"] = "object",
                ["description"] = "Request body payload encoded as application/json.",
            };

            if (requestBody.Required)
            {
                required.Add("body");
            }
        }

        var root = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
        };

        if (required.Count > 0)
        {
            root["required"] = required;
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Converts a JSON argument to wire format string for path/query placement.
    /// </summary>
    /// Resolves a primary tool name using OpenAPI operationId or method/path fallback.
    /// </summary>
    private static string ResolvePrimaryToolName(string operationId, OperationType operationType, string routeTemplate)
    {
        if (!string.IsNullOrWhiteSpace(operationId))
        {
            return NormalizeIdentifier(operationId);
        }

        string normalizedPath = routeTemplate
            .Replace("/", "_", StringComparison.Ordinal)
            .Replace("{", string.Empty, StringComparison.Ordinal)
            .Replace("}", string.Empty, StringComparison.Ordinal)
            .Trim('_')
            .ToLowerInvariant();

        return $"{operationType.ToString().ToLowerInvariant()}_{normalizedPath}";
    }

    /// <summary>
    /// Generates optional compatibility aliases for common REST collection naming patterns.
    /// </summary>
    private static IReadOnlyList<string> ResolveCompatibilityAliases(string routeTemplate, OperationType operationType, string primaryName)
    {
        if (operationType != OperationType.Get)
        {
            return [];
        }

        string[] segments = routeTemplate.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] staticSegments = segments.Where(static segment => !segment.StartsWith('{') && !segment.EndsWith('}')).ToArray();

        if (staticSegments.Length == 0)
        {
            return [];
        }

        string resource = Singularize(staticSegments[0]);
        string lastStaticSegment = staticSegments[^1].ToLowerInvariant();
        bool hasPathParameter = segments.Any(static segment => segment.StartsWith('{') && segment.EndsWith('}'));

        string? alias = lastStaticSegment switch
        {
            "random" => $"{resource}_random",
            "search" => $"{resource}_search",
            "meta" or "metadata" => $"{resource}_meta",
            _ when hasPathParameter => $"{resource}_get",
            _ => $"{resource}_list",
        };

        alias = NormalizeIdentifier(alias);

        if (string.Equals(alias, primaryName, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return [alias];
    }

    /// <summary>
    /// Merges path-level and operation-level parameters by name+location.
    /// </summary>
    private static List<OpenApiParameter> MergeParameters(IList<OpenApiParameter> pathParameters, IList<OpenApiParameter> operationParameters)
    {
        var map = new Dictionary<string, OpenApiParameter>(StringComparer.OrdinalIgnoreCase);

        foreach (OpenApiParameter parameter in pathParameters)
        {
            string key = BuildParameterKey(parameter);
            map[key] = parameter;
        }

        foreach (OpenApiParameter parameter in operationParameters)
        {
            string key = BuildParameterKey(parameter);
            map[key] = parameter;
        }

        return map.Values.ToList();
    }

    /// <summary>
    /// Builds a stable dictionary key for parameter identity.
    /// </summary>
    private static string BuildParameterKey(OpenApiParameter parameter)
    {
        return $"{parameter.In}:{parameter.Name}";
    }

    /// <summary>
    /// Projects requestBody support details used by generated tool input schema and invocation.
    /// </summary>
    private static RequestBodyDefinition BuildRequestBody(OpenApiRequestBody? requestBody)
    {
        if (requestBody is null)
        {
            return new RequestBodyDefinition(SupportsJson: false, Required: false);
        }

        bool supportsJson = requestBody.Content.ContainsKey("application/json") || requestBody.Content.Keys.Any(static key => key.EndsWith("+json", StringComparison.OrdinalIgnoreCase));

        return new RequestBodyDefinition(SupportsJson: supportsJson, Required: requestBody.Required);
    }

    /// <summary>
    /// Resolves the effective server URL based on operation, path, document, and optional override.
    /// </summary>
    private static Uri? ResolveEndpointBaseUri(
        IList<OpenApiServer> operationServers,
        IList<OpenApiServer> pathServers,
        IList<OpenApiServer> rootServers,
        Uri? overrideBaseUri,
        Uri? documentUri)
    {
        if (overrideBaseUri is not null)
        {
            return overrideBaseUri;
        }

        OpenApiServer? selected = operationServers.FirstOrDefault() ?? pathServers.FirstOrDefault() ?? rootServers.FirstOrDefault();
        if (selected is null)
        {
            return null;
        }

        string expanded = selected.Url;
        foreach ((string variableName, OpenApiServerVariable variable) in selected.Variables)
        {
            expanded = expanded.Replace($"{{{variableName}}}", variable.Default, StringComparison.Ordinal);
        }

        if (Uri.TryCreate(expanded, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return EnsureTrailingSlash(uri);
        }

        if (documentUri is null || !Uri.TryCreate(documentUri, expanded, out Uri? resolvedUri))
        {
            return null;
        }

        return EnsureTrailingSlash(resolvedUri);
    }

    /// <summary>
    /// Resolves and validates an optional explicit base URL override.
    /// </summary>
    private static Uri? ResolveOverrideBaseUri(string? baseUrlOverride)
    {
        if (string.IsNullOrWhiteSpace(baseUrlOverride))
        {
            return null;
        }

        if (!Uri.TryCreate(baseUrlOverride, UriKind.Absolute, out Uri? parsed))
        {
            throw new InvalidOperationException($"OpenApi:BaseUrl must be an absolute URI. Value: '{baseUrlOverride}'.");
        }

        return EnsureTrailingSlash(parsed);
    }

    /// <summary>
    /// Ensures a URI has a trailing slash so relative path joins preserve base path segments.
    /// </summary>
    private static Uri EnsureTrailingSlash(Uri uri)
    {
        if (uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
        {
            return uri;
        }

        return new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
    }

    /// <summary>
    /// Infers a JSON Schema primitive type from an OpenAPI parameter schema.
    /// </summary>
    private static string InferParameterType(OpenApiSchema? schema)
    {
        if (schema is null)
        {
            return "string";
        }

        return schema.Type switch
        {
            "integer" => "integer",
            "number" => "number",
            "boolean" => "boolean",
            "array" => "array",
            "object" => "object",
            _ => "string",
        };
    }

    /// <summary>
    /// Builds fallback tags from a route template when operation tags are not present.
    /// </summary>
    private static IReadOnlyList<string> BuildFallbackTags(string routeTemplate)
    {
        string firstSegment = routeTemplate
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(static segment => !segment.StartsWith('{') && !segment.EndsWith('}'))
            ?? "openapi";

        return [NormalizeIdentifier(Singularize(firstSegment))];
    }

    /// <summary>
    /// Converts identifiers to a stable snake_case token for tool names.
    /// </summary>
    private static string NormalizeIdentifier(string value)
    {
        string withWordBoundaries = Regex.Replace(value, "([a-z0-9])([A-Z])", "$1_$2", RegexOptions.CultureInvariant);
        string normalized = Regex.Replace(withWordBoundaries, "[^a-zA-Z0-9]+", "_", RegexOptions.CultureInvariant)
            .Trim('_')
            .ToLowerInvariant();

        return string.IsNullOrWhiteSpace(normalized) ? "tool" : normalized;
    }

    /// <summary>
    /// Performs a minimal plural-to-singular normalization for resource-style names.
    /// </summary>
    private static string Singularize(string value)
    {
        string normalized = NormalizeIdentifier(value);

        if (normalized.EndsWith("ies", StringComparison.Ordinal) && normalized.Length > 3)
        {
            return normalized[..^3] + "y";
        }

        if (normalized.EndsWith('s') && normalized.Length > 1)
        {
            return normalized[..^1];
        }

        return normalized;
    }

    /// <summary>
    /// Maps OpenAPI operation type to HTTP method.
    /// </summary>

    /// <summary>
    /// Resolves configured OpenAPI sources from the OpenApi:Sources collection.
    /// </summary>
    private static List<OpenApiSourceDefinition> ResolveConfiguredSources(IConfiguration configuration, string baseDirectory)
    {
        List<OpenApiSourceDefinition> sources = [];

        foreach (IConfigurationSection section in configuration.GetSection("OpenApi:Sources").GetChildren())
        {
            string? configuredPath = section["Path"];
            string? configuredUrl = section["Url"];

            bool hasPath = !string.IsNullOrWhiteSpace(configuredPath);
            bool hasUrl = !string.IsNullOrWhiteSpace(configuredUrl);

            if (hasPath == hasUrl)
            {
                throw new InvalidOperationException(
                    "Each OpenApi:Sources entry must define exactly one of Path or Url.");
            }

            string location = hasPath
                ? ResolveConfiguredLocalPath(configuredPath!, baseDirectory)
                : configuredUrl!;

            string name = string.IsNullOrWhiteSpace(section["Name"])
                ? InferSourceName(location)
                : section["Name"]!;

            sources.Add(new OpenApiSourceDefinition(name, location, section["BaseUrl"]));
        }

        return sources;
    }

    /// <summary>
    /// Discovers candidate OpenAPI contract files relative to the application base directory.
    /// </summary>
    private static IReadOnlyList<string> DiscoverSpecPaths(string baseDirectory)
    {
        var discoveredSpecs = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string[] candidates =
        [
            Path.Combine(baseDirectory, "contracts", "openapi.yaml"),
            Path.Combine(baseDirectory, "contracts", "openapi.yml"),
            Path.Combine(baseDirectory, "contracts", "openapi.json"),
            Path.Combine(baseDirectory, "openapi.yaml"),
            Path.Combine(baseDirectory, "openapi.yml"),
            Path.Combine(baseDirectory, "openapi.json"),
        ];

        foreach (string candidate in candidates)
        {
            AddDiscoveredSpec(discoveredSpecs, seen, candidate);
        }

        string[] contractDirectories =
        [
            Path.Combine(baseDirectory, "contracts"),
            Path.GetFullPath(Path.Combine(baseDirectory, "../../../../contracts")),
        ];

        string[] patterns = ["*.openapi.yaml", "*.openapi.yml", "*.openapi.json", "openapi.yaml", "openapi.yml", "openapi.json"];

        foreach (string directory in contractDirectories.Where(Directory.Exists))
        {
            foreach (string pattern in patterns)
            {
                foreach (string file in Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly))
                {
                    AddDiscoveredSpec(discoveredSpecs, seen, file);
                }
            }
        }

        return discoveredSpecs;
    }

    /// <summary>
    /// Reserves a globally unique tool name across all loaded OpenAPI sources.
    /// </summary>
    private static string ReserveUniqueToolName(HashSet<string> names, string proposedName, string sourceName)
    {
        string normalizedName = NormalizeIdentifier(proposedName);
        if (names.Add(normalizedName))
        {
            return normalizedName;
        }

        string prefixedName = NormalizeIdentifier($"{sourceName}_{normalizedName}");
        if (names.Add(prefixedName))
        {
            return prefixedName;
        }

        int suffix = 2;
        while (true)
        {
            string candidate = $"{prefixedName}_{suffix}";
            if (names.Add(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    /// <summary>
    /// Adds one discovered spec path while preserving discovery order and uniqueness.
    /// </summary>
    private static void AddDiscoveredSpec(List<string> discoveredSpecs, HashSet<string> seen, string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) || !seen.Add(fullPath))
        {
            return;
        }

        discoveredSpecs.Add(fullPath);
    }

    /// <summary>
    /// Resolves one configured local spec path relative to the application base directory.
    /// </summary>
    private static string ResolveConfiguredLocalPath(string configuredPath, string baseDirectory)
    {
        string explicitPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(baseDirectory, configuredPath));

        return explicitPath;
    }

    /// <summary>
    /// Infers a stable source name from a local file path or remote URL.
    /// </summary>
    private static string InferSourceName(string location)
    {
        if (Uri.TryCreate(location, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            string lastSegment = uri.Segments.LastOrDefault()?.Trim('/') ?? string.Empty;
            string candidate = string.IsNullOrWhiteSpace(lastSegment)
                ? uri.Host
                : lastSegment;

            return NormalizeIdentifier(candidate);
        }

        return NormalizeIdentifier(Path.GetFileNameWithoutExtension(location));
    }

    /// <summary>
    /// Normalized endpoint definition derived from OpenAPI operation data.
    /// </summary>
    internal sealed record EndpointDefinition(
        string ToolName,
        string Description,
        string RouteTemplate,
        OperationType Method,
        IReadOnlyList<EndpointParameter> Parameters,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> Aliases,
        RequestBodyDefinition RequestBody,
        Uri? BaseUri);

    /// <summary>
    /// Canonical parameter projection for generated handlers.
    /// </summary>
    internal sealed record EndpointParameter(
        string Name,
        ParameterLocation Location,
        bool Required,
        string Type,
        string Description);

    /// <summary>
    /// Request body projection used for generated schema and invocation behavior.
    /// </summary>
    internal sealed record RequestBodyDefinition(
        bool SupportsJson,
        bool Required);
}
