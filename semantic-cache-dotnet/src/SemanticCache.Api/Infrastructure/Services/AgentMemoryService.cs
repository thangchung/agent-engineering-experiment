using Microsoft.EntityFrameworkCore;
using SemanticCache.Api.Infrastructure.Database;
using SemanticCache.Api.Infrastructure.Database.Entities;

namespace SemanticCache.Api.Infrastructure.Services;

/// <summary>
/// Manages agent conversation threads, user context, and memory persistence.
/// Inspired by Microsoft Agent Framework patterns for conversation state management.
/// </summary>
public class AgentMemoryService(AppDbContext context)
{
    private readonly AppDbContext _context = context;

    /// <summary>
    /// Retrieves or creates a user profile.
    /// </summary>
    public async Task<UserProfile> GetOrCreateUserAsync(string userId, string name, CancellationToken cancellationToken = default)
    {
        var user = await _context.UserProfiles.FindAsync([userId], cancellationToken);
        
        if (user == null)
        {
            user = new UserProfile
            {
                Id = userId,
                Name = name,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _context.UserProfiles.AddAsync(user, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
        
        return user;
    }

    /// <summary>
    /// Adds a conversation entry to a thread for a specific user.
    /// </summary>
    public async Task AddConversationEntryAsync(
        string userId,
        string threadId,
        string role,
        string content,
        CancellationToken cancellationToken = default)
    {
        var entry = new ConversationEntry
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            ThreadId = threadId,
            Role = role,
            Content = content,
            Timestamp = DateTime.UtcNow
        };

        await _context.ConversationEntries.AddAsync(entry, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves conversation history for a specific thread.
    /// </summary>
    public async Task<List<ConversationEntry>> GetConversationHistoryAsync(
        string userId,
        string threadId,
        int maxEntries = 50,
        CancellationToken cancellationToken = default)
    {
        return await _context.ConversationEntries
            .Where(e => e.UserId == userId && e.ThreadId == threadId)
            .OrderByDescending(e => e.Timestamp)
            .Take(maxEntries)
            .OrderBy(e => e.Timestamp)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Stores or updates a context memory (key-value pair) for a user.
    /// </summary>
    public async Task SetContextMemoryAsync(
        string userId,
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.ContextMemories
            .FirstOrDefaultAsync(m => m.UserId == userId && m.MemoryKey == key, cancellationToken);

        if (existing != null)
        {
            existing.MemoryValue = value;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var memory = new ContextMemory
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                MemoryKey = key,
                MemoryValue = value,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _context.ContextMemories.AddAsync(memory, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves a specific context memory for a user.
    /// </summary>
    public async Task<string?> GetContextMemoryAsync(
        string userId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var memory = await _context.ContextMemories
            .FirstOrDefaultAsync(m => m.UserId == userId && m.MemoryKey == key, cancellationToken);

        return memory?.MemoryValue;
    }

    /// <summary>
    /// Retrieves all context memories for a user.
    /// </summary>
    public async Task<Dictionary<string, string>> GetAllContextMemoriesAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var memories = await _context.ContextMemories
            .Where(m => m.UserId == userId)
            .ToListAsync(cancellationToken);

        return memories.ToDictionary(m => m.MemoryKey, m => m.MemoryValue);
    }

    /// <summary>
    /// Retrieves user preferences.
    /// </summary>
    public async Task<Dictionary<string, string>> GetUserPreferencesAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var preferences = await _context.UserPreferences
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        return preferences.ToDictionary(p => p.PreferenceKey, p => p.PreferenceValue);
    }

    /// <summary>
    /// Sets a user preference.
    /// </summary>
    public async Task SetUserPreferenceAsync(
        string userId,
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.UserPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.PreferenceKey == key, cancellationToken);

        if (existing != null)
        {
            existing.PreferenceValue = value;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var preference = new UserPreference
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                PreferenceKey = key,
                PreferenceValue = value,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _context.UserPreferences.AddAsync(preference, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
