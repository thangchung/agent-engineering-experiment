using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgenticTodo.TodoAgent;

public sealed class TaggingChatClient : IChatClient
{
    private static readonly ActivitySource ActivitySource = new("AgenticTodo.TodoAgent");

    private readonly IChatClient _inner;
    private readonly string _modelId;
    private readonly string _providerName;
    private readonly bool _captureMessageContent;

    public TaggingChatClient(IChatClient inner, bool? captureMessageContent = null)
    {
        _inner = inner;
        _modelId = ExtractModelId(inner);
        _providerName = ExtractProviderName(inner);
        _captureMessageContent = captureMessageContent ?? ShouldCaptureMessageContent();
    }

    private static bool ShouldCaptureMessageContent()
    {
        var value = Environment.GetEnvironmentVariable("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT");
        return bool.TryParse(value, out var result) && result;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity($"chat {_modelId}", ActivityKind.Client);
        StampRequest(activity, messages);

        var response = await _inner.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        StampResponse(activity, response);
        return response;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity($"chat {_modelId}", ActivityKind.Client);
        StampRequest(activity, messages);

        string? modelId = null;
        long? inputTokens = null;
        long? outputTokens = null;

        await foreach (var update in _inner.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(update.ModelId))
            {
                modelId = update.ModelId;
            }

            if (update.Contents.OfType<UsageContent>().FirstOrDefault()?.Details is { } usage)
            {
                inputTokens ??= usage.InputTokenCount;
                outputTokens ??= usage.OutputTokenCount;
            }

            yield return update;
        }

        StampResponse(activity, modelId, inputTokens, outputTokens);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        _inner.GetService(serviceType, serviceKey);

    public void Dispose() => _inner.Dispose();

    private void StampRequest(Activity? activity, IEnumerable<ChatMessage>? messages)
    {
        var tags = new (string Key, object? Value)[]
        {
            ("gen_ai.operation.name", "chat"),
            ("gen_ai.provider.name", _providerName),
            ("gen_ai.system", _providerName),
            ("gen_ai.request.model", _modelId)
        };

        ApplyTags(activity, Activity.Current, tags);

        if (_captureMessageContent && messages is not null)
        {
            var inputMessages = JsonSerializer.Serialize(messages.Select(ToInputMessage));
            activity?.SetTag("gen_ai.input.messages", inputMessages);
            Activity.Current?.SetTag("gen_ai.input.messages", inputMessages);
        }
    }

    private void StampResponse(Activity? activity, ChatResponse response)
    {
        StampResponse(activity, response.ModelId, response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount);

        if (_captureMessageContent && response.Messages is not null)
        {
            var outputMessages = JsonSerializer.Serialize(response.Messages.Select(ToOutputMessage));
            activity?.SetTag("gen_ai.output.messages", outputMessages);
            Activity.Current?.SetTag("gen_ai.output.messages", outputMessages);
        }
    }

    private static void StampResponse(Activity? activity, string? modelId, long? inputTokens, long? outputTokens)
    {
        var tags = new List<(string Key, object? Value)>();
        if (!string.IsNullOrEmpty(modelId))
        {
            tags.Add(("gen_ai.response.model", modelId));
        }

        if (inputTokens is { } i)
        {
            tags.Add(("gen_ai.usage.input_tokens", i));
        }

        if (outputTokens is { } o)
        {
            tags.Add(("gen_ai.usage.output_tokens", o));
        }

        if (tags.Count > 0)
        {
            ApplyTags(activity, Activity.Current, tags.ToArray());
        }
    }

    private static Dictionary<string, object> ToInputMessage(ChatMessage message)
    {
        var text = string.Concat(message.Contents.OfType<TextContent>().Select(c => c.Text));
        return new Dictionary<string, object>
        {
            ["role"] = message.Role.Value,
            ["parts"] = new[] { new Dictionary<string, object> { ["type"] = "text", ["content"] = text } }
        };
    }

    private static Dictionary<string, object> ToOutputMessage(ChatMessage message)
    {
        var text = string.Concat(message.Contents.OfType<TextContent>().Select(c => c.Text));
        return new Dictionary<string, object>
        {
            ["role"] = message.Role.Value,
            ["parts"] = new[] { new Dictionary<string, object> { ["type"] = "text", ["content"] = text } },
            ["finish_reason"] = "stop"
        };
    }

    private static void ApplyTags(Activity? activity, Activity? ambient, (string Key, object? Value)[] tags)
    {
        foreach (var (key, value) in tags)
        {
            activity?.SetTag(key, value);
            ambient?.SetTag(key, value);
        }
    }

    private static string ExtractModelId(IChatClient client) =>
        (client.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata)?.DefaultModelId ?? "unknown";

    private static string ExtractProviderName(IChatClient client) =>
        (client.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata)?.ProviderName ?? "unknown";
}
