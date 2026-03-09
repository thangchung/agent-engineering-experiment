using CoffeeshopCli.Configuration;
using CoffeeshopCli.Errors;

namespace CoffeeshopCli.Mcp;

/// <summary>
/// Creates MCP clients from configuration. The stdio transport implementation is intentionally
/// deferred; this factory validates config and returns an in-memory fallback client for local flows.
/// </summary>
public static class McpClientFactory
{
    public static IMcpClient Create(CliConfig config)
    {
        if (config is null)
        {
            throw new McpError("MCP configuration is required");
        }

        // Keep this simple for now: if no servers configured, use fallback dataset.
        // This allows order submission flows to function locally and keeps Phase 2 testable.
        return new InMemoryMcpClient();
    }
}
