using McpServer.Registry;
using McpServer.Search;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace McpServer.CodeMode;

/// <summary>
/// Staged discovery API used before Execute.
/// </summary>
public sealed class DiscoveryTools(IToolRegistry registry, IToolSearcher searcher)
{
    private static readonly ActivitySource ActivitySource = new("McpServer.CodeMode.DiscoveryTools");
    private const int DefaultLimit = 10;

    /// <summary>
    /// Searches tool catalog and returns discovery results with optional filtering.
    /// </summary>
    public DiscoverySearchResponse Search(
        string query,
        UserContext context,
        SchemaDetailLevel detail = SchemaDetailLevel.Brief,
        IReadOnlyList<string>? tags = null,
        int? limit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(context);

        using Activity? activity = ActivitySource.StartActivity("codemode.Search", ActivityKind.Internal);
        activity?.SetTag("mcp.query", query);
        activity?.SetTag("mcp.detail", detail.ToString());
        activity?.SetTag("mcp.tags.count", tags?.Count ?? 0);

        int resolvedLimit = limit ?? DefaultLimit;
        activity?.SetTag("mcp.limit", resolvedLimit);
        if (resolvedLimit <= 0)
        {
            activity?.SetTag("mcp.results.count", 0);
            return new DiscoverySearchResponse([], 0, null);
        }

        int max = Math.Max(1, registry.GetVisibleTools(context).Count);
        IEnumerable<ToolDescriptor> ranked = searcher.Search(query, max, context);
        ranked = FilterByTags(ranked, tags);

        List<ToolDescriptor> matched = ranked.ToList();
        List<ToolDescriptor> selected = matched.Take(resolvedLimit).ToList();

        string? annotation = null;
        if (detail != SchemaDetailLevel.Full && selected.Count < matched.Count)
        {
            annotation = $"{selected.Count} of {matched.Count} tools";
        }

        IReadOnlyList<DiscoveryResult> results = selected
            .Select(tool => new DiscoveryResult(
                tool.Name,
                tool.Description,
                tool.Tags,
                BuildSchemaProjection(tool, detail)))
            .ToArray();

        activity?.SetTag("mcp.results.count", results.Count);
        activity?.SetTag("mcp.results.totalMatched", matched.Count);

        return new DiscoverySearchResponse(results, matched.Count, annotation);
    }

    /// <summary>
    /// Returns schemas for a requested set of tool names and reports missing ones.
    /// </summary>
    public SchemaLookupResponse GetSchema(
        IReadOnlyList<string> toolNames,
        UserContext context,
        SchemaDetailLevel detail = SchemaDetailLevel.Detailed)
    {
        ArgumentNullException.ThrowIfNull(toolNames);
        ArgumentNullException.ThrowIfNull(context);

        using Activity? activity = ActivitySource.StartActivity("codemode.GetSchema", ActivityKind.Internal);
        activity?.SetTag("mcp.detail", detail.ToString());
        activity?.SetTag("mcp.toolNames.count", toolNames.Count);

        if (toolNames.Count == 0)
        {
            activity?.SetTag("mcp.results.count", 0);
            activity?.SetTag("mcp.missing.count", 0);
            return new SchemaLookupResponse([], []);
        }

        List<SchemaResult> results = [];
        List<string> missing = [];

        foreach (string toolName in toolNames)
        {
            ToolDescriptor? tool = registry.FindByName(toolName, context);

            if (tool is null)
            {
                missing.Add(toolName);
                continue;
            }

            string schema = detail switch
            {
                SchemaDetailLevel.Brief => $"{{\"name\":\"{tool.Name}\"}}",
                SchemaDetailLevel.Detailed => BuildCompactParameterMarkdown(tool.InputJsonSchema),
                SchemaDetailLevel.Full => tool.InputJsonSchema,
                _ => tool.InputJsonSchema,
            };

            results.Add(new SchemaResult(tool.Name, schema));
        }

        activity?.SetTag("mcp.results.count", results.Count);
        activity?.SetTag("mcp.missing.count", missing.Count);
        return new SchemaLookupResponse(results, missing);
    }

    /// <summary>
    /// Filters ranked tools by optional tag list.
    /// </summary>
    private static IEnumerable<ToolDescriptor> FilterByTags(IEnumerable<ToolDescriptor> ranked, IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return ranked;
        }

        HashSet<string> requested = tags
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (requested.Count == 0)
        {
            return ranked;
        }

        return ranked.Where(tool => tool.Tags.Any(requested.Contains));
    }

    /// <summary>
    /// Builds schema projection for Search results according to detail level.
    /// </summary>
    private static string? BuildSchemaProjection(ToolDescriptor tool, SchemaDetailLevel detail)
    {
        return detail switch
        {
            SchemaDetailLevel.Brief => null,
            SchemaDetailLevel.Detailed => BuildCompactParameterMarkdown(tool.InputJsonSchema),
            SchemaDetailLevel.Full => tool.InputJsonSchema,
            _ => null,
        };
    }

    /// <summary>
    /// Converts JSON schema properties into compact markdown parameter lines.
    /// </summary>
    private static string BuildCompactParameterMarkdown(string schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return "No parameters.";
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(schemaJson);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("properties", out JsonElement properties) ||
                properties.ValueKind != JsonValueKind.Object)
            {
                return "No parameters.";
            }

            HashSet<string> required = [];
            if (root.TryGetProperty("required", out JsonElement requiredElement) &&
                requiredElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in requiredElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is string name)
                    {
                        required.Add(name);
                    }
                }
            }

            var sb = new StringBuilder();
            foreach (JsonProperty property in properties.EnumerateObject())
            {
                JsonElement prop = property.Value;
                string type = prop.TryGetProperty("type", out JsonElement typeElement) && typeElement.ValueKind == JsonValueKind.String
                    ? typeElement.GetString() ?? "unknown"
                    : "unknown";

                string optionality = required.Contains(property.Name) ? "required" : "optional";
                string description = prop.TryGetProperty("description", out JsonElement descElement) && descElement.ValueKind == JsonValueKind.String
                    ? descElement.GetString() ?? string.Empty
                    : string.Empty;

                sb.Append("- ").Append(property.Name).Append(" (type: ").Append(type).Append(", ").Append(optionality).Append(')');
                if (!string.IsNullOrWhiteSpace(description))
                {
                    sb.Append(": ").Append(description.Trim());
                }

                sb.AppendLine();
            }

            string result = sb.ToString().TrimEnd();
            return string.IsNullOrWhiteSpace(result) ? "No parameters." : result;
        }
        catch (JsonException)
        {
            return "Schema format unavailable for markdown rendering.";
        }
    }
}
