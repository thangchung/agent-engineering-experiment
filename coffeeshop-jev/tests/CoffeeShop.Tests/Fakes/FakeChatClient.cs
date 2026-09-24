using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace CoffeeShop.Tests.Fakes;

/// <summary>
/// A minimal in-memory <see cref="IChatClient"/> for offline tests (research.md §10, "fake
/// IChatClient"). Records every call so a test can assert on what the agent actually sent.
/// </summary>
public sealed class FakeChatClient(Func<IReadOnlyList<ChatMessage>, ChatOptions?, string> respond) : IChatClient
{
    public List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public static FakeChatClient Returning(string fixedText) => new((_, _) => fixedText);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        Calls.Add((list, options));
        var text = respond(list, options);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
