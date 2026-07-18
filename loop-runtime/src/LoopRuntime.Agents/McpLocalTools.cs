using System.Diagnostics;
using LoopRuntime.Contracts;

namespace LoopRuntime.Agents;

public sealed class McpLocalTools : IMcpTools
{
    private readonly ICodeSandbox _sandbox;
    private readonly string _outputsRoot;

    public McpLocalTools(ICodeSandbox sandbox)
    {
        _sandbox = sandbox;
        _outputsRoot = Path.Combine(AppContext.BaseDirectory, "outputs");
        Directory.CreateDirectory(_outputsRoot);
    }

    public async Task<string> WriteFileAsync(string sessionId, string name, string content, CancellationToken cancellationToken)
    {
        StampTags("write_file", sessionId);
        var safeName = string.IsNullOrWhiteSpace(name) ? "main.py" : name;
        var dir = Path.Combine(_outputsRoot, SanitizeSegment(sessionId));
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, safeName);
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        return path;
    }

    public async Task<string> EditFileAsync(string sessionId, string path, string content, CancellationToken cancellationToken)
    {
        StampTags("edit_file", sessionId);
        var fullPath = GetValidatedPath(sessionId, path);
        await File.WriteAllTextAsync(fullPath, content, cancellationToken).ConfigureAwait(false);
        return fullPath;
    }

    public async Task<string> LoadFileAsync(string path, CancellationToken cancellationToken)
    {
        StampTags("load_file", path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("File not found", fullPath);
        }

        return await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RunResult> RunPythonAsync(string path, CancellationToken cancellationToken)
    {
        StampTags("run_python", path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return new RunResult(1, string.Empty, $"File not found: {fullPath}", false);
        }

        var code = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        return await _sandbox.RunAsync(code, cancellationToken).ConfigureAwait(false);
    }

    private static void StampTags(string toolName, string sessionIdOrPath)
    {
        var current = Activity.Current;
        if (current is null)
        {
            return;
        }

        current.SetTag("mcp.method.name", "tools/call");
        current.SetTag("mcp.resource.type", "tool");
        current.SetTag("mcp.target", "LoopRuntime.Mcp");
        current.SetTag("gen_ai.tool.name", toolName);

        if (toolName is "load_file" or "run_python")
        {
            current.SetTag("mcp.target.path", sessionIdOrPath);
        }
        else
        {
            current.SetTag("mcp.session.id", sessionIdOrPath);
        }
    }

    private string GetValidatedPath(string sessionId, string path)
    {
        var dir = Path.Combine(_outputsRoot, SanitizeSegment(sessionId));
        Directory.CreateDirectory(dir);

        var fullPath = Path.GetFullPath(path);
        var fullDir = Path.GetFullPath(dir);
        if (!fullPath.StartsWith(fullDir, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Path escapes session outputs root.");
        }

        return fullPath;
    }

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }
}
