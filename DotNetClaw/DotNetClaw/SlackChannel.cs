using SlackNet;
using SlackNet.AspNetCore;
using SlackNet.Events;
using SlackNet.WebApi;

namespace DotNetClaw;

/// <summary>
/// The Slack channel — equivalent to OpenClaw's src/slack adapter.
///
/// OpenClaw Slack patterns mirrored here:
///   1. Two event types: DMs (MessageEvent on im channel) + @mentions (AppMention)
///   2. Session ID: "slack:dm:{userId}" for DMs, "slack:{channelId}:{userId}" for channels
///   3. Threading: always reply in-thread (keeps channel tidy, mirrors OpenClaw's replyToMode:"all")
///   4. Filter: ignore bot's own messages to prevent loops
///   5. Access policy: configurable — "open" (everyone) or "allowlist" (specific users only)
///
/// Slack App setup (Socket Mode — no public URL required):
///   1. Slack API → Your App → Socket Mode → Enable Socket Mode
///   2. Generate an App-Level Token (xapp-...) with the connections:write scope
///   3. Subscribe to bot events: message.im, app_mention  ← Event Subscriptions → Subscribe to Bot Events
///   4. OAuth Scopes: chat:write, im:read, im:history, app_mentions:read, channels:history (optional)
///   5. Set secret: dotnet user-secrets set "Slack:AppToken" "xapp-..."
/// </summary>
public sealed class SlackMessageHandler(
    ClawRuntime runtime,
    ISlackApiClient slack,
    IConfiguration config,
    ILogger<SlackMessageHandler> logger)
    : IEventHandler<MessageEvent>, IEventHandler<AppMention>
{
    // Slack Bot User ID — auto-resolved at startup, used to filter the bot's own messages
    // Set in appsettings.json or via dotnet user-secrets set "Slack:BotUserId" "UXXXXX"
    private readonly string _botUserId = config["Slack:BotUserId"] ?? "";
    private readonly string _policy    = config["Slack:Policy"] ?? "open";  // "open" or "allowlist"
    private readonly HashSet<string> _allowlist = new(
        (config["Slack:AllowedUserIds"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));

    // ── Direct messages ───────────────────────────────────────────────────────────
    // Fires when someone sends a DM to your bot.
    public async Task Handle(MessageEvent e)
    {
        // Only handle DMs (channel_type == "im")
        if (e.ChannelType != "im") return;

        // Ignore bot's own messages (prevent infinite loops)
        if (!string.IsNullOrEmpty(e.BotId) || e.User == _botUserId) return;
        if (string.IsNullOrWhiteSpace(e.Text)) return;

        if (!IsAllowed(e.User))
        {
            logger.LogInformation("[Slack] DM from {User} blocked by policy", e.User);
            return;
        }

        var sessionId = $"slack:dm:{e.User}";
        logger.LogInformation("[Slack] DM from {User}: {Preview}", e.User, e.Text[..Math.Min(80, e.Text.Length)]);

        var reply = await runtime.HandleAsync(sessionId, e.Text, default);
        if (string.IsNullOrWhiteSpace(reply)) return;

        // Reply in-thread (mirrors OpenClaw's replyToMode:"all")
        await slack.Chat.PostMessage(new Message
        {
            Channel = e.Channel,
            Text    = TruncateSlack(reply),
            ThreadTs = e.Ts   // ties reply to the specific message thread
        });
    }

    // ── Channel @mentions ─────────────────────────────────────────────────────────
    // Fires when someone @mentions your bot in a channel.
    public async Task Handle(AppMention e)
    {
        logger.LogInformation("[Slack] AppMention event: User={User}, Channel={Channel}, Text={Text}", e.User, e.Channel, e.Text);

        if (string.IsNullOrWhiteSpace(e.Text)) return;
        if (!IsAllowed(e.User))
        {
            logger.LogInformation("[Slack] Mention from {User} in {Channel} blocked by policy", e.User, e.Channel);
            return;
        }

        // Strip the leading <@BOTID> mention so the agent gets clean text
        var text = StripMention(e.Text);
        if (string.IsNullOrWhiteSpace(text)) return;

        // Per-user per-channel session (mirrors OpenClaw: "group:{channel}:{userId}")
        var sessionId = $"slack:{e.Channel}:{e.User}";
        logger.LogInformation("[Slack] Mention in {Channel} from {User}: {Preview}",
            e.Channel, e.User, text[..Math.Min(80, text.Length)]);

        var reply = await runtime.HandleAsync(sessionId, text, default);
        if (string.IsNullOrWhiteSpace(reply)) return;

        // Always reply in-thread to keep the channel tidy
        var replyThread = e.ThreadTs ?? e.Ts;
        await slack.Chat.PostMessage(new Message
        {
            Channel  = e.Channel,
            Text     = TruncateSlack(reply),
            ThreadTs = replyThread
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    // OpenClaw policy: "open" = everyone, "allowlist" = specific user IDs only
    private bool IsAllowed(string userId)
    {
        if (_policy == "open") return true;
        return _allowlist.Contains(userId);
    }

    // Remove <@BOTID> or <@BOTID|username> from start of text
    private static string StripMention(string text)
    {
        var end = text.IndexOf('>');
        return end >= 0 ? text[(end + 1)..].Trim() : text.Trim();
    }

    // Slack's message limit is 4000 chars per block; truncate gracefully
    private static string TruncateSlack(string text) =>
        text.Length <= 4000 ? text : text[..3990] + "\n[...]";
}

/// <summary>Extension to keep Program.cs clean.</summary>
public static class SlackChannelExtensions
{
    public static IServiceCollection AddSlackChannel(this IServiceCollection services, IConfiguration config)
    {
        var botToken  = config["Slack:BotToken"]  ?? throw new InvalidOperationException("Slack:BotToken is not configured.");
        var appToken  = config["Slack:AppToken"]  ?? throw new InvalidOperationException("Slack:AppToken (xapp-...) is not configured. Run: dotnet user-secrets set \"Slack:AppToken\" \"xapp-...\"");

        services.AddSlackNet(c => c
            .UseApiToken(botToken)       // bot token (xoxb-...) for API calls
            .UseAppLevelToken(appToken)  // app-level token (xapp-...) for socket mode connection
            // Register handlers for both event types
            .RegisterEventHandler<MessageEvent, SlackMessageHandler>()
            .RegisterEventHandler<AppMention,   SlackMessageHandler>());

        return services;
    }

    public static WebApplication MapSlack(this WebApplication app, IConfiguration config)
    {
        // Socket mode: SlackNet connects outbound to Slack's WebSocket API —
        // no public URL or ngrok needed. The SocketModeService (IHostedService)
        // manages the connection automatically when UseSocketMode() is set.
        // NumberOfConnections=1: default is 2, but with 2 connections each event
        // fires the handler twice → double responses. One connection is enough
        // for a personal bot.
        app.UseSlackNet(c => c.UseSocketMode(useSocketMode: true,
            new SlackNet.SocketMode.SocketModeConnectionOptions { NumberOfConnections = 1 }));

        return app;
    }
}
