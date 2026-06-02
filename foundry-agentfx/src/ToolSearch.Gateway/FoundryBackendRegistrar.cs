using System.Text.Json;
using Azure.Core;
using ToolSearch.Gateway.Registry;

namespace ToolSearch.Gateway;

public static class FoundryBackendRegistrar
{
    public static IReadOnlyList<ToolDescriptor> Build(IConfiguration config, IHttpClientFactory httpFactory, TokenCredential? credential = null)
    {
        var tools = new List<ToolDescriptor>();

        var iqEndpoint = config["FoundryIQ:SearchEndpoint"];
        var iqKbName = config["FoundryIQ:KnowledgeBaseName"];
        var iqApiKey = config["FoundryIQ:ApiKey"];
        if (!string.IsNullOrWhiteSpace(iqEndpoint) && !string.IsNullOrWhiteSpace(iqKbName))
        {
            tools.Add(BuildKnowledgeLookupTool(httpFactory, iqEndpoint, iqKbName, iqApiKey, credential));
        }

        var braveApiKey = config["BraveSearch:ApiKey"];
        if (!string.IsNullOrWhiteSpace(braveApiKey))
        {
            tools.Add(BuildBraveSearchTool(httpFactory, braveApiKey));
        }

        var toolboxEndpoint = config["Toolbox:McpEndpoint"];
        var foundryApiKey = config["Foundry:ApiKey"];
        if (!string.IsNullOrWhiteSpace(toolboxEndpoint))
        {
            bool hasWebSearch = !string.IsNullOrWhiteSpace(braveApiKey);
            tools.AddRange(BuildToolboxTools(httpFactory, toolboxEndpoint, foundryApiKey, credential, skipWebSearch: hasWebSearch));
        }

        return tools;
    }

