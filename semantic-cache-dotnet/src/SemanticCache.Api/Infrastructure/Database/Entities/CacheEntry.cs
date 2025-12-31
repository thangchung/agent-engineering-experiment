namespace SemanticCache.Api.Infrastructure.Database.Entities;

public class CacheEntry
{
    public required string Id { get; set; }
    public required string Query { get; set; }
    public required string Response { get; set; }
    public required float[] Embedding { get; set; } // Vector embedding for semantic search
    public string? Metadata { get; set; }
    public int HitCount { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
}
