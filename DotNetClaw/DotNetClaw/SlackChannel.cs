using SlackNet;
using SlackNet.AspNetCore;
using SlackNet.Events;
using SlackNet.WebApi;
using System.Diagnostics;

namespace DotNetClaw;

public sealed class SlackMessageHandler(
    ClawRuntime runtime,
    ISlackApiClient slack,
    IConfiguration config,
    ILogger<SlackMessageHandler> logger)
    : IEventHandler<MessageEvent>, IEventHandler<AppMention>
{
    private readonly string _botUserId = config["Slack:BotUserId"] ?? "";
    private readonly string _policy    = config["Slack:Policy"] ?? "open";
    private readonly HashSet<string> _allowlist = new(
        (config["Slack:AllowedUserIds"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));

    public async Task Handle(MessageEvent e)
    {
        using var activity = ClawTelemetry.ActivitySource.StartActivity("slack.dm", ActivityKind.Consumer);
        activity?.SetTag("channel.type", "slack");
        activity?.SetTag("event.type", "dm");
        activity?.SetTag("slack.user", e.User);
        activity?.SetTag("slack.channel", e.Channel);

        if (e.ChannelType != "im") return;
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

        await slack.Chat.PostMessage(new Message
        {
            Channel = e.Channel,
            Text    = TruncateSlack(reply),
            ThreadTs = e.Ts
        });

        activity?.SetStatus(ActivityStatusCode.Ok);
        activity?.SetTag("response.length", reply.Length);
    }

    public async Task Handle(AppMention e)
    {
        using var activity = ClawTelemetry.ActivitySource.StartActivity("slack.mention", ActivityKind.Consumer);
        activity?.SetTag("channel.type", "slack");
        activity?.SetTag("event.type", "mention");
        activity?.SetTag("slack.user", e.User);
        activity?.SetTag("slack.channel", e.Channel);

        logger.LogInformation("[Slack] AppMention: User={User}, Channel={Channel}", e.User, e.Channel);

        if (string.IsNullOrWhiteSpace(e.Text)) return;
        if (!IsAllowed(e.User))
        {
            logger.LogInformation("[Slack] Mention from {User} in {Channel} blocked by policy", e.User, e.Channel);
            return;
        }

        var text = StripMention(e.Text);
        if (string.IsNullOrWhiteSpace(text)) return;

        var sessionId = $"slack:{e.Channel}:{e.User}";
        logger.LogInformation("[Slack] Mention in {Channel} from {User}: {Preview}",
            e.Channel, e.User, text[..Math.Min(80, text.Length)]);

        var reply = await runtime.HandleAsync(sessionId, text, default);
        if (string.IsNullOrWhiteSpace(reply)) return;

        var replyThread = e.ThreadTs ?? e.Ts;
        await slack.Chat.PostMessage(new Message
        {
            Channel  = e.Channel,
            Text     = TruncateSlack(reply),
            ThreadTs = replyThread
        });

        activity?.SetStatus(ActivityStatusCode.Ok);
        activity?.SetTag("response.length", reply.Length);
    }

    private bool IsAllowed(string userId)
    {
        if (_policy == "open") return true;
        return _allowlist.Contains(userId);
    }

    private static string StripMention(string text)
    {
        var end = text.IndexOf('>');
        return end >= 0 ? text[(end + 1)..].Trim() : text.Trim();
    }

    // Slack block limit is 4000 chars
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
            .UseApiToken(botToken)
            .UseAppLevelToken(appToken)
            .RegisterEventHandler<MessageEvent, SlackMessageHandler>()
            .RegisterEventHandler<AppMention,   SlackMessageHandler>());

        return services;
    }

    public static WebApplication MapSlack(this WebApplication app, IConfiguration config)
    {
        // NumberOfConnections=1: default 2 causes each event to fire twice → double responses.
        app.UseSlackNet(c => c.UseSocketMode(useSocketMode: true,
            new SlackNet.SocketMode.SocketModeConnectionOptions { NumberOfConnections = 1 }));

        return app;
    }
}
