namespace Claw.Channels;

public interface IAgentClient
{
    Task<string> InvokeAsync(string input, string sessionId, CancellationToken ct = default);
}
