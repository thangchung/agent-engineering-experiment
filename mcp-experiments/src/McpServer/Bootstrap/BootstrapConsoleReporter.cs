using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using McpServer.Registry;
using McpServer.CodeMode;
using McpServer.ToolSearch;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol.Server;
using Spectre.Console;

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

        AnsiConsole.Write(new Rule("[aqua]Bootstrap[/]").RuleStyle("grey"));

        if (openApiSources.Count > 0)
        {
            string sourceSummary = string.Join(", ", openApiSources.Select(static source => $"'{Markup.Escape(source.Name)}'"));
            AnsiConsole.MarkupLine($"[grey][[Bootstrap]][/] Loading [aqua]{openApiSources.Count}[/] OpenAPI source(s): {sourceSummary}");
        }

        AnsiConsole.MarkupLine($"[grey][[Bootstrap]][/] Total tools registered: [aqua]{tools.Count}[/] (status + OpenAPI-generated)");

        List<IGrouping<string, ToolDescriptor>> toolsByTag = tools
            .Where(static tool => !tool.IsSynthetic && tool.Tags.Count > 0)
            .GroupBy(static tool => tool.Tags.FirstOrDefault() ?? "untagged")
            .OrderBy(static group => group.Key)
            .ToList();

        Tree toolTree = new("[grey][[Bootstrap]][/] Tool inventory")
        {
            Guide = TreeGuide.Line,
        };

        foreach (IGrouping<string, ToolDescriptor> group in toolsByTag)
        {
            TreeNode groupNode = toolTree.AddNode($"[yellow]{Markup.Escape(group.Key)}[/]: [aqua]{group.Count()}[/] tool(s)");

            foreach (ToolDescriptor tool in group.OrderBy(static tool => tool.Name, StringComparer.OrdinalIgnoreCase))
            {
                groupNode.AddNode($"[white]{Markup.Escape(tool.Name)}[/]: [grey]{Markup.Escape(ToShortDescription(tool.Description))}[/]");
            }
        }

        AnsiConsole.Write(toolTree);

        WritePromptSurfaceComparison(tools);
    }

    private static void WritePromptSurfaceComparison(IReadOnlyList<ToolDescriptor> tools)
    {
        IReadOnlyList<MethodInfo> exposedHandlers = McpHandlerCatalog.GetExposedToolMethods();

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
                .Where(method => method.Name is nameof(ToolSearchHandlers.SearchTools) or nameof(ToolSearchHandlers.CallTool))
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

        AnsiConsole.MarkupLine("[grey][[Bootstrap]][/] Context footprint ([italic]approx tokens using ceil(chars/4)[/]):");

        Table table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Mode")
            .AddColumn(new TableColumn("Tools").RightAligned())
            .AddColumn(new TableColumn("Tokens").RightAligned())
            .AddColumn("Savings");

        table.AddRow("traditional-mcp", traditionalToolCount.ToString(), traditionalTokens.ToString(), "baseline");
        table.AddRow(
            "tool-Search+code-mode",
            currentToolCount.ToString(),
            currentTokens.ToString(),
            $"{currentReductionTokens} tok ({currentReductionPercentage:F1}%)");
        table.AddRow(
            "pure-tool-Search",
            pureToolSearchCount.ToString(),
            pureToolSearchTokens.ToString(),
            $"{pureToolSearchReductionTokens} tok ({pureToolSearchReductionPercentage:F1}%)");

        AnsiConsole.Write(table);
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

    private static (int ToolCount, int TokenCount) MeasurePromptSurface(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        return (document.RootElement.GetArrayLength(), CountApproxTokens(payload));
    }
}