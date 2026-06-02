namespace Claw.Channels;

public sealed class LocalAgentClient(HttpClient http, IConfiguration config) : IAgentClient
{
    private readonly string _invocationsPath = (config["Agent:InvocationsPath"] ?? "/invocations").TrimStart('/');

    public async Task<string> InvokeAsync(string input, string sessionId, CancellationToken ct = default)
    {
        var separator = _invocationsPath.Contains('?') ? '&' : '?';
        var url = $"{_invocationsPath}{separator}agent_session_id={Uri.EscapeDataString(sessionId)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new { input, session_id = sessionId }),
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await http.SendAsync(request, ct);
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
            catch (System.Text.Json.JsonException) { }
        }

        return body;
    }
}
