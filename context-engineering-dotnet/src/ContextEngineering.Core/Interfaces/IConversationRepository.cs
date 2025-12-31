using ContextEngineering.Core.Entities;

namespace ContextEngineering.Core.Interfaces;

public interface IConversationRepository
{
    Task<ConversationThread?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ConversationThread?> GetByUserIdAsync(string userId, CancellationToken ct = default);
    Task<ConversationThread> CreateAsync(string userId, CancellationToken ct = default);
    Task<ConversationThread> UpdateAsync(ConversationThread thread, CancellationToken ct = default);
    Task AddMessageAsync(Guid threadId, ChatMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid threadId, CancellationToken ct = default);
    Task IncrementReductionCountAsync(Guid threadId, CancellationToken ct = default);
    Task RecordTokenUsageAsync(Guid threadId, int inputTokens, int outputTokens, bool afterReduction = false, CancellationToken ct = default);
}
