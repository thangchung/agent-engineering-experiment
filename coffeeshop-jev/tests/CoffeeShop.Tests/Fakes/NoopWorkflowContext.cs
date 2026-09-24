using Microsoft.Agents.AI.Workflows;

namespace CoffeeShop.Tests.Fakes;

/// <summary>
/// A minimal <see cref="IWorkflowContext"/> for calling one executor's <c>HandleAsync</c>
/// directly, without building a full workflow graph. Backed by a real in-memory dictionary for
/// state, so <c>ReadOrInitStateAsync</c>/<c>QueueStateUpdateAsync</c> round-trip like the real
/// thing within a single test.
/// </summary>
public sealed class NoopWorkflowContext : IWorkflowContext
{
    private readonly Dictionary<string, object?> _state = [];
    public List<object> SentMessages { get; } = [];
    public List<WorkflowEvent> Events { get; } = [];
    public List<object> Outputs { get; } = [];

    public ValueTask AddEventAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(workflowEvent);
        return ValueTask.CompletedTask;
    }

    public ValueTask SendMessageAsync(object message, string? targetId = null, CancellationToken cancellationToken = default)
    {
        SentMessages.Add(message);
        return ValueTask.CompletedTask;
    }

    public ValueTask YieldOutputAsync(object output, CancellationToken cancellationToken = default)
    {
        Outputs.Add(output);
        return ValueTask.CompletedTask;
    }

    public ValueTask RequestHaltAsync() => ValueTask.CompletedTask;

    public ValueTask<T?> ReadStateAsync<T>(string key, string? scopeName = null, CancellationToken cancellationToken = default)
    {
        var full = ScopedKey(key, scopeName);
        return ValueTask.FromResult(_state.TryGetValue(full, out var value) ? (T?)value : default);
    }

    public ValueTask<T> ReadOrInitStateAsync<T>(string key, Func<T> initialStateFactory, string? scopeName = null, CancellationToken cancellationToken = default)
    {
        var full = ScopedKey(key, scopeName);
        if (_state.TryGetValue(full, out var value) && value is T typed)
        {
            return ValueTask.FromResult(typed);
        }

        var initial = initialStateFactory();
        _state[full] = initial;
        return ValueTask.FromResult(initial);
    }

    public ValueTask QueueStateUpdateAsync<T>(string key, T? value, string? scopeName = null, CancellationToken cancellationToken = default)
    {
        _state[ScopedKey(key, scopeName)] = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask QueueClearScopeAsync(string? scopeName = null, CancellationToken cancellationToken = default)
    {
        var prefix = scopeName ?? string.Empty;
        foreach (var key in _state.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            _state.Remove(key);
        }

        return ValueTask.CompletedTask;
    }

    private static string ScopedKey(string key, string? scopeName) => $"{scopeName ?? string.Empty}::{key}";

    public ValueTask<HashSet<string>> ReadStateKeysAsync(string? scopeName = null, CancellationToken cancellationToken = default)
    {
        var prefix = ScopedKey(string.Empty, scopeName);
        var keys = _state.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Select(k => k[prefix.Length..]);
        return ValueTask.FromResult(new HashSet<string>(keys));
    }

    public IReadOnlyDictionary<string, string> TraceContext { get; } = new Dictionary<string, string>();

    public bool ConcurrentRunsEnabled => false;
}
