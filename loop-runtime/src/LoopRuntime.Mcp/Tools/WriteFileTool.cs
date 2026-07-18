using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using LoopRuntime.Contracts;
using Microsoft.Extensions.Logging;

namespace LoopRuntime.Mcp.Tools;

[McpServerToolType]
public sealed class WriteFileTool
{
    private readonly IMcpTools _tools;
    private readonly ILogger<WriteFileTool> _logger;

    public WriteFileTool(IMcpTools tools, ILogger<WriteFileTool> logger)
    {
        _tools = tools;
        _logger = logger;
    }

    [McpServerTool(Name = "write_file"), Description("Write Python source code to a new session file. Returns the absolute path.")]
    public async Task<string> WriteFileAsync(
        [Description("Session ID that groups files for one run.")] string sessionId,
        [Description("File name, e.g. fibo_20250710.py")] string name,
        [Description("Complete Python source code to write.")] string content,
        CancellationToken cancellationToken)
    {
        Activity.Current?.SetTag("mcp.method.name", "tools/call");
        Activity.Current?.SetTag("mcp.session.id", sessionId);
        Activity.Current?.SetTag("mcp.resource.type", "tool");
        Activity.Current?.SetTag("mcp.target", "LoopRuntime.Mcp");
        Activity.Current?.SetTag("gen_ai.tool.name", "write_file");
        _logger.LogInformation("write_file tool invoked. SessionId={SessionId} Name={Name} ContentLength={ContentLength}", sessionId, name, content.Length);
        var path = await _tools.WriteFileAsync(sessionId, name, content, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("write_file tool completed. SessionId={SessionId} Path={Path}", sessionId, path);
        return path;
    }
}
