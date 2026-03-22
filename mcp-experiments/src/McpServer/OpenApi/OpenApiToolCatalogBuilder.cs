using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Web;
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

        var candidates = new List<string>
        {
            Path.Combine(baseDirectory, "contracts", "openapi.yaml"),
            Path.Combine(baseDirectory, "contracts", "openapi.yml"),
            Path.Combine(baseDirectory, "contracts", "openapi.json"),
            Path.Combine(baseDirectory, "openapi.yaml"),
            Path.Combine(baseDirectory, "openapi.yml"),
            Path.Combine(baseDirectory, "openapi.json"),
        };

        foreach (string path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        string[] contractDirectories =
        [
            Path.Combine(baseDirectory, "contracts"),
            Path.GetFullPath(Path.Combine(baseDirectory, "../../../../contracts")),
        ];

        var discoveredSpecs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] patterns = ["*.openapi.yaml", "*.openapi.yml", "*.openapi.json", "openapi.yaml", "openapi.yml", "openapi.json"];

        foreach (string directory in contractDirectories.Where(Directory.Exists))
        {
            foreach (string pattern in patterns)
            {
                foreach (string file in Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly))
                {
                    discoveredSpecs.Add(Path.GetFullPath(file));
                }
            }
        }

        if (discoveredSpecs.Count == 1)
        {
            return discoveredSpecs.Single();
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
    /// Parses an OpenAPI file and builds executable tool descriptors.
    /// </summary>
    public static IReadOnlyList<ToolDescriptor> BuildTools(string specPath, string? baseUrlOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(specPath);

        using FileStream stream = File.OpenRead(specPath);
        OpenApiDocument document = new OpenApiStreamReader().Read(stream, out OpenApiDiagnostic diagnostic);

        if (diagnostic.Errors.Count > 0)
        {
            string errors = string.Join("; ", diagnostic.Errors.Select(error => error.Message));
            throw new InvalidOperationException($"OpenAPI parse failed: {errors}");
        }

        List<ToolDescriptor> tools = [];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Uri? overrideBaseUri = ResolveOverrideBaseUri(baseUrlOverride);

        foreach ((string routeTemplate, OpenApiPathItem pathItem) in document.Paths)
        {
            foreach ((OperationType operationType, OpenApiOperation operation) in pathItem.Operations)
            {
                EndpointDefinition endpoint = BuildEndpointDefinition(document, routeTemplate, pathItem, operationType, operation, overrideBaseUri);

                ToolDescriptor descriptor = BuildToolDescriptor(endpoint, endpoint.ToolName, isAlias: false);
                if (names.Add(descriptor.Name))
                {
                    tools.Add(descriptor);
                }

                foreach (string alias in endpoint.Aliases)
                {
                    if (!names.Add(alias))
                    {
                        continue;
                    }

                    tools.Add(BuildToolDescriptor(endpoint, alias, isAlias: true));
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
        Uri? overrideBaseUri)
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

        Uri? endpointBaseUri = ResolveEndpointBaseUri(operation.Servers, pathItem.Servers, document.Servers, overrideBaseUri);

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
    private static ToolDescriptor BuildToolDescriptor(EndpointDefinition endpoint, string toolName, bool isAlias)
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
            Handler: async (arguments, ct) => await InvokeEndpointAsync(endpoint, arguments, ct));
    }

    /// <summary>
    /// Executes one OpenAPI-defined endpoint by mapping JSON arguments to path and query values.
    /// </summary>
    private static async Task<object?> InvokeEndpointAsync(EndpointDefinition endpoint, JsonElement arguments, CancellationToken ct)
    {
        HttpClient client = ToolServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();

        if (endpoint.BaseUri is null)
        {
            throw new InvalidOperationException(
                $"Operation '{endpoint.ToolName}' requires a server URL. " +
                "Define servers.url in the OpenAPI document or set OpenApi:BaseUrl.");
        }

        string path = endpoint.RouteTemplate;
        var query = HttpUtility.ParseQueryString(string.Empty);
        var cookieValues = new List<string>();
        var headerValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (EndpointParameter parameter in endpoint.Parameters)
        {
            bool hasValue = arguments.TryGetProperty(parameter.Name, out JsonElement argumentValue);
            if (!hasValue)
            {
                if (parameter.Required)
                {
                    throw new ArgumentException($"Missing required parameter '{parameter.Name}' for tool '{endpoint.ToolName}'.");
                }

                continue;
            }

            string stringValue = ToArgumentString(argumentValue);
            if (parameter.Location == ParameterLocation.Path)
            {
                path = path.Replace($"{{{parameter.Name}}}", Uri.EscapeDataString(stringValue), StringComparison.Ordinal);
                continue;
            }

            if (parameter.Location == ParameterLocation.Query)
            {
                query[parameter.Name] = stringValue;
                continue;
            }

            if (parameter.Location == ParameterLocation.Header)
            {
                headerValues[parameter.Name] = stringValue;
                continue;
            }

            if (parameter.Location == ParameterLocation.Cookie)
            {
                cookieValues.Add($"{parameter.Name}={Uri.EscapeDataString(stringValue)}");
            }
        }

        if (endpoint.RequestBody.Required && !arguments.TryGetProperty("body", out _))
        {
            throw new ArgumentException($"Missing required request body 'body' for tool '{endpoint.ToolName}'.");
        }

        string requestPath = query.Count > 0 ? $"{path}?{query}" : path;

        var requestUri = new Uri(endpoint.BaseUri, requestPath.TrimStart('/'));
        using HttpRequestMessage request = new(ToHttpMethod(endpoint.Method), requestUri);

        foreach ((string name, string value) in headerValues)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        if (cookieValues.Count > 0)
        {
            request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookieValues));
        }

        if (endpoint.RequestBody.SupportsJson && arguments.TryGetProperty("body", out JsonElement bodyElement))
        {
            string requestBodyJson = bodyElement.GetRawText();
            request.Content = new StringContent(requestBodyJson, System.Text.Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        string body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return body;
        }
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
    private static string ToArgumentString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => value.GetRawText(),
        };
    }

    /// <summary>
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
        Uri? overrideBaseUri)
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

        if (!Uri.TryCreate(expanded, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        return EnsureTrailingSlash(uri);
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
    private static HttpMethod ToHttpMethod(OperationType operationType)
    {
        return operationType switch
        {
            OperationType.Get => HttpMethod.Get,
            OperationType.Post => HttpMethod.Post,
            OperationType.Put => HttpMethod.Put,
            OperationType.Delete => HttpMethod.Delete,
            OperationType.Patch => HttpMethod.Patch,
            OperationType.Head => HttpMethod.Head,
            OperationType.Options => HttpMethod.Options,
            OperationType.Trace => HttpMethod.Trace,
            _ => throw new NotSupportedException($"Unsupported OpenAPI method: {operationType}"),
        };
    }

    /// <summary>
    /// Normalized endpoint definition derived from OpenAPI operation data.
    /// </summary>
    private sealed record EndpointDefinition(
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
    private sealed record EndpointParameter(
        string Name,
        ParameterLocation Location,
        bool Required,
        string Type,
        string Description);

    /// <summary>
    /// Request body projection used for generated schema and invocation behavior.
    /// </summary>
    private sealed record RequestBodyDefinition(
        bool SupportsJson,
        bool Required);
}
