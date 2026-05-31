namespace Claw.Slack;

using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

public sealed class FoundryAgentClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly TokenCredential? _credential;
    private readonly string _tokenResource;
    private readonly string _invocationsPath;
    private readonly string _sessionsPath;
    private readonly ConcurrentDictionary<string, string> _foundrySessions = new();

    public FoundryAgentClient(HttpClient http, IConfiguration config)
        : this(http, config, new DefaultAzureCredential())
    {
    }

    internal FoundryAgentClient(HttpClient http, IConfiguration config, TokenCredential credential)
    {
        _http = http;
        _invocationsPath = (config["Agent:InvocationsPath"] ?? "invocations").TrimStart('/');
        _sessionsPath = BuildSessionsPath(_invocationsPath);
        _tokenResource = config["Agent:TokenResource"] ?? "";

        if (!string.IsNullOrEmpty(_tokenResource))
            _credential = credential;
    }

    public async Task<string> InvokeAsync(string input, string sessionId, CancellationToken ct = default)
    {
        var agentSessionId = _credential is null
            ? sessionId
            : await GetOrCreateFoundrySessionAsync(sessionId, ct);

        var separator = _invocationsPath.Contains('?') ? '&' : '?';
        var url = $"{_invocationsPath}{separator}agent_session_id={Uri.EscapeDataString(agentSessionId)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new { input, session_id = sessionId }),
            System.Text.Encoding.UTF8,
            "application/json");

        await AuthorizeAsync(request, ct);

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body))
            return "";

        var trimmed = body.AsSpan().TrimStart();
        if (trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '['))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("output", out var output))
                        return output.GetString() ?? "";

                    if (doc.RootElement.TryGetProperty("response", out var responseText))
                        return responseText.GetString() ?? "";
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Some endpoints may return non-JSON text despite a JSON content type.
            }
        }

        return body;
    }

    private async Task<string> GetOrCreateFoundrySessionAsync(string logicalSessionId, CancellationToken ct)
    {
        if (_foundrySessions.TryGetValue(logicalSessionId, out var existingSessionId))
            return existingSessionId;

        using var request = new HttpRequestMessage(HttpMethod.Post, _sessionsPath);
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        await AuthorizeAsync(request, ct);

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var agentSessionId = doc.RootElement.GetProperty("agent_session_id").GetString()
            ?? throw new InvalidOperationException("Foundry session response did not include agent_session_id.");

        _foundrySessions.TryAdd(logicalSessionId, agentSessionId);
        return agentSessionId;
    }

    private async Task AuthorizeAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (_credential is null)
            return;

        var tokenRequest = new TokenRequestContext([_tokenResource]);
        var token = await _credential.GetTokenAsync(tokenRequest, ct);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.TryAddWithoutValidation("Foundry-Features", "HostedAgents=V1Preview");
    }

    private static string BuildSessionsPath(string invocationsPath)
    {
        var queryStart = invocationsPath.IndexOf('?');
        var path = queryStart >= 0 ? invocationsPath[..queryStart] : invocationsPath;
        var query = queryStart >= 0 ? invocationsPath[queryStart..] : "";
        const string invocationsSuffix = "protocols/invocations";

        if (path.EndsWith(invocationsSuffix, StringComparison.OrdinalIgnoreCase))
            return $"{path[..^invocationsSuffix.Length]}sessions{query}";

        return $"sessions{query}";
    }

    public void Dispose() => _http.Dispose();
}
