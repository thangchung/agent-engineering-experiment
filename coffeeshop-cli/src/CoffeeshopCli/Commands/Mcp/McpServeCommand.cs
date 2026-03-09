using CoffeeshopCli.Mcp;
using Spectre.Console.Cli;

namespace CoffeeshopCli.Commands.Mcp;

public sealed class McpServeSettings : CommandSettings
{
}

/// <summary>
/// Starts stdio server for MCP-compatible JSON-RPC methods.
/// </summary>
public sealed class McpServeCommand : Command<McpServeSettings>
{
    private readonly McpServerHost _host;

    public McpServeCommand(McpServerHost host)
    {
        _host = host;
    }

    public override int Execute(CommandContext context, McpServeSettings settings)
    {
        return _host.RunStdio();
    }
}
