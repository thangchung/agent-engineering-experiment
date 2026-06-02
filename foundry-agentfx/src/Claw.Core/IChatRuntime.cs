namespace Claw.Core;

public interface IChatRuntime
{
    Task<string> HandleAsync(string sessionId, string message, CancellationToken ct = default);
}