    private static ToolDescriptor BuildKnowledgeLookupTool(
        IHttpClientFactory httpFactory, string endpoint, string knowledgeBaseName,
        string? apiKey, TokenCredential? credential)
    {
        return new ToolDescriptor(
            Name: "knowledge_lookup",
            Description: "Search the Foundry Coffee Co. knowledge base. Use for questions about company info, store hours, locations, menu, policies, sustainability, careers, catering, and promotions. Always include 'Foundry Coffee' in your query (e.g. 'about Foundry Coffee Co.' or 'Foundry Coffee store hours').",
            InputJsonSchema: """{"type":"object","properties":{"query":{"type":"string","description":"Natural language question including the brand name 'Foundry Coffee' for best results"}},"required":["query"]}""",
            Tags: ["foundry", "knowledge", "iq", "search", "coffeeshop"],
            IsPinned: false,
            IsSynthetic: false,
            IsVisible: _ => true,
            Handler: async (arguments, ct) =>
            {
                using HttpClient http = httpFactory.CreateClient("foundry-iq");

                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    http.DefaultRequestHeaders.Authorization = null;
                    http.DefaultRequestHeaders.Remove("api-key");
                    http.DefaultRequestHeaders.TryAddWithoutValidation("api-key", apiKey);
                }
                else if (credential is not null)
                {
                    var tokenCtx = new TokenRequestContext(["https://search.azure.com/.default"]);
                    var token = await credential.GetTokenAsync(tokenCtx, ct);
                    http.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
                }

                var query = arguments.ValueKind == JsonValueKind.Object &&
                    arguments.TryGetProperty("query", out var q)
                    ? q.GetString() ?? "" : "";

                // KB retrieve with minimal reasoning effort uses intents format (no model needed)
                var payload = new
                {
                    intents = new[] { new { type = "semantic", search = query } }
                };

                var url = $"{endpoint.TrimEnd('/')}/knowledgebases/{knowledgeBaseName}/retrieve?api-version=2025-11-01-preview";
                using var response = await http.PostAsJsonAsync(url, payload, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    return new { error = $"Knowledge base query failed: {response.StatusCode} — {body}" };
                }

                JsonDocument doc = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

                // Extract text content from response[0].content[0].text (extractiveData mode)
                if (doc.RootElement.TryGetProperty("response", out var resp) &&
                    resp.ValueKind == JsonValueKind.Array &&
                    resp.GetArrayLength() > 0 &&
                    resp[0].TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.Array &&
                    content.GetArrayLength() > 0 &&
                    content[0].TryGetProperty("text", out var text))
                {
                    return text.GetString();
                }

                return doc.RootElement;
            });
    }

    private static ToolDescriptor BuildBraveSearchTool(IHttpClientFactory httpFactory, string apiKey)
    {
        return new ToolDescriptor(
            Name: "web_search",
            Description: "Search the web for real-time information. Use for current news, trends, competitor info, or when the user pastes a URL and asks about its content (e.g. 'https://vnexpress.vn what is hot today?' → query: 'site:vnexpress.vn hot news today').",
            InputJsonSchema: """{"type":"object","properties":{"query":{"type":"string","description":"Search query. For URL+question use 'site:<domain> <question>', e.g. 'site:vnexpress.vn hot news'"}},"required":["query"]}""",
            Tags: ["web", "search", "brave", "news", "realtime"],
            IsPinned: false,
            IsSynthetic: false,
            IsVisible: _ => true,
            Handler: async (arguments, ct) =>
            {
                var query = arguments.ValueKind == JsonValueKind.Object &&
                    arguments.TryGetProperty("query", out var q)
                    ? q.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(query))
                    return new { error = "query is required" };

                using HttpClient http = httpFactory.CreateClient("brave-search");
                http.DefaultRequestHeaders.TryAddWithoutValidation("X-Subscription-Token", apiKey);
                http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

                var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count=10";
                using var response = await http.GetAsync(url, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync(ct);
                    return new { error = $"Brave Search failed: {response.StatusCode} — {err}" };
                }

                var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

                if (!doc.RootElement.TryGetProperty("web", out var web) ||
                    !web.TryGetProperty("results", out var values))
                    return new { results = Array.Empty<object>() };

                var results = values.EnumerateArray().Select(item => new
                {
                    title       = item.TryGetProperty("title",       out var t) ? t.GetString() : null,
                    url         = item.TryGetProperty("url",         out var u) ? u.GetString() : null,
                    description = item.TryGetProperty("description", out var d) ? d.GetString() : null,
                }).ToArray();

                return new { query, results };
            });
    }

    private static IEnumerable<ToolDescriptor> BuildToolboxTools(
        IHttpClientFactory httpFactory, string toolboxEndpoint, string? apiKey, TokenCredential? credential, bool skipWebSearch = false)
    {
        if (!skipWebSearch)
        {
            yield return new ToolDescriptor(
                Name: "web_search",
                Description: "Search the web for current information about coffee trends, competitor prices, or any real-time information.",
                InputJsonSchema: """{"type":"object","properties":{"search_query":{"type":"string","description":"The search query"}},"required":["search_query"]}""",
                Tags: ["toolbox", "web", "search", "foundry"],
                IsPinned: false,
                IsSynthetic: false,
                IsVisible: _ => true,
                Handler: (arguments, ct) => CallToolboxAsync(httpFactory, toolboxEndpoint, apiKey, credential, "web_search", arguments, ct));
        }

        yield return new ToolDescriptor(
            Name: "code_interpreter",
            Description: "Execute Python code for calculations, data analysis, discounts, or analytics. Returns output from code execution.",
            InputJsonSchema: """{"type":"object","properties":{"code":{"type":"string","description":"Python code to execute"}},"required":["code"]}""",
            Tags: ["toolbox", "code", "compute", "foundry"],
            IsPinned: false,
            IsSynthetic: false,
            IsVisible: _ => true,
            Handler: (arguments, ct) => CallToolboxAsync(httpFactory, toolboxEndpoint, apiKey, credential, "code_interpreter", arguments, ct));
    }

    private static async Task<object?> CallToolboxAsync(
        IHttpClientFactory httpFactory, string toolboxEndpoint, string? apiKey,
        TokenCredential? credential, string toolName, JsonElement arguments, CancellationToken ct)
    {
        using HttpClient http = httpFactory.CreateClient("foundry-toolbox");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            http.DefaultRequestHeaders.Authorization = null;
            http.DefaultRequestHeaders.Remove("api-key");
            http.DefaultRequestHeaders.TryAddWithoutValidation("api-key", apiKey);
        }
        else if (credential is not null)
        {
            var tokenCtx = new TokenRequestContext(["https://search.azure.com/.default"]);
            var token = await credential.GetTokenAsync(tokenCtx, ct);
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
        }
        var payload = new
        {
            jsonrpc = "2.0",
            method = "tools/call",
            @params = new { name = toolName, arguments },
            id = Guid.NewGuid().ToString("N")
        };

        using var response = await http.PostAsJsonAsync(toolboxEndpoint, payload, ct);

        if (!response.IsSuccessStatusCode)
            return new { error = $"Toolbox call '{toolName}' failed: {response.StatusCode}" };

        JsonDocument doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        return doc.RootElement.TryGetProperty("result", out var result) ? result : doc.RootElement;
    }
}
