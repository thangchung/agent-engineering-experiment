using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using LoopRuntime.Contracts;
using Microsoft.Extensions.Logging;

namespace LoopRuntime.Mcp.Tools;

[McpServerToolType]
public sealed class EditFileTool
{
    private readonly IMcpTools _tools;
    private readonly ILogger<EditFileTool> _logger;

    public EditFileTool(IMcpTools tools, ILogger<EditFileTool> logger)
    {
        _tools = tools;
        _logger = logger;
    }

    [McpServerTool(Name = "edit_file"), Description("Overwrite an existing session file with revised Python code. Returns the absolute path.")]
    public async Task<string> EditFileAsync(
        [Description("Session ID that owns the file.")] string sessionId,
        [Description("Absolute path returned by a previous write_file or edit_file call.")] string path,
        [Description("Revised complete Python source code.")] string content,
        CancellationToken cancellationToken)
    {
        Activity.Current?.SetTag("mcp.method.name", "tools/call");
        Activity.Current?.SetTag("mcp.session.id", sessionId);
        Activity.Current?.SetTag("mcp.resource.type", "tool");
        Activity.Current?.SetTag("mcp.target", "LoopRuntime.Mcp");
        Activity.Current?.SetTag("gen_ai.tool.name", "edit_file");
        _logger.LogInformation("edit_file tool invoked. SessionId={SessionId} Path={Path} ContentLength={ContentLength}", sessionId, path, content.Length);
        var editedPath = await _tools.EditFileAsync(sessionId, path, content, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("edit_file tool completed. SessionId={SessionId} Path={Path}", sessionId, editedPath);
        return editedPath;
    }
}
