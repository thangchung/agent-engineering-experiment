namespace SemanticCache.Api.Infrastructure.Database.Entities;

public class UserProfile
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public ICollection<UserPreference> Preferences { get; set; } = [];
    public ICollection<ConversationEntry> ConversationEntries { get; set; } = [];
    public ICollection<ContextMemory> ContextMemories { get; set; } = [];
}
