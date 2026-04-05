namespace DotNetClaw;
using System.Diagnostics;

/// <summary>
/// Web channel — minimal HTTP API for a browser-based chat window.
///
/// Session ID convention: "web:{guid}" where guid comes from a cookie.
/// Follows the same pattern as SlackChannel: receive → runtime.HandleAsync → reply.
/// </summary>
public static class WebChannelExtensions
{
    private const string SessionCookie = "X-Chat-Session";

    public static WebApplication MapWebChannel(this WebApplication app)
    {
        app.UseStaticFiles();

        // Serve index.html at root (override the JSON health endpoint when web channel is enabled)
        app.MapGet("/", (HttpContext ctx) =>
        {
            var filePath = Path.Combine(app.Environment.WebRootPath, "index.html");
            return Results.File(filePath, "text/html");
        });

        app.MapPost("/api/chat", async (HttpContext ctx, ClawRuntime runtime, ILogger<ClawRuntime> logger) =>
        {
            var sessionId = GetOrCreateSessionId(ctx);
            var activity = Activity.Current;
            activity?.SetTag("channel.type", "web");
            activity?.SetTag("session.id", sessionId);

            using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted);
            var message = doc.RootElement.GetProperty("message").GetString();

            if (string.IsNullOrWhiteSpace(message))
                return Results.BadRequest(new { error = "message is required" });

            activity?.SetTag("message.length", message.Length);

            logger.LogInformation("[Web] Chat from session {Session}: {Preview}",
                sessionId, message[..Math.Min(80, message.Length)]);

            var reply = await runtime.HandleAsync(sessionId, message, ctx.RequestAborted);
            activity?.SetTag("response.length", reply.Length);
            return Results.Ok(new { reply });
        });

        app.MapPost("/api/chat/reset", (HttpContext ctx) =>
        {
            Activity.Current?.SetTag("channel.type", "web");
            // Clear the session cookie so next request gets a fresh session
            ctx.Response.Cookies.Delete(SessionCookie);
            return Results.NoContent();
        });

        return app;
    }

    private static string GetOrCreateSessionId(HttpContext ctx)
    {
        if (ctx.Request.Cookies.TryGetValue(SessionCookie, out var existing) && !string.IsNullOrEmpty(existing))
            return $"web:{existing}";

        var id = Guid.NewGuid().ToString("N")[..12];
        ctx.Response.Cookies.Append(SessionCookie, id, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            MaxAge   = TimeSpan.FromDays(7),
        });
        return $"web:{id}";
    }
}
