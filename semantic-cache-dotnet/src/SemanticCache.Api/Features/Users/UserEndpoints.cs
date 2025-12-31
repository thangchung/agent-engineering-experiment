using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SemanticCache.Api.Infrastructure.Database;
using SemanticCache.Api.Infrastructure.Services;

namespace SemanticCache.Api.Features.Users;

public static class UserEndpoints
{
    public record UserDto(string Id, string Name, DateTime CreatedAt, DateTime UpdatedAt);
    public record PreferenceDto(string Key, string Value);
    public record SetPreferenceRequest(string Key, string Value);

    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users");

        group.MapGet("/", async (
            AppDbContext context,
            CancellationToken cancellationToken) =>
        {
            var users = await context.UserProfiles
                .Select(u => new UserDto(u.Id, u.Name, u.CreatedAt, u.UpdatedAt))
                .ToListAsync(cancellationToken);

            return Results.Ok(users);
        })
        .WithName("GetAllUsers")
        .WithSummary("Get all users")
        .WithOpenApi();

        group.MapGet("/{userId}", async (
            string userId,
            AppDbContext context,
            CancellationToken cancellationToken) =>
        {
            var user = await context.UserProfiles
                .Where(u => u.Id == userId)
                .Select(u => new UserDto(u.Id, u.Name, u.CreatedAt, u.UpdatedAt))
                .FirstOrDefaultAsync(cancellationToken);

            return user != null ? Results.Ok(user) : Results.NotFound();
        })
        .WithName("GetUser")
        .WithSummary("Get user by ID")
        .WithOpenApi();

        group.MapGet("/{userId}/preferences", async (
            string userId,
            AgentMemoryService memoryService,
            CancellationToken cancellationToken) =>
        {
            var preferences = await memoryService.GetUserPreferencesAsync(userId, cancellationToken);
            var dtos = preferences.Select(p => new PreferenceDto(p.Key, p.Value));
            return Results.Ok(dtos);
        })
        .WithName("GetUserPreferences")
        .WithSummary("Get user preferences")
        .WithOpenApi();

        group.MapPost("/{userId}/preferences", async (
            string userId,
            [FromBody] SetPreferenceRequest request,
            AgentMemoryService memoryService,
            CancellationToken cancellationToken) =>
        {
            await memoryService.SetUserPreferenceAsync(userId, request.Key, request.Value, cancellationToken);
            return Results.Ok(new { message = "Preference set successfully" });
        })
        .WithName("SetUserPreference")
        .WithSummary("Set user preference")
        .WithOpenApi();

        group.MapGet("/{userId}/conversations/{threadId}", async (
            string userId,
            string threadId,
            AgentMemoryService memoryService,
            CancellationToken cancellationToken) =>
        {
            var history = await memoryService.GetConversationHistoryAsync(userId, threadId, cancellationToken: cancellationToken);
            return Results.Ok(history);
        })
        .WithName("GetConversationHistory")
        .WithSummary("Get conversation history for a thread")
        .WithOpenApi();

        return app;
    }
}
