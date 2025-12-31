using ContextEngineering.Core.Interfaces;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.Json;

// Use alias to avoid ambiguity with Microsoft.Extensions.AI.ChatMessage
using DomainChatMessage = ContextEngineering.Core.Entities.ChatMessage;

namespace ContextEngineering.Infrastructure.Plugins;

/// <summary>
/// AIFunction plugin for conversation operations.
/// Provides tools for managing conversation threads and messages.
/// Use AIFunctionFactory.Create() to wrap methods as AIFunction tools.
/// </summary>
public class ConversationPlugin(IConversationRepository conversationRepository)
{
    /// <summary>
    /// Get or create a conversation thread for a user.
    /// </summary>
    [Description("Get or create a conversation thread for a user. Returns the thread ID and metadata.")]
    public async Task<string> GetOrCreateConversationAsync(
        [Description("The user ID to get or create conversation for")] string userId,
        CancellationToken ct = default)
    {
        var thread = await conversationRepository.GetByUserIdAsync(userId, ct)
                     ?? await conversationRepository.CreateAsync(userId, ct);

        return JsonSerializer.Serialize(new
        {
            threadId = thread.Id,
            userId = thread.UserId,
            messageCount = thread.MessageCount,
            totalTokensUsed = thread.TotalTokensUsed,
            reductionEventCount = thread.ReductionEventCount,
            createdAt = thread.CreatedAt
        });
    }

    /// <summary>
    /// Get all messages in a conversation thread.
    /// </summary>
    [Description("Get all messages in a conversation thread.")]
    public async Task<string> GetConversationMessagesAsync(
        [Description("The thread ID (GUID) to get messages for")] string threadId,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(threadId, out var guid))
        {
            return "Invalid thread ID format. Must be a valid GUID.";
        }

        var messages = await conversationRepository.GetMessagesAsync(guid, ct);

        if (messages.Count == 0)
        {
            return "[]";
        }

        return JsonSerializer.Serialize(messages.Select(m => new
        {
            role = m.Role,
            content = m.Content,
            tokenCount = m.TokenCount,
            createdAt = m.CreatedAt
        }));
    }

    /// <summary>
    /// Add a message to a conversation thread.
    /// </summary>
    [Description("Add a message to a conversation thread.")]
    public async Task<string> AddMessageAsync(
        [Description("The thread ID (GUID) to add message to")] string threadId,
        [Description("The role of the message sender: 'user' or 'assistant'")] string role,
        [Description("The content of the message")] string content,
        [Description("The token count for this message")] int tokenCount,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(threadId, out var guid))
        {
            return "Invalid thread ID format. Must be a valid GUID.";
        }

        if (role != "user" && role != "assistant")
        {
            return "Invalid role. Must be 'user' or 'assistant'.";
        }

        var message = new DomainChatMessage
        {
            Role = role,
            Content = content,
            TokenCount = tokenCount
        };

        await conversationRepository.AddMessageAsync(guid, message, ct);
        return $"Message added successfully. Role: {role}, Tokens: {tokenCount}";
    }

    /// <summary>
    /// Record token usage for a conversation.
    /// </summary>
    [Description("Record token usage for a conversation and optionally increment reduction count.")]
    public async Task<string> RecordTokenUsageAsync(
        [Description("The thread ID (GUID) to record usage for")] string threadId,
        [Description("Number of tokens in the prompt")] int promptTokens,
        [Description("Number of tokens in the completion")] int completionTokens,
        [Description("Whether the conversation was reduced/summarized")] bool wasReduced,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(threadId, out var guid))
        {
            return "Invalid thread ID format. Must be a valid GUID.";
        }

        await conversationRepository.RecordTokenUsageAsync(guid, promptTokens, completionTokens, wasReduced, ct);

        if (wasReduced)
        {
            await conversationRepository.IncrementReductionCountAsync(guid, ct);
        }

        return $"Token usage recorded. Prompt: {promptTokens}, Completion: {completionTokens}, Reduced: {wasReduced}";
    }

    /// <summary>
    /// Create a new conversation thread for a user.
    /// </summary>
    [Description("Create a new conversation thread for a user, ending the current one.")]
    public async Task<string> CreateNewConversationAsync(
        [Description("The user ID to create a new conversation for")] string userId,
        CancellationToken ct = default)
    {
        var thread = await conversationRepository.CreateAsync(userId, ct);

        return JsonSerializer.Serialize(new
        {
            threadId = thread.Id,
            userId = thread.UserId,
            createdAt = thread.CreatedAt
        });
    }

    /// <summary>
    /// Get conversation summary/statistics for a user.
    /// </summary>
    [Description("Get conversation summary/statistics for a user.")]
    public async Task<string> GetConversationSummaryAsync(
        [Description("The user ID to get conversation summary for")] string userId,
        CancellationToken ct = default)
    {
        var thread = await conversationRepository.GetByUserIdAsync(userId, ct);

        if (thread is null)
        {
            return "No conversation found for this user.";
        }

        return JsonSerializer.Serialize(new
        {
            threadId = thread.Id,
            userId = thread.UserId,
            messageCount = thread.MessageCount,
            totalTokensUsed = thread.TotalTokensUsed,
            reductionEventCount = thread.ReductionEventCount,
            createdAt = thread.CreatedAt
        });
    }

    /// <summary>
    /// Get all AIFunction tools from this plugin.
    /// </summary>
    public IEnumerable<AIFunction> GetTools()
    {
        yield return AIFunctionFactory.Create(
            GetOrCreateConversationAsync,
            name: "get_or_create_conversation",
            description: "Get or create a conversation thread for a user. Returns the thread ID and metadata.");

        yield return AIFunctionFactory.Create(
            GetConversationMessagesAsync,
            name: "get_conversation_messages",
            description: "Get all messages in a conversation thread.");

        yield return AIFunctionFactory.Create(
            AddMessageAsync,
            name: "add_message",
            description: "Add a message to a conversation thread.");

        yield return AIFunctionFactory.Create(
            RecordTokenUsageAsync,
            name: "record_token_usage",
            description: "Record token usage for a conversation and optionally increment reduction count.");

        yield return AIFunctionFactory.Create(
            CreateNewConversationAsync,
            name: "create_new_conversation",
            description: "Create a new conversation thread for a user, ending the current one.");

        yield return AIFunctionFactory.Create(
            GetConversationSummaryAsync,
            name: "get_conversation_summary",
            description: "Get conversation summary/statistics for a user.");
    }
}
