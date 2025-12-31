namespace ContextEngineering.Core.Entities;

/// <summary>
/// Represents a conversation thread tracking token usage and reduction events.
/// </summary>
public class ConversationThread
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public required string UserId { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public int MessageCount { get; set; }
    
    public int TotalTokensUsed { get; set; }
    
    public int ReductionEventCount { get; set; }
    
    public DateTime? LastReductionAt { get; set; }
    
    public ICollection<ChatMessage> Messages { get; set; } = [];
}

/// <summary>
/// Represents a chat message within a conversation thread.
/// </summary>
public class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid ConversationThreadId { get; set; }
    
    public ConversationThread? ConversationThread { get; set; }
    
    /// <summary>
    /// Role of the message sender: "user", "assistant", or "system"
    /// </summary>
    public required string Role { get; set; }
    
    public required string Content { get; set; }
    
    public int TokenCount { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Indicates if this message is a summary of previous messages.
    /// </summary>
    public bool IsSummary { get; set; }
}
