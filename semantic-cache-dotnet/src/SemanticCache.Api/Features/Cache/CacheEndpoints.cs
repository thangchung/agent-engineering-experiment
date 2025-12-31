using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SemanticCache.Api.Infrastructure.Database;
using SemanticCache.Api.Infrastructure.Services;

namespace SemanticCache.Api.Features.Cache;

public static class CacheEndpoints
{
    public record CacheEntryDto(
        string Id,
        string Query,
        string Response,
        int HitCount,
        DateTime CreatedAt,
        DateTime LastAccessedAt);

    public static IEndpointRouteBuilder MapCacheEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cache").WithTags("Cache");

        group.MapGet("/", async (
            SemanticCacheService cacheService,
            CancellationToken cancellationToken) =>
        {
            var entries = await cacheService.GetAllCacheEntriesAsync(cancellationToken);
            var dtos = entries.Select(e => new CacheEntryDto(
                e.Id,
                e.Query,
                e.Response,
                e.HitCount,
                e.CreatedAt,
                e.LastAccessedAt));

            return Results.Ok(dtos);
        })
        .WithName("GetAllCacheEntries")
        .WithSummary("Get all cache entries with statistics")
        .WithOpenApi();

        group.MapDelete("/", async (
            SemanticCacheService cacheService,
            CancellationToken cancellationToken) =>
        {
            await cacheService.ClearCacheAsync(cancellationToken);
            return Results.Ok(new { message = "Cache cleared successfully" });
        })
        .WithName("ClearCache")
        .WithSummary("Clear all cache entries")
        .WithOpenApi();

        return app;
    }
}
