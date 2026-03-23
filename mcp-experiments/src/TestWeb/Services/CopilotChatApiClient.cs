using System.Net.Http.Json;
using System.Text.Json;

namespace TestWeb.Services;

/// <summary>
/// Thin HTTP client for chat endpoints hosted by the McpServer minimal API.
/// </summary>
public sealed class CopilotChatApiClient(HttpClient httpClient, IConfiguration configuration)
{
    private readonly Uri baseUri = ResolveServerBaseUri(configuration);

    public async Task<ChatTurnMetrics> SendPromptAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Prompt cannot be empty.", nameof(prompt));
        }

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            new Uri(baseUri, "/chat/send"),
            new ChatPromptRequest(prompt),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string errorMessage = await ReadErrorMessageAsync(response, cancellationToken);
            throw new InvalidOperationException(errorMessage);
        }

        ChatTurnMetrics? metrics = await response.Content.ReadFromJsonAsync<ChatTurnMetrics>(cancellationToken);
        if (metrics is null)
        {
            throw new InvalidOperationException("Chat API returned an empty response body.");
        }

        return metrics;
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsync(new Uri(baseUri, "/chat/reset"), content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static Uri ResolveServerBaseUri(IConfiguration configuration)
    {
        string endpoint = configuration["Mcp:Endpoint"] ?? "http://localhost:5100/mcp";

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? endpointUri))
        {
            throw new InvalidOperationException($"Invalid Mcp:Endpoint value: {endpoint}");
        }

        Uri authority = new(endpointUri.GetLeftPart(UriPartial.Authority));
        return authority;
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"Chat API request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}).";
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            string? title = TryGetString(root, "title") ?? TryGetString(root, "error");
            string? detail = TryGetString(root, "detail");

            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(detail))
            {
                return $"{title}: {detail}";
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }
        }
        catch (JsonException)
        {
            // Preserve raw body fallback when the server did not return JSON.
        }

        return body;
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        if (root.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        if (!root.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind is not JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    public sealed record ChatTurnMetrics(
        string Content,
        int PromptTokens,
        int CompletionTokens,
        int TotalTokens,
        long ElapsedMilliseconds);

    private sealed record ChatPromptRequest(string Prompt);
}
