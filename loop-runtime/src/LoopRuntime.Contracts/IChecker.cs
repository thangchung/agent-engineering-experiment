namespace LoopRuntime.Contracts;

public interface IChecker
{
    Task<Verdict> ReviewAsync(string sessionId, string path, CancellationToken cancellationToken);
}
