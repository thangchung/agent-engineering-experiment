using System.Net.Http.Json;
using System.Text.Json;

namespace ToolSearch.Gateway;

/// <summary>
/// Manages a single MCP Streamable HTTP session with the coffeeshop-mcp server.
/// Handles the initialize handshake and caches the Mcp-Session-Id for subsequent calls.
/// </summary>
public sealed class CoffeeshopMcpSession
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<CoffeeshopMcpSession> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private string? _sessionId;

    public CoffeeshopMcpSession(IHttpClientFactory httpFactory, ILogger<CoffeeshopMcpSession> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<JsonElement> CallToolAsync(string toolName, object arguments, CancellationToken ct)
    {
        string sessionId = await EnsureSessionAsync(ct);
        using HttpClient http = _httpFactory.CreateClient("coffeeshop-mcp");
        return await CallToolWithSessionAsync(http, sessionId, toolName, arguments, ct);
    }

    private async Task<string> EnsureSessionAsync(CancellationToken ct)
    {
        if (_sessionId is not null) return _sessionId;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_sessionId is not null) return _sessionId;
            _sessionId = await InitializeSessionAsync(ct);
            return _sessionId;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<string> InitializeSessionAsync(CancellationToken ct)
    {
        using HttpClient http = _httpFactory.CreateClient("coffeeshop-mcp");

        var initRequest = new
        {
            jsonrpc = "2.0",
            id = "init-1",
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "toolsearch-gateway", version = "1.0" }
            }
        };

        using HttpResponseMessage initResponse = await http.PostAsJsonAsync("/mcp/", initRequest, ct);
        initResponse.EnsureSuccessStatusCode();

        string sessionId = initResponse.Headers.TryGetValues("Mcp-Session-Id", out var vals)
            ? vals.First()
            : throw new InvalidOperationException("MCP server did not return Mcp-Session-Id after initialize");

        _logger.LogInformation("MCP session initialized: {SessionId}", sessionId);

        // Send initialized notification (no id = notification)
        using var initializedReq = new HttpRequestMessage(HttpMethod.Post, "/mcp/")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized",
                @params = new { }
            })
        };
        initializedReq.Headers.Add("Mcp-Session-Id", sessionId);
        using HttpResponseMessage notifResponse = await http.SendAsync(initializedReq, ct);
        // 202 Accepted or 200 OK are both fine for notifications

        return sessionId;
    }

    private async Task<JsonElement> CallToolWithSessionAsync(
        HttpClient http, string sessionId, string toolName, object arguments, CancellationToken ct)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            method = "tools/call",
            @params = new { name = toolName, arguments },
            id = Guid.NewGuid().ToString("N")
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp/")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("Mcp-Session-Id", sessionId);

        using HttpResponseMessage response = await http.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Gone)
        {
            // Session expired — reset and retry once
            _logger.LogWarning("MCP session expired (410), reinitializing...");
            _sessionId = null;
            string newSessionId = await EnsureSessionAsync(ct);
            return await CallToolWithSessionAsync(http, newSessionId, toolName, arguments, ct);
        }

        response.EnsureSuccessStatusCode();

        // MCP Streamable HTTP may return either application/json or text/event-stream
        string? contentType = response.Content.Headers.ContentType?.MediaType;
        string body = await response.Content.ReadAsStringAsync(ct);

        string json = contentType == "text/event-stream"
            ? ExtractSseData(body)
            : body;

        JsonDocument doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("result", out JsonElement result))
            return result;
        return doc.RootElement;
    }

    private static string ExtractSseData(string sseBody)
    {
        // SSE format: one or more "field: value\n" lines, blank-line-separated events.
        // We only need the first "data:" line.
        foreach (string line in sseBody.Split('\n'))
        {
            ReadOnlySpan<char> trimmed = line.AsSpan().TrimEnd();
            if (trimmed.StartsWith("data:", StringComparison.Ordinal))
                return trimmed[5..].TrimStart().ToString();
        }
        throw new InvalidOperationException($"No 'data:' line found in SSE response: {sseBody[..Math.Min(200, sseBody.Length)]}");
    }
}
