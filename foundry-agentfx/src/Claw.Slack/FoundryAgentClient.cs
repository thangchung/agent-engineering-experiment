namespace Claw.Slack;

using Azure.Core;
using Azure.Identity;

public sealed class FoundryAgentClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly TokenCredential? _credential;
    private readonly string _tokenResource;
    private readonly string _invocationsPath;

    public FoundryAgentClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _invocationsPath = config["Agent:InvocationsPath"] ?? "/invocations";
        _tokenResource = config["Agent:TokenResource"] ?? "";

        if (!string.IsNullOrEmpty(_tokenResource))
            _credential = new DefaultAzureCredential();
    }

    public async Task<string> InvokeAsync(string input, string sessionId, CancellationToken ct = default)
    {
        var url = $"{_invocationsPath}?agent_session_id={Uri.EscapeDataString(sessionId)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new { input }),
            System.Text.Encoding.UTF8,
            "application/json");

        if (_credential is not null)
        {
            var tokenRequest = new TokenRequestContext([_tokenResource]);
            var token = await _credential.GetTokenAsync(tokenRequest, ct);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
            request.Headers.TryAddWithoutValidation("Foundry-Features", "HostedAgents=V1Preview");
        }

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("output", out var output))
            return output.GetString() ?? "";
        return json;
    }

    public void Dispose() => _http.Dispose();
}
