using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using McpServer.Registry;
using McpServer.Tools;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol.Server;

namespace McpServer.Bootstrap;

/// <summary>
/// Writes startup diagnostics about loaded OpenAPI sources, registered tools, and prompt-surface footprint.
/// </summary>
internal static class BootstrapConsoleReporter
{
    /// <summary>
    /// Writes the configured source names, grouped tool inventory, and prompt-surface token comparison.
    /// </summary>
    public static void WriteReport(
        IReadOnlyList<OpenApi.OpenApiToolCatalogBuilder.OpenApiSourceDefinition> openApiSources,
        IReadOnlyList<ToolDescriptor> tools)
    {
        ArgumentNullException.ThrowIfNull(openApiSources);
        ArgumentNullException.ThrowIfNull(tools);

        if (openApiSources.Count > 0)
        {
            Console.WriteLine($"[Bootstrap] Loading {openApiSources.Count} OpenAPI source(s): {string.Join(", ", openApiSources.Select(s => $"'{s.Name}'"))}");
        }

        Console.WriteLine($"[Bootstrap] Total tools registered: {tools.Count} (status + OpenAPI-generated)");

        List<IGrouping<string, ToolDescriptor>> toolsByTag = tools
            .Where(static tool => !tool.IsSynthetic && tool.Tags.Count > 0)
            .GroupBy(static tool => tool.Tags.FirstOrDefault() ?? "untagged")
            .OrderBy(static group => group.Key)
            .ToList();

        foreach (IGrouping<string, ToolDescriptor> group in toolsByTag)
        {
            Console.WriteLine($"[Bootstrap]   - {group.Key}: {group.Count()} tool(s)");

            foreach (ToolDescriptor tool in group.OrderBy(static tool => tool.Name, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[Bootstrap]     * {tool.Name}: {ToShortDescription(tool.Description)}");
            }
        }

        WritePromptSurfaceComparison(tools);
    }

    private static void WritePromptSurfaceComparison(IReadOnlyList<ToolDescriptor> tools)
    {
        IReadOnlyList<MethodInfo> exposedHandlers = GetExposedMcpHandlers();

        string traditionalPayload = BuildPromptSurfacePayload(
            tools.Select(static tool => (tool.Name, tool.Description, tool.InputJsonSchema)));

        string currentArchitecturePayload = BuildPromptSurfacePayload(
            exposedHandlers.Select(method =>
                (
                    method.Name,
                    method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty,
                    BuildParameterSchema(method.GetParameters())
                )));

        string pureToolSearchPayload = BuildPromptSurfacePayload(
            exposedHandlers
                .Where(method => method.Name is nameof(McpToolHandlers.search_tools) or nameof(McpToolHandlers.call_tool))
                .Select(method =>
                    (
                        method.Name,
                        method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty,
                        BuildParameterSchema(method.GetParameters())
                    )));

        (int traditionalToolCount, int traditionalTokens) = MeasurePromptSurface(traditionalPayload);
        (int currentToolCount, int currentTokens) = MeasurePromptSurface(currentArchitecturePayload);
        (int pureToolSearchCount, int pureToolSearchTokens) = MeasurePromptSurface(pureToolSearchPayload);

        int currentReductionTokens = traditionalTokens - currentTokens;
        double currentReductionPercentage = traditionalTokens == 0 ? 0 : currentReductionTokens * 100d / traditionalTokens;
        int pureToolSearchReductionTokens = traditionalTokens - pureToolSearchTokens;
        double pureToolSearchReductionPercentage = traditionalTokens == 0 ? 0 : pureToolSearchReductionTokens * 100d / traditionalTokens;

        Console.WriteLine("[Bootstrap] Context footprint (approx tokens using ceil(chars/4)):");
        Console.WriteLine($"[Bootstrap]   - traditional-mcp: {traditionalToolCount} tools, {traditionalTokens} tok (baseline)");
        Console.WriteLine($"[Bootstrap]   - tool-search+code-mode: {currentToolCount} tools, {currentTokens} tok, save {currentReductionTokens} tok ({currentReductionPercentage:F1}%)");
        Console.WriteLine($"[Bootstrap]   - pure-tool-search: {pureToolSearchCount} tools, {pureToolSearchTokens} tok, save {pureToolSearchReductionTokens} tok ({pureToolSearchReductionPercentage:F1}%)");
    }

    private static string ToShortDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return "(no description)";
        }

        string normalized = string.Join(' ', description.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        int firstSentenceEnd = normalized.IndexOf('.');
        string shortDescription = firstSentenceEnd >= 0
            ? normalized[..(firstSentenceEnd + 1)]
            : normalized;

        const int maxLength = 100;
        if (shortDescription.Length <= maxLength)
        {
            return shortDescription;
        }

        return $"{shortDescription[..(maxLength - 3)]}...";
    }

    private static int CountApproxTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(text.Length / 4d));
    }

    private static string BuildParameterSchema(ParameterInfo[] parameters)
    {
        Dictionary<string, object?> properties = [];

        foreach (ParameterInfo parameter in parameters)
        {
            if (parameter.GetCustomAttribute<FromServicesAttribute>() is not null ||
                parameter.ParameterType == typeof(CancellationToken))
            {
                continue;
            }

            properties[parameter.Name ?? "unknown"] = new
            {
                type = parameter.ParameterType.Name,
                description = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty,
            };
        }

        return JsonSerializer.Serialize(new
        {
            type = "object",
            properties,
        });
    }

    private static string BuildPromptSurfacePayload(IEnumerable<(string Name, string Description, string Schema)> tools)
    {
        return JsonSerializer.Serialize(tools.Select(tool => new
        {
            name = tool.Name,
            description = tool.Description,
            inputSchema = tool.Schema,
        }));
    }

    private static IReadOnlyList<MethodInfo> GetExposedMcpHandlers()
    {
        return typeof(McpToolHandlers)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static (int ToolCount, int TokenCount) MeasurePromptSurface(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        return (document.RootElement.GetArrayLength(), CountApproxTokens(payload));
    }
}