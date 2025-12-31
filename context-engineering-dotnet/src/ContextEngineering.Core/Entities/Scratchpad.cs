namespace ContextEngineering.Core.Entities;

/// <summary>
/// Represents a scratchpad entry for storing user preferences and completed tasks.
/// </summary>
public class Scratchpad
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public required string UserId { get; set; }
    
    /// <summary>
    /// Category of the scratchpad entry: "preferences" or "tasks"
    /// </summary>
    public required string Category { get; set; }
    
    public string Content { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Scratchpad categories for organizing user data.
/// </summary>
public static class ScratchpadCategory
{
    public const string Preferences = "preferences";
    public const string Tasks = "tasks";
}
