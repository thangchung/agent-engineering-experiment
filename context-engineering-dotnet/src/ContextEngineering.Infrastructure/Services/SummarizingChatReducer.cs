using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ContextEngineering.Infrastructure.Services;

/// <summary>
/// Interface for chat reducers that manage conversation history size.
/// </summary>
public interface IChatHistoryReducer
{
    ValueTask<IList<ChatMessage>?> ReduceAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A chat reducer that summarizes older messages using an LLM when the message count exceeds a threshold.
/// </summary>
public class SummarizingChatReducer : IChatHistoryReducer
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<SummarizingChatReducer> _logger;
    private readonly int _thresholdCount;
    private readonly int _targetCount;
    private readonly string _summarizationPrompt;

    public SummarizingChatReducer(
        IChatClient chatClient,
        ILogger<SummarizingChatReducer> logger,
        int thresholdCount = 10,
        int targetCount = 5)
    {
        _chatClient = chatClient;
        _logger = logger;
        _thresholdCount = thresholdCount;
        _targetCount = targetCount;
        _summarizationPrompt = """
            Provide a concise summary of the conversation history below. 
            Focus on key decisions, preferences expressed, tasks discussed, and important context.
            Keep the summary under 200 words.
            
            Conversation:
            """;
    }

    public async ValueTask<IList<ChatMessage>?> ReduceAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count <= _thresholdCount)
        {
            return null; // No reduction needed
        }

        _logger.LogInformation(
            "Reducing chat history from {CurrentCount} to {TargetCount} messages",
            messages.Count, _targetCount);

        // Separate system messages (keep them) from conversation messages
        var systemMessages = messages.Where(m => m.Role == ChatRole.System).ToList();
        var conversationMessages = messages.Where(m => m.Role != ChatRole.System).ToList();

        // Keep the most recent messages
        var recentCount = _targetCount - 1; // -1 to leave room for summary
        var messagesToSummarize = conversationMessages.Take(conversationMessages.Count - recentCount).ToList();
        var recentMessages = conversationMessages.Skip(conversationMessages.Count - recentCount).ToList();

        if (messagesToSummarize.Count == 0)
        {
            return null;
        }

        // Build conversation text for summarization
        var conversationText = string.Join("\n", messagesToSummarize.Select(m => $"{m.Role}: {m.Text}"));
        
        // Create summarization request
        var summaryRequest = new List<ChatMessage>
        {
            new(ChatRole.User, $"{_summarizationPrompt}\n{conversationText}")
        };

        try
        {
            var response = await _chatClient.GetResponseAsync(summaryRequest, options, cancellationToken);
            var summaryText = response.Text ?? "Previous conversation context.";

            // Build the reduced message list
            var reducedMessages = new List<ChatMessage>();
            
            // Add system messages first
            reducedMessages.AddRange(systemMessages);
            
            // Add summary as a system message
            reducedMessages.Add(new ChatMessage(
                ChatRole.System, 
                $"[Summary of previous conversation]: {summaryText}"));
            
            // Add recent messages
            reducedMessages.AddRange(recentMessages);

            _logger.LogInformation(
                "Chat history reduced. Summarized {SummarizedCount} messages into 1 summary. Total: {TotalCount}",
                messagesToSummarize.Count, reducedMessages.Count);

            return reducedMessages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to summarize chat history, falling back to truncation");
            
            // Fallback: simple truncation
            var fallbackMessages = new List<ChatMessage>();
            fallbackMessages.AddRange(systemMessages);
            fallbackMessages.AddRange(recentMessages);
            return fallbackMessages;
        }
    }
}
