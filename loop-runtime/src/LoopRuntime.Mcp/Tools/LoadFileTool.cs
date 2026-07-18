using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using LoopRuntime.Contracts;
using Microsoft.Extensions.Logging;

namespace LoopRuntime.Mcp.Tools;

[McpServerToolType]
public sealed class LoadFileTool
{
    private readonly IMcpTools _tools;
    private readonly ILogger<LoadFileTool> _logger;

    public LoadFileTool(IMcpTools tools, ILogger<LoadFileTool> logger)
    {
        _tools = tools;
        _logger = logger;
    }

    [McpServerTool(Name = "load_file"), Description("Read the contents of a file. Returns the source code as a string.")]
    public async Task<string> LoadFileAsync(
        [Description("Absolute path to the file to read.")] string path,
        CancellationToken cancellationToken)
    {
        Activity.Current?.SetTag("mcp.method.name", "tools/call");
        Activity.Current?.SetTag("mcp.resource.type", "tool");
        Activity.Current?.SetTag("mcp.target", "LoopRuntime.Mcp");
        Activity.Current?.SetTag("gen_ai.tool.name", "load_file");
        Activity.Current?.SetTag("mcp.target.path", path);
        _logger.LogInformation("load_file tool invoked. Path={Path}", path);
        var content = await _tools.LoadFileAsync(path, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("load_file tool completed. Path={Path} Length={Length}", path, content.Length);
        return content;
    }
}
