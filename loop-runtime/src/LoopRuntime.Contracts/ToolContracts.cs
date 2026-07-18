namespace LoopRuntime.Contracts;

public sealed record WriteFileRequest(string SessionId, string Name, string Content);
public sealed record EditFileRequest(string SessionId, string Path, string Content);
public sealed record LoadFileRequest(string Path);
public sealed record RunPythonRequest(string Path);
public sealed record ToolPathResponse(string Path);
public sealed record ToolContentResponse(string Content);

public interface IMcpTools
{
    Task<string> WriteFileAsync(string sessionId, string name, string content, CancellationToken cancellationToken);
    Task<string> EditFileAsync(string sessionId, string path, string content, CancellationToken cancellationToken);
    Task<string> LoadFileAsync(string path, CancellationToken cancellationToken);
    Task<RunResult> RunPythonAsync(string path, CancellationToken cancellationToken);
}
