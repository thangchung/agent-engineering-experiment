using System.ComponentModel;
using System.Text.Json;
using McpServer.ToolSearch;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace McpServer.Cli.Commands;

/// <summary>
/// Invokes tools directly from the CLI.
/// </summary>
[Description("Run a tool by explicit name")]
internal sealed class QueryCommand(
    ToolInvoker toolInvoker,
    CliRuntimeOptions runtimeOptions,
    ILogger<QueryCommand> logger)
    : AsyncCommand<QueryCommand.Settings>
{
    /// <summary>
    /// Settings for query command.
    /// </summary>
    internal sealed class Settings : VerboseCommandSettings
    {
        [CommandOption("--tool <NAME>")]
        [Description("Tool name to invoke")]
        public string? ToolName { get; init; }

        [CommandOption("--args <JSON>")]
        [Description("Arguments JSON passed to the tool. Default is {}")]
        public string? ArgsJson { get; init; }

        [CommandOption("--json")]
        [Description("Return machine-readable JSON output")]
        public bool AsJson { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(settings.ToolName))
            {
                throw new ArgumentException("Provide --tool <name> and optional --args <json>.");
            }

            string argsJson = string.IsNullOrWhiteSpace(settings.ArgsJson) ? "{}" : settings.ArgsJson;
            WriteVerbose(settings, runtimeOptions, logger, $"Invoking tool '{settings.ToolName}' from CLI query.");
            using JsonDocument argumentDocument = JsonDocument.Parse(argsJson);

            ToolInvocationResult result = await toolInvoker.InvokeAsync(settings.ToolName, argumentDocument.RootElement.Clone(), CancellationToken.None);
            WriteVerbose(settings, runtimeOptions, logger, $"Tool '{settings.ToolName}' invocation finished successfully.");
            string formatted = JsonFormatter.FormatResult(result, settings.AsJson);
            Write(settings.AsJson, formatted);
            return 0;
        }
        catch (Exception exception)
        {
            int exitCode = CliErrorHandler.MapExceptionToExitCode(exception);
            WriteVerbose(settings, runtimeOptions, logger, $"Query failed: {exception.Message}");
            Write(settings.AsJson, JsonFormatter.FormatError(exception, settings.AsJson));
            return exitCode;
        }
    }

    private static void Write(bool asJson, string output)
    {
        Console.WriteLine(output);
    }

    private static void WriteVerbose(Settings settings, CliRuntimeOptions runtimeOptions, ILogger<QueryCommand> logger, string message)
    {
        if (!settings.Verbose && !runtimeOptions.Verbose)
        {
            return;
        }

        logger.LogInformation(message);
        Console.Error.WriteLine($"[verbose] {message}");
    }
}
