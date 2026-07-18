using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using LoopRuntime.Contracts;
using Microsoft.Extensions.Logging;

namespace LoopRuntime.Mcp.Tools;

[McpServerToolType]
public sealed class RunPythonTool
{
    private readonly IMcpTools _tools;
    private readonly ILogger<RunPythonTool> _logger;

    public RunPythonTool(IMcpTools tools, ILogger<RunPythonTool> logger)
    {
        _tools = tools;
        _logger = logger;
    }

    [McpServerTool(Name = "run_python"), Description("Execute a Python file in an isolated Docker sandbox. Returns exit code, stdout, stderr, and timedOut flag.")]
    public async Task<string> RunPythonAsync(
        [Description("Absolute path to the Python file to execute.")] string path,
        CancellationToken cancellationToken)
    {
        Activity.Current?.SetTag("mcp.method.name", "tools/call");
        Activity.Current?.SetTag("mcp.resource.type", "tool");
        Activity.Current?.SetTag("mcp.target", "LoopRuntime.Mcp");
        Activity.Current?.SetTag("gen_ai.tool.name", "run_python");
        Activity.Current?.SetTag("mcp.target.path", path);
        _logger.LogInformation("run_python tool invoked. Path={Path}", path);
        var result = await _tools.RunPythonAsync(path, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("run_python tool completed. Path={Path} ExitCode={ExitCode} TimedOut={TimedOut}", path, result.ExitCode, result.TimedOut);
        return System.Text.Json.JsonSerializer.Serialize(result);
    }
}
