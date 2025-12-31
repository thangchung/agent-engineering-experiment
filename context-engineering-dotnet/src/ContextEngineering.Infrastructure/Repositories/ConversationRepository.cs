using ContextEngineering.Core.Entities;
using ContextEngineering.Core.Interfaces;
using ContextEngineering.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContextEngineering.Infrastructure.Repositories;

public class ConversationRepository(AppDbContext dbContext) : IConversationRepository
{
    public async Task<ConversationThread?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await dbContext.ConversationThreads
            .Include(t => t.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<ConversationThread?> GetByUserIdAsync(string userId, CancellationToken ct = default)
    {
        return await dbContext.ConversationThreads
            .Include(t => t.Messages.OrderBy(m => m.CreatedAt))
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ConversationThread> CreateAsync(string userId, CancellationToken ct = default)
    {
        var thread = new ConversationThread
        {
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        
        dbContext.ConversationThreads.Add(thread);
        await dbContext.SaveChangesAsync(ct);
        
        return thread;
    }

    public async Task<ConversationThread> UpdateAsync(ConversationThread thread, CancellationToken ct = default)
    {
        dbContext.ConversationThreads.Update(thread);
        await dbContext.SaveChangesAsync(ct);
        return thread;
    }

    public async Task AddMessageAsync(Guid threadId, ChatMessage message, CancellationToken ct = default)
    {
        message.ConversationThreadId = threadId;
        dbContext.ChatMessages.Add(message);
        
        var thread = await dbContext.ConversationThreads.FindAsync([threadId], ct);
        if (thread is not null)
        {
            thread.MessageCount++;
            thread.TotalTokensUsed += message.TokenCount;
        }
        
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid threadId, CancellationToken ct = default)
    {
        return await dbContext.ChatMessages
            .Where(m => m.ConversationThreadId == threadId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task IncrementReductionCountAsync(Guid threadId, CancellationToken ct = default)
    {
        await dbContext.ConversationThreads
            .Where(t => t.Id == threadId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.ReductionEventCount, t => t.ReductionEventCount + 1)
                .SetProperty(t => t.LastReductionAt, DateTime.UtcNow), ct);
    }

    public async Task RecordTokenUsageAsync(Guid threadId, int inputTokens, int outputTokens, bool afterReduction = false, CancellationToken ct = default)
    {
        var usage = new TokenUsage
        {
            ConversationThreadId = threadId,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            AfterReduction = afterReduction,
            RecordedAt = DateTime.UtcNow
        };
        
        dbContext.TokenUsages.Add(usage);
        await dbContext.SaveChangesAsync(ct);
    }
}
