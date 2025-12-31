using ContextEngineering.Core.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ContextEngineering.Infrastructure.Services;

/// <summary>
/// A chat reducer that truncates messages when total tokens exceed a threshold.
/// </summary>
public class TokenCountingChatReducer : IChatHistoryReducer
{
    private readonly ITokenCounter _tokenCounter;
    private readonly ILogger<TokenCountingChatReducer> _logger;
    private readonly int _maxTokens;
    private readonly int _targetTokens;

    public TokenCountingChatReducer(
        ITokenCounter tokenCounter,
        ILogger<TokenCountingChatReducer> logger,
        int maxTokens = 4000,
        int targetTokens = 3000)
    {
        _tokenCounter = tokenCounter;
        _logger = logger;
        _maxTokens = maxTokens;
        _targetTokens = targetTokens;
    }

    public ValueTask<IList<ChatMessage>?> ReduceAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var totalTokens = messages.Sum(m => _tokenCounter.CountTokens(m.Text ?? string.Empty));
        
        if (totalTokens <= _maxTokens)
        {
            return ValueTask.FromResult<IList<ChatMessage>?>(null); // No reduction needed
        }

        _logger.LogInformation(
            "Token count {CurrentTokens} exceeds max {MaxTokens}, reducing to {TargetTokens}",
            totalTokens, _maxTokens, _targetTokens);

        // Separate system messages (always keep) from conversation messages
        var systemMessages = messages.Where(m => m.Role == ChatRole.System).ToList();
        var conversationMessages = messages.Where(m => m.Role != ChatRole.System).ToList();

        // Calculate tokens used by system messages
        var systemTokens = systemMessages.Sum(m => _tokenCounter.CountTokens(m.Text ?? string.Empty));
        var availableTokens = _targetTokens - systemTokens;

        // Keep most recent messages that fit within available tokens
        var reducedConversation = new List<ChatMessage>();
        var currentTokens = 0;

        foreach (var message in conversationMessages.AsEnumerable().Reverse())
        {
            var messageTokens = _tokenCounter.CountTokens(message.Text ?? string.Empty);
            if (currentTokens + messageTokens > availableTokens)
            {
                break;
            }
            reducedConversation.Insert(0, message);
            currentTokens += messageTokens;
        }

        // Combine system messages with reduced conversation
        var result = new List<ChatMessage>();
        result.AddRange(systemMessages);
        result.AddRange(reducedConversation);

        _logger.LogInformation(
            "Reduced from {OriginalCount} to {ReducedCount} messages ({OriginalTokens} to {ReducedTokens} tokens)",
            messages.Count, result.Count, totalTokens, systemTokens + currentTokens);

        return ValueTask.FromResult<IList<ChatMessage>?>(result);
    }
}
