using System.Text.Json;
using Microsoft.Agents.AI;

namespace AgentService.Providers;

/// <summary>
/// Thread implementation for FoundryLocalAgent.
/// Stores conversation messages in memory with a unique thread ID.
/// </summary>
public class FoundryLocalAgentThread : InMemoryAgentThread
{
    /// <summary>
    /// Gets the unique identifier for this thread.
    /// </summary>
    public string ThreadId { get; }

    public FoundryLocalAgentThread() : base()
    {
        ThreadId = Guid.NewGuid().ToString("N");
    }

    public FoundryLocalAgentThread(JsonElement serializedThreadState, JsonSerializerOptions? jsonSerializerOptions = null)
        : base(serializedThreadState, jsonSerializerOptions)
    {
        // Try to read ThreadId from serialized state, otherwise generate new one
        if (serializedThreadState.TryGetProperty("threadId", out var threadIdEl) &&
            threadIdEl.ValueKind == JsonValueKind.String)
        {
            ThreadId = threadIdEl.GetString() ?? Guid.NewGuid().ToString("N");
        }
        else
        {
            ThreadId = Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>
    /// Serializes the thread state including the ThreadId.
    /// </summary>
    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        var baseState = base.Serialize(jsonSerializerOptions);

        // Merge ThreadId into the serialized state
        using var doc = JsonDocument.Parse(baseState.GetRawText());
        var dict = new Dictionary<string, object?>
        {
            ["threadId"] = ThreadId
        };

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.Clone();
        }

        return JsonSerializer.SerializeToElement(dict);
    }
}
