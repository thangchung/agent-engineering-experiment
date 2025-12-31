namespace SemanticCache.Api.Infrastructure.Database.Entities;

public class ConversationEntry
{
    public required string Id { get; set; }
    public required string UserId { get; set; }
    public required string ThreadId { get; set; }
    public required string Role { get; set; } // "user" or "assistant"
    public required string Content { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    // Navigation property
    public UserProfile User { get; set; } = null!;
}
