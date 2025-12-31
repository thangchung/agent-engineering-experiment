namespace SemanticCache.Api.Infrastructure.Database.Entities;

public class UserPreference
{
    public required string Id { get; set; }
    public required string UserId { get; set; }
    public required string PreferenceKey { get; set; }
    public required string PreferenceValue { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation property
    public UserProfile User { get; set; } = null!;
}
