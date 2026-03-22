using GitHub.Copilot.SDK;
using System.Diagnostics;

namespace TestWeb.Services;

public sealed class CopilotChatService : IAsyncDisposable
{
    private const string PromptModeSystem = """
You are an MCP tool orchestration assistant for OpenBreweryDB.

Primary behavior:
- Prefer MCP tool usage over freeform guessing.
- Keep answers concise, factual, and directly tied to tool results.
- If a tool call fails, explain briefly and suggest a valid next attempt.

Interpret natural user intents and map them to these workflows:
1) Tool discovery
- For requests like "what tools are available", list all available tools with one-line descriptions.

2) Focused tool search
- For requests like "find brewery search tools" or "tools for city search", return only matching tool names.

3) Schema lookup (compact)
- For requests asking for a concise parameter schema, use compact schema output for the requested tool.

4) Schema lookup (full)
- For requests asking for full schema details, return full JSON schema for requested tools.

5) Direct tool invocation
- If user says "get random brewery" (or equivalent), call brewery_random and return only: name, city, state, country.

6) Multi-step tool chain
- If user says "find breweries in San Diego", run a search/list step, pick one brewery id, then fetch full details.
- Return: chosen id plus the final full details object.

7) Code mode single-call
- If user asks "how many brewery are there" (or total count), run code mode with brewery_meta and return only total count.

8) Code mode multi-call + transform
- If user asks for transformed top results (for example "moon" query with top 5 name/city), run code mode and return only transformed array.

9) Error handling
- If user asks for a non-existent tool, do not hallucinate.
- Report the missing tool error briefly and suggest closest valid tool names.

10) Safety/verbosity check
- When user asks for a short summary and successful calls, provide a short summary and list only successful tool calls.

Output rules:
- Do not expose chain-of-thought.
- Do not include unrelated commentary.
- Prefer bullet lists for tool names and short key-value outputs for direct responses.
""";

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string model;
    private readonly string mcpEndpoint;
    private readonly string mcpServerName;
    private static readonly ActivitySource ActivitySource = new("TestWeb.CopilotChatService");

    private CopilotClient? client;
    private CopilotSession? session;
    private string? sessionTraceParent;

    public CopilotChatService(IConfiguration configuration)
    {
        model = configuration["Copilot:Model"] ?? "gpt-5";
        mcpEndpoint = configuration["Mcp:Endpoint"] ?? "http://localhost:5100/mcp";
        mcpServerName = configuration["Copilot:McpServerName"] ?? "mcp-experiments";
    }

    public async Task<ChatTurnMetrics> SendPromptAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Prompt cannot be empty.", nameof(prompt));
        }

        using Activity? activity = ActivitySource.StartActivity("copilot.chat.turn", ActivityKind.Client);
        string? traceParent = activity?.Id;
        string? traceState = activity?.TraceStateString;

        await EnsureSessionAsync(traceParent, traceState, cancellationToken);

        int promptTokens = CountTokens(prompt);
        Stopwatch stopwatch = Stopwatch.StartNew();

        var response = await session!.SendAndWaitAsync(new MessageOptions
        {
            Prompt = prompt,
        }, TimeSpan.FromSeconds(90));

        stopwatch.Stop();

        string content = response?.Data?.Content ?? "(no response)";
        int completionTokens = CountTokens(content);

        return new ChatTurnMetrics(
            content,
            promptTokens,
            completionTokens,
            promptTokens + completionTokens,
            stopwatch.ElapsedMilliseconds);
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (session is not null)
            {
                await session.DisposeAsync();
                session = null;
            }

            if (client is not null)
            {
                await client.StopAsync();
                await client.DisposeAsync();
                client = null;
            }

            sessionTraceParent = null;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task EnsureSessionAsync(string? traceParent, string? traceState, CancellationToken cancellationToken)
    {
        if (session is not null && string.Equals(sessionTraceParent, traceParent, StringComparison.Ordinal))
        {
            return;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (session is not null && string.Equals(sessionTraceParent, traceParent, StringComparison.Ordinal))
            {
                return;
            }

            if (session is not null)
            {
                await session.DisposeAsync();
                session = null;
                sessionTraceParent = null;
            }

            CopilotClientOptions options = new();

            string? configuredCliPath = Environment.GetEnvironmentVariable("COPILOT_CLI_PATH");
            if (!string.IsNullOrWhiteSpace(configuredCliPath))
            {
                options.CliPath = configuredCliPath;
            }

            string? configuredCliUrl = Environment.GetEnvironmentVariable("COPILOT_CLI_URL");
            if (!string.IsNullOrWhiteSpace(configuredCliUrl))
            {
                options.CliUrl = configuredCliUrl;
            }

            string? configuredToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (!string.IsNullOrWhiteSpace(configuredToken))
            {
                options.GitHubToken = configuredToken;
            }

            client = new CopilotClient(options);
            await client.StartAsync(cancellationToken);

            SessionConfig config = new()
            {
                Model = model,
                OnPermissionRequest = PermissionHandler.ApproveAll,
                SystemMessage = new SystemMessageConfig
                {
                    Mode = SystemMessageMode.Append,
                    Content = PromptModeSystem,
                },
                McpServers = BuildMcpServerConfig(traceParent, traceState),
            };

            session = await client.CreateSessionAsync(config, cancellationToken);
            sessionTraceParent = traceParent;
        }
        finally
        {
            gate.Release();
        }
    }

    private Dictionary<string, object> BuildMcpServerConfig(string? traceParent, string? traceState)
    {
        Dictionary<string, object> serverConfig = new()
        {
            ["type"] = "http",
            ["url"] = mcpEndpoint,
            ["tools"] = new[] { "*" },
        };

        if (!string.IsNullOrWhiteSpace(traceParent))
        {
            Dictionary<string, string> headers = new()
            {
                ["traceparent"] = traceParent,
            };

            if (!string.IsNullOrWhiteSpace(traceState))
            {
                headers["tracestate"] = traceState;
            }

            serverConfig["headers"] = headers;
        }

        return new Dictionary<string, object>
        {
            [mcpServerName] = serverConfig,
        };
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync();
        gate.Dispose();
    }

    private int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        // Use a fast local estimate to avoid blocking page render on tokenizer initialization.
        return Math.Max(1, (int)Math.Ceiling(text.Length / 4d));
    }

    public sealed record ChatTurnMetrics(
        string Content,
        int PromptTokens,
        int CompletionTokens,
        int TotalTokens,
        long ElapsedMilliseconds);
}
