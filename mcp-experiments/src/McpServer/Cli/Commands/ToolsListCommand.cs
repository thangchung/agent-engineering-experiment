using System.ComponentModel;
using McpServer.Registry;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace McpServer.Cli.Commands;

/// <summary>
/// Lists all tools visible to the current caller context.
/// </summary>
[Description("List all visible tools")]
internal sealed class ToolsListCommand(IToolRegistry toolRegistry, UserContext userContext, ILogger<ToolsListCommand> logger)
    : BaseCliCommand<ToolsListCommand.Settings>(toolRegistry, userContext)
{
    /// <summary>
    /// Empty command settings used by Spectre for argument binding.
    /// </summary>
    internal sealed class Settings : VerboseCommandSettings;

    /// <inheritdoc />
    public override Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (settings.Verbose)
        {
            logger.LogInformation("Listing tools for current user context.");
        }

        IReadOnlyList<ToolDescriptor> tools = ToolRegistry
            .GetVisibleTools(UserContext)
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (settings.Verbose)
        {
            logger.LogInformation("Resolved {ToolCount} visible tools.", tools.Count);
        }

        if (tools.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No tools are visible for the current context.[/]");
            return Task.FromResult(0);
        }

        Table table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Name");
        table.AddColumn("Description");
        table.AddColumn("Tags");

        foreach (ToolDescriptor tool in tools)
        {
            table.AddRow(tool.Name, tool.Description, string.Join(", ", tool.Tags));
        }

        AnsiConsole.Write(table);
        return Task.FromResult(0);
    }
}
