using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.OpenApi.Models;

namespace McpServer.OpenApi;

/// <summary>
/// Default executor for normalized OpenAPI endpoint requests.
/// </summary>
internal sealed class OpenApiRequestInvoker : IOpenApiRequestInvoker
{
    /// <inheritdoc/>
    public async Task<object?> InvokeAsync(OpenApiToolCatalogBuilder.EndpointDefinition endpoint, JsonElement arguments, CancellationToken ct)
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

        foreach (OpenApiToolCatalogBuilder.EndpointParameter parameter in endpoint.Parameters)
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

        Uri requestUri = new(endpoint.BaseUri, requestPath.TrimStart('/'));
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
            request.Content = new StringContent(requestBodyJson, Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await client.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            return new { status = (int)response.StatusCode, error = response.ReasonPhrase, body };
        }

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
}