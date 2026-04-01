using McpServer.Registry;
using System.ComponentModel;
using Spectre.Console.Cli;

namespace McpServer.Cli.Commands;

internal abstract class VerboseCommandSettings : CommandSettings
{
    [CommandOption("--verbose")]
    [Description("Enable verbose CLI logs for this command")]
    public bool Verbose { get; init; }
}

/// <summary>
/// Base command that provides shared registry/context dependencies.
/// </summary>
/// <typeparam name="TSettings">Command settings type.</typeparam>
internal abstract class BaseCliCommand<TSettings>(IToolRegistry toolRegistry, UserContext userContext)
    : AsyncCommand<TSettings>
    where TSettings : VerboseCommandSettings
{
    protected IToolRegistry ToolRegistry { get; } = toolRegistry;

    protected UserContext UserContext { get; } = userContext;
}
