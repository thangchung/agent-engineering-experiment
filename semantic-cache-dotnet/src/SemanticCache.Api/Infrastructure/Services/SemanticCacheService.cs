using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using SemanticCache.Api.Infrastructure.Database;
using SemanticCache.Api.Infrastructure.Database.Entities;

namespace SemanticCache.Api.Infrastructure.Services;

public class SemanticCacheService(
    AppDbContext context,
    HybridCache cache,
    VectorSimilarityService vectorService)
{
    private readonly AppDbContext _context = context;
    private readonly HybridCache _cache = cache;
    private readonly VectorSimilarityService _vectorService = vectorService;
    private const float DefaultSimilarityThreshold = 0.85f;

    /// <summary>
    /// Attempts to find a cached response for the given query using semantic similarity.
    /// Returns the cached response if found with similarity above threshold, otherwise null.
    /// </summary>
    public async Task<(string? Response, float Similarity, bool IsHit)?> GetSemanticCacheAsync(
        float[] queryEmbedding,
        float similarityThreshold = DefaultSimilarityThreshold,
        CancellationToken cancellationToken = default)
    {
        // Try to get from HybridCache first (L1 memory + L2 Redis)
        var cacheKey = $"semantic_cache_all";
        var allEntries = await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await _context.CacheEntries.ToListAsync(cancel),
            cancellationToken: cancellationToken);

        if (allEntries == null || allEntries.Count == 0)
        {
            return null;
        }

        // Find most similar cached entry using in-memory cosine similarity
        var result = _vectorService.FindMostSimilar(
            queryEmbedding,
            allEntries.Select(e => e.Embedding),
            similarityThreshold);

        if (result == null)
        {
            return null;
        }

        var matchedEntry = allEntries[result.Value.Index];
        
        // Update hit count and last accessed time
        matchedEntry.HitCount++;
        matchedEntry.LastAccessedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        
        // Invalidate cache to reflect updated stats
        await _cache.RemoveAsync(cacheKey, cancellationToken);

        return (matchedEntry.Response, result.Value.Similarity, true);
    }

    /// <summary>
    /// Stores a new query-response pair with its embedding in the semantic cache.
    /// </summary>
    public async Task SetSemanticCacheAsync(
        string query,
        string response,
        float[] embedding,
        string? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var cacheEntry = new CacheEntry
        {
            Id = Guid.NewGuid().ToString(),
            Query = query,
            Response = response,
            Embedding = embedding,
            Metadata = metadata,
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        };

        await _context.CacheEntries.AddAsync(cacheEntry, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        
        // Invalidate cache to include new entry
        await _cache.RemoveAsync("semantic_cache_all", cancellationToken);
    }

    /// <summary>
    /// Retrieves all cache entries with statistics.
    /// </summary>
    public async Task<List<CacheEntry>> GetAllCacheEntriesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.CacheEntries
            .OrderByDescending(e => e.HitCount)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Clears all cache entries from the database and HybridCache.
    /// </summary>
    public async Task ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        _context.CacheEntries.RemoveRange(_context.CacheEntries);
        await _context.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync("semantic_cache_all", cancellationToken);
    }
}
