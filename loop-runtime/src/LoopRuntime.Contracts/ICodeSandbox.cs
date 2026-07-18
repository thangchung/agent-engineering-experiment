namespace LoopRuntime.Contracts;

public interface ICodeSandbox
{
    Task<RunResult> RunAsync(string code, CancellationToken cancellationToken);
}
