using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace McpServer.Cli.Commands;

/// <summary>
/// Placeholder for stdio serve mode in a later phase.
/// </summary>
[Description("Start CLI serve mode (placeholder)")]
internal sealed class ServeCommand(CliConfig cliConfig) : AsyncCommand<ServeCommand.Settings>
{
    /// <summary>
    /// Empty command settings used by Spectre for argument binding.
    /// </summary>
    internal sealed class Settings : VerboseCommandSettings;

    /// <inheritdoc />
    public override Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (cliConfig.CliServeMode)
        {
            AnsiConsole.MarkupLine("[yellow]Serve mode flag is enabled, but stdio bridge is not implemented yet.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Serve mode is not implemented yet. This command is a phase-1 scaffold.[/]");
        }

        return Task.FromResult(0);
    }
}
