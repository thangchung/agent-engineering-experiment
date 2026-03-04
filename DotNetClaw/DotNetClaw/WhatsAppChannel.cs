using Twilio.AspNet.Core;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace DotNetClaw;

/// <summary>
/// The WhatsApp channel — equivalent to OpenClaw's Telegram/Discord channel adapters.
///
/// Responsibilities (same as any OpenClaw channel adapter):
///   1. Receive inbound message from Twilio webhook (form-encoded POST)
///   2. Normalize to a plain string + session ID (phone number)
///   3. Send to ClawRuntime for agent processing
///   4. Deliver reply back to the user via Twilio REST API
///
/// Twilio WhatsApp sandbox setup:
///   Twilio Console → Messaging → Try it out → Send a WhatsApp message
///   Webhook URL: https://<your-ngrok>.ngrok.io/whatsapp/webhook
///   HTTP Method: POST
/// </summary>
public static class WhatsAppChannel
{
    internal static async Task<IResult> HandleWebhookAsync(
        HttpContext context,
        ClawRuntime runtime,
        IConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        var form = await context.Request.ReadFormAsync(ct);

        // Twilio sends: From=whatsapp:+1234567890, To=whatsapp:+14155238886, Body=<text>
        var from = form["From"].ToString();
        var body = form["Body"].ToString();

        if (string.IsNullOrWhiteSpace(body))
            return Results.Ok(); // ignore empty messages (delivery receipts, etc.)

        logger.LogInformation("[WhatsApp] {From} → {Body}", from, body);

        // Run the agent. Phone number = session ID → each user has their own conversation.
        var reply = await runtime.HandleAsync(sessionId: from, message: body, ct: ct);

        if (string.IsNullOrWhiteSpace(reply))
            return Results.Ok();

        // WhatsApp has a 4096-character limit per message. Truncate if needed.
        if (reply.Length > 4096)
            reply = reply[..4090] + " [...]";

        var fromNumber = config["Twilio:From"]
            ?? throw new InvalidOperationException("Twilio:From is not configured");

        await MessageResource.CreateAsync(
            body: reply,
            from: new PhoneNumber(fromNumber),
            to: new PhoneNumber(from));

        logger.LogInformation("[WhatsApp] Reply sent to {From} ({Length} chars)", from, reply.Length);

        return Results.Ok();
    }
}

/// <summary>Extension for clean registration in Program.cs.</summary>
public static class WhatsAppChannelExtensions
{
    public static IEndpointRouteBuilder MapWhatsApp(this IEndpointRouteBuilder app)
    {
        // ValidateTwilioRequestFilter ensures requests are genuinely from Twilio (HMAC check).
        // In Twilio.AspNet.Core v8.x, use the endpoint filter instead of the old [ValidateTwilioRequest] attribute.
        app.MapPost("/whatsapp/webhook",
            (HttpContext ctx, ClawRuntime rt, IConfiguration cfg, ILoggerFactory lf, CancellationToken ct) =>
                WhatsAppChannel.HandleWebhookAsync(ctx, rt, cfg, lf.CreateLogger("WhatsApp"), ct))
            .AddEndpointFilter<ValidateTwilioRequestFilter>();

        return app;
    }
}
