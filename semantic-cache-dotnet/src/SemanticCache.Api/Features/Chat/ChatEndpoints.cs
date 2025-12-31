using Microsoft.AspNetCore.Mvc;
using SemanticCache.Api.Infrastructure.Services;

namespace SemanticCache.Api.Features.Chat;

public static class ChatEndpoints
{
    public record RawChatRequest(string Query);
    public record RawChatResponse(string Response, long ResponseTimeMs);

    public record CachedChatRequest(string Query, float SimilarityThreshold = 0.85f);
    public record CachedChatResponse(string Response, bool FromCache, float Similarity, long ResponseTimeMs);

    public record PersonalizedChatRequest(
        string UserId,
        string UserName,
        string Query,
        string? ThreadId = null,
        float SimilarityThreshold = 0.85f);
    public record PersonalizedChatResponse(string Response, bool FromCache, float Similarity, string ThreadId, long ResponseTimeMs);

    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/chat").WithTags("Chat");

        group.MapPost("/raw", async (
            [FromBody] RawChatRequest request,
            ChatService chatService,
            CancellationToken cancellationToken) =>
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response = await chatService.GetRawResponseAsync(request.Query, cancellationToken);
            stopwatch.Stop();

            return Results.Ok(new RawChatResponse(response, stopwatch.ElapsedMilliseconds));
        })
        .WithName("RawChat")
        .WithSummary("Get raw chat response without caching")
        .WithOpenApi();

        group.MapPost("/cached", async (
            [FromBody] CachedChatRequest request,
            ChatService chatService,
            CancellationToken cancellationToken) =>
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var (response, fromCache, similarity) = await chatService.GetCachedResponseAsync(
                request.Query,
                request.SimilarityThreshold,
                cancellationToken);
            stopwatch.Stop();

            return Results.Ok(new CachedChatResponse(response, fromCache, similarity, stopwatch.ElapsedMilliseconds));
        })
        .WithName("CachedChat")
        .WithSummary("Get chat response with semantic caching")
        .WithOpenApi();

        group.MapPost("/personalized", async (
            [FromBody] PersonalizedChatRequest request,
            ChatService chatService,
            CancellationToken cancellationToken) =>
        {
            var threadId = request.ThreadId ?? Guid.NewGuid().ToString();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            var (response, fromCache, similarity) = await chatService.GetPersonalizedResponseAsync(
                request.UserId,
                request.UserName,
                request.Query,
                threadId,
                request.SimilarityThreshold,
                cancellationToken);
            
            stopwatch.Stop();

            return Results.Ok(new PersonalizedChatResponse(response, fromCache, similarity, threadId, stopwatch.ElapsedMilliseconds));
        })
        .WithName("PersonalizedChat")
        .WithSummary("Get personalized chat response with user context and semantic caching")
        .WithOpenApi();

        return app;
    }
}
