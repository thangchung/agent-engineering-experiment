namespace ContextEngineering.Core.Entities;

/// <summary>
/// Tracks token usage for monitoring and reporting purposes.
/// </summary>
public class TokenUsage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid ConversationThreadId { get; set; }
    
    public ConversationThread? ConversationThread { get; set; }
    
    public int InputTokens { get; set; }
    
    public int OutputTokens { get; set; }
    
    public int TotalTokens => InputTokens + OutputTokens;
    
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Indicates if this usage was recorded after a reduction event.
    /// </summary>
    public bool AfterReduction { get; set; }
}
