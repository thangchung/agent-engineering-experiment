using System.Text.Json;
using CoffeeshopCli.Mcp.Tools;

namespace CoffeeshopCli.Mcp;

/// <summary>
/// Lightweight stdio JSON-RPC host for MCP-like initialization and tool listing.
/// </summary>
public sealed class McpServerHost
{
    private readonly ToolRegistry _tools;

    public McpServerHost(ToolRegistry tools)
    {
        _tools = tools;
    }

    public int RunStdio(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                break;
            }

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
            var id = root.TryGetProperty("id", out var reqId) ? reqId : default;
            var idValue = id.ValueKind == JsonValueKind.Undefined ? 0 : JsonSerializer.Deserialize<object>(id.GetRawText());

            object response = method switch
            {
                "initialize" => new
                {
                    jsonrpc = "2.0",
                    id = idValue,
                    result = new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new { tools = new { } },
                        serverInfo = new { name = "coffeeshop-cli", version = "0.1.0" }
                    }
                },
                "tools/list" => new
                {
                    jsonrpc = "2.0",
                    id = idValue,
                    result = new
                    {
                        tools = _tools.ListTools()
                    }
                },
                _ => new
                {
                    jsonrpc = "2.0",
                    id = idValue,
                    error = new { code = -32601, message = "Method not found" }
                }
            };

            Console.WriteLine(JsonSerializer.Serialize(response));
        }

        return 0;
    }
}
