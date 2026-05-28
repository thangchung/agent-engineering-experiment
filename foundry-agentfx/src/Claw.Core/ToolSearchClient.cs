namespace Claw.Core;

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

public sealed class ToolSearchClient(HttpClient http, ILogger<ToolSearchClient> logger) : IToolSearchClient
{
    public async Task<IReadOnlyList<ToolDefinition>> SearchToolsAsync(string query, int limit, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            "/api/search-tools", new { query, limit }, ct);
        response.EnsureSuccessStatusCode();

        IReadOnlyList<ToolDefinition>? result =
            await response.Content.ReadFromJsonAsync<IReadOnlyList<ToolDefinition>>(ct);

        logger.LogDebug("search_tools returned {Count} results for '{Query}'", result?.Count ?? 0, query);
        return result ?? [];
    }

    public async Task<object?> CallToolAsync(string name, JsonElement arguments, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            "/api/call-tool", new { name, arguments }, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }
}
