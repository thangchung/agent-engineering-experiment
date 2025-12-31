namespace SemanticCache.Api.Infrastructure.Database.Entities;

public class MetricsLog
{
    public required string Id { get; set; }
    public required string Endpoint { get; set; }
    public required string Method { get; set; }
    public int StatusCode { get; set; }
    public long ResponseTimeMs { get; set; }
    public bool CacheHit { get; set; }
    public string? UserId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
