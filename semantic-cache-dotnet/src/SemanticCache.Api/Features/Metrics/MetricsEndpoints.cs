using Microsoft.EntityFrameworkCore;
using SemanticCache.Api.Infrastructure.Database;

namespace SemanticCache.Api.Features.Metrics;

public static class MetricsEndpoints
{
    public record MetricsDto(
        string Id,
        string Endpoint,
        string Method,
        int StatusCode,
        long ResponseTimeMs,
        bool CacheHit,
        string? UserId,
        DateTime Timestamp);

    public record MetricsStatsDto(
        int TotalRequests,
        int CacheHits,
        int CacheMisses,
        double CacheHitRate,
        long AverageResponseTimeMs);

    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/metrics").WithTags("Metrics");

        group.MapGet("/", async (
            AppDbContext context,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            var query = context.MetricsLogs
                .OrderByDescending(m => m.Timestamp)
                .AsQueryable();

            if (limit.HasValue)
            {
                query = query.Take(limit.Value);
            }

            var metrics = await query
                .Select(m => new MetricsDto(
                    m.Id,
                    m.Endpoint,
                    m.Method,
                    m.StatusCode,
                    m.ResponseTimeMs,
                    m.CacheHit,
                    m.UserId,
                    m.Timestamp))
                .ToListAsync(cancellationToken);

            return Results.Ok(metrics);
        })
        .WithName("GetMetrics")
        .WithSummary("Get metrics logs")
        .WithOpenApi();

        group.MapGet("/stats", async (
            AppDbContext context,
            CancellationToken cancellationToken) =>
        {
            var allMetrics = await context.MetricsLogs.ToListAsync(cancellationToken);
            var totalRequests = allMetrics.Count;
            var cacheHits = allMetrics.Count(m => m.CacheHit);
            var cacheMisses = totalRequests - cacheHits;
            var cacheHitRate = totalRequests > 0 ? (double)cacheHits / totalRequests : 0;
            var averageResponseTime = allMetrics.Any() ? (long)allMetrics.Average(m => m.ResponseTimeMs) : 0;

            var stats = new MetricsStatsDto(
                totalRequests,
                cacheHits,
                cacheMisses,
                cacheHitRate,
                averageResponseTime);

            return Results.Ok(stats);
        })
        .WithName("GetMetricsStats")
        .WithSummary("Get aggregated metrics statistics")
        .WithOpenApi();

        return app;
    }
}
