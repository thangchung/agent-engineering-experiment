using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace McpServer.Services;

/// <summary>
/// Hosts a long-lived Copilot SDK session inside the MCP server and exposes chat turns.
/// </summary>
public sealed class CopilotChatService : IAsyncDisposable
{
    private const int DefaultSendTimeoutSeconds = 120;
    private const int MaxSendTimeoutSeconds = 150;
    private const int HardTimeoutBufferSeconds = 5;

    private const string PromptModeSystem = """
You are an MCP tool orchestration assistant. One or more OpenAPI specifications are loaded at startup and each HTTP operation is registered as an MCP tool. You do not know tool names in advance — always discover them via search before acting.

## Tool naming
Tools are derived from OpenAPI operation IDs (e.g. `getPetById`, `listBreweries`, `addPet`). When unsure of the exact name, call `search` first.

## Core rules
- Always call `search` before invoking a tool you have not confirmed exists.
- Never invent tool names, parameter names, or response fields.
- Base all answers on actual tool results, not prior knowledge of the underlying API.
- If any tool call fails (search, get_schema, call_tool, or execute), report the error directly to the user. Do not attempt fallback strategies or alternative approaches.
- When you need schemas for multiple tools before writing code, pass all tool names to a single `get_schema` call rather than calling it once per tool.
- **Code mode and tool-search-tool are mutually exclusive workflows.** Once you begin code mode (search → get_schema → get_execute_syntax → execute), you must use ONLY the execute result. Do NOT call `call_tool` or `search_tools` after a successful `execute`. Similarly, do NOT call `execute` after using `call_tool`.
- After `execute` returns successfully, treat its output as the final answer. Do not re-fetch or verify the data with additional tool calls.

## Workflow mapping

### 1 — Browse available tools
User asks what tools exist or what the API can do.
→ Call `search` with a broad query or list all tools with one-line summaries.

### 2 — Focused search
User mentions a domain, resource, or action keyword.
→ Call `search` with that keyword and return the matching tool names and brief descriptions.

### 3 — Schema lookup (compact)
User asks for a quick or concise parameter list for a tool.
→ Call `get_schema` with `detail = "Brief"` and return parameter names and types only.

### 4 — Schema lookup (full)
User asks for a full or detailed schema.
→ Call `get_schema` with `detail = "Full"` and return the complete JSON schema.

### 5 — Single tool invocation
User asks to call an endpoint or perform an action.
→ Search to confirm the tool name, then invoke it with the supplied parameters.
→ Return only the fields most relevant to the user's request.

### 6 — Multi-step chain
User request requires calling one tool to obtain an ID or key, then calling another tool with that value.
→ Execute each step in sequence; present intermediate results when they aid understanding.
→ Return the final result.

### 7 — Counts and metadata
User asks for totals, counts, or pagination metadata.
→ Call the appropriate tool and surface only the metadata fields requested.

### 8 — Code mode (pure Python compute)
Use `execute` only when the task genuinely requires Python logic (e.g. sorting, filtering, math across many results) that cannot be done with a single tool call.
Strict order is mandatory: `search` -> `get_schema` -> `get_execute_syntax` -> `execute`.
Do not skip `get_schema` even if `search(detail="Full")` appears sufficient.
Steps:
    a. Call `search` to discover candidate tools.
    b. Call `get_schema` for the chosen tool names.
    c. Call `get_execute_syntax` to confirm runner capabilities.
    d. For OpenSandbox runner, generate Python that can fetch required API data directly inside execute code (for example via `requests.get(..., timeout=10)`) when appropriate.
    e. Do not call `search`, `get_schema`, `call_tool`, or `execute` from inside execute code.
    f. Do not generate JavaScript or TypeScript for execute.
**Critical:** Once `execute` returns a result (success or failure), your turn is DONE.
- On success: present the execute result to the user. Do NOT call `call_tool`, `search_tools`, or any other tool after this point.
- On failure: report the error directly to the user. Do NOT fall back to tool-search-tool or any other approach.

### 9 — Missing or ambiguous tool
User refers to a tool or endpoint that cannot be found.
→ Do not hallucinate a tool name or parameters.
→ Report that the tool was not found and suggest the closest matches from `search`.


## Error handling
- **No fallback between workflows**: Code mode and tool-search-tool are separate workflows. Never mix them in a single turn. If one workflow produces a result (success or failure), do not invoke the other.
- **Direct error reporting**: Present the error message as-is; do not wrap, reframe, or suggest workarounds unless explicitly helpful.
- **Tool-search failures**: If `search` fails, report it directly. Do not attempt alternative discovery methods.
## Output rules
- Never expose internal reasoning or chain-of-thought.
- Use bullet lists for tool name enumerations.
- Use compact key-value format (`field: value`) for single-record results.
- Use short prose for multi-record summaries.
- Omit fields the user did not ask for unless they are essential for context.
""";

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string model;
    private readonly string? gitHubToken;
    private readonly string mcpEndpoint;
    private readonly string mcpServerName;
    private readonly TimeSpan sendTimeout;
    private readonly TimeSpan hardSendTimeout;
    private readonly ILogger<CopilotChatService> logger;
    private static readonly ActivitySource ActivitySource = new("McpServer.CopilotChatService");

    private CopilotClient? client;
    private CopilotSession? session;

    public CopilotChatService(IConfiguration configuration, ILogger<CopilotChatService> logger)
    {
        model = configuration["Copilot:Model"] ?? "gpt-5";
        gitHubToken = configuration["Copilot:GitHubToken"];
        mcpEndpoint = configuration["Mcp:Endpoint"] ?? "http://localhost:5100/mcp";
        mcpServerName = configuration["Copilot:McpServerName"] ?? "mcp-experiments";
        this.logger = logger;

        int configuredTimeoutSeconds = configuration.GetValue<int?>("Copilot:SendTimeoutSeconds") ?? DefaultSendTimeoutSeconds;
        if (configuredTimeoutSeconds <= 0)
        {
            configuredTimeoutSeconds = DefaultSendTimeoutSeconds;
        }

        configuredTimeoutSeconds = Math.Min(configuredTimeoutSeconds, MaxSendTimeoutSeconds);

        sendTimeout = TimeSpan.FromSeconds(configuredTimeoutSeconds);
        // Guard against SDK-level waits that do not honor the provided timeout reliably.
        hardSendTimeout = sendTimeout + TimeSpan.FromSeconds(HardTimeoutBufferSeconds);
    }

    public async Task<ChatTurnMetrics> SendPromptAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Prompt cannot be empty.", nameof(prompt));
        }

        using Activity? activity = ActivitySource.StartActivity("copilot.chat.turn", ActivityKind.Client);
        await EnsureSessionAsync(cancellationToken);

        int promptTokensPerAttempt = CountTokens(prompt);
        int totalPromptTokens = 0;
        int totalCompletionTokens = 0;
        int attemptCount = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            attemptCount++;
            totalPromptTokens += promptTokensPerAttempt;

            var response = await SendWithHardTimeoutAsync(prompt, cancellationToken);

            string content = response?.Data?.Content ?? "(no response)";
            int completionTokens = CountTokens(content);
            totalCompletionTokens += completionTokens;

            stopwatch.Stop();

            if (attemptCount > 1)
            {
                logger.LogWarning("Chat turn succeeded after {AttemptCount} attempts. Total tokens: prompt={TotalPromptTokens}, completion={TotalCompletionTokens}, total={TotalTokens}",
                    attemptCount, totalPromptTokens, totalCompletionTokens, totalPromptTokens + totalCompletionTokens);
            }

            return new ChatTurnMetrics(
                content,
                totalPromptTokens,
                totalCompletionTokens,
                totalPromptTokens + totalCompletionTokens,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (IsSessionNotFoundException(ex))
        {
            // Session became stale; clear and retry with fresh session
            logger.LogInformation("Session stale after {AttemptCount} attempt(s), tokens so far: prompt={PromptTokens}, completion={CompletionTokens}",
                attemptCount, totalPromptTokens, totalCompletionTokens);

            await gate.WaitAsync(cancellationToken);
            try
            {
                if (session is not null)
                {
                    await session.DisposeAsync();
                    session = null;
                }
            }
            finally
            {
                gate.Release();
            }

            // Retry with fresh session
            await EnsureSessionAsync(cancellationToken);

            attemptCount++;
            totalPromptTokens += promptTokensPerAttempt;

            try
            {
                var retryResponse = await SendWithHardTimeoutAsync(prompt, cancellationToken);

                string retryContent = retryResponse?.Data?.Content ?? "(no response)";
                int retryCompletionTokens = CountTokens(retryContent);
                totalCompletionTokens += retryCompletionTokens;

                stopwatch.Stop();

                logger.LogWarning("Chat turn recovered. Total attempts: {AttemptCount}, total tokens: prompt={TotalPromptTokens}, completion={TotalCompletionTokens}, total={TotalTokens}",
                    attemptCount, totalPromptTokens, totalCompletionTokens, totalPromptTokens + totalCompletionTokens);

                return new ChatTurnMetrics(
                    retryContent,
                    totalPromptTokens,
                    totalCompletionTokens,
                    totalPromptTokens + totalCompletionTokens,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (Exception retryEx) when (IsSessionNotFoundException(retryEx))
            {
                stopwatch.Stop();
                throw new InvalidOperationException(
                    "Copilot session became stale and could not be recovered after retry. Please reset session and try again.",
                    retryEx);
            }
        }
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

        }
        finally
        {
            gate.Release();
        }
    }

    private async Task EnsureSessionAsync(CancellationToken cancellationToken)
    {
        if (session is not null)
        {
            return;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (session is not null)
            {
                return;
            }

            if (session is not null)
            {
                await session.DisposeAsync();
                session = null;
            }

            if (client is null)
            {
                CopilotClientOptions options = new()
                {
                    GitHubToken = gitHubToken,
                    UseLoggedInUser = string.IsNullOrWhiteSpace(gitHubToken),
                };

                client = new CopilotClient(options);
                await client.StartAsync(cancellationToken);

                ValidateConfiguredToken(gitHubToken);

                GetAuthStatusResponse authStatus = await client.GetAuthStatusAsync(cancellationToken);
                if (!authStatus.IsAuthenticated)
                {
                    throw new InvalidOperationException(
                        "Copilot CLI is not authenticated. Configure a supported token (gho_, ghu_, or github_pat_) or enable logged-in user authentication.");
                }

                try
                {
                    await client.ListModelsAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(BuildModelDiscoveryErrorMessage(ex), ex);
                }
            }

            SessionConfig config = new()
            {
                Model = model,
                OnPermissionRequest = PermissionHandler.ApproveAll,
                SystemMessage = new SystemMessageConfig
                {
                    Mode = SystemMessageMode.Append,
                    Content = PromptModeSystem,
                },
                McpServers = BuildMcpServerConfig(),
            };

            session = await client.CreateSessionAsync(config, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsSessionNotFoundException(Exception ex)
    {
        if (ex.Message.Contains("Session not found", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ex.InnerException is not null && IsSessionNotFoundException(ex.InnerException);
    }

    private async Task<AssistantMessageEvent?> SendWithHardTimeoutAsync(string prompt, CancellationToken cancellationToken)
    {
        Task<AssistantMessageEvent?> sendTask = session!.SendAndWaitAsync(new MessageOptions
        {
            Prompt = prompt,
        }, sendTimeout, cancellationToken);

        try
        {
            return await sendTask.WaitAsync(hardSendTimeout, cancellationToken);
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(
                ex,
                "Copilot send exceeded hard timeout. Requested timeout: {RequestedTimeoutSeconds}s, hard timeout: {HardTimeoutSeconds}s.",
                sendTimeout.TotalSeconds,
                hardSendTimeout.TotalSeconds);

            throw new TimeoutException(
                $"Copilot did not return within {hardSendTimeout.TotalSeconds:0}s. " +
                "Try again with a shorter prompt or increase Copilot:SendTimeoutSeconds.",
                ex);
        }
    }

    private static void ValidateConfiguredToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        if (token.StartsWith("ghp_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Copilot:GitHubToken is a classic personal access token (ghp_), which the GitHub Copilot SDK does not support. Use a gho_, ghu_, or github_pat_ token instead.");
        }
    }

    private string BuildModelDiscoveryErrorMessage(Exception ex)
    {
        string message = ex.Message;

        if (gitHubToken?.StartsWith("github_pat_", StringComparison.OrdinalIgnoreCase) == true
            && message.Contains("Copilot Requests", StringComparison.OrdinalIgnoreCase)
            && message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            return $"Failed to discover Copilot models for '{model}'. The configured fine-grained personal access token is missing the required 'Copilot Requests' permission. Update Copilot:GitHubToken to a github_pat_ token that grants 'Copilot Requests', or use a gho_ or ghu_ user token for an account with Copilot access. SDK error: {message}";
        }

        return $"Failed to discover Copilot models for '{model}'. Verify that Copilot:GitHubToken is a supported token type and that the authenticated account has Copilot access. SDK error: {message}";
    }

    private Dictionary<string, object> BuildMcpServerConfig()
    {
        Dictionary<string, object> serverConfig = new()
        {
            ["type"] = "http",
            ["url"] = mcpEndpoint,
            ["tools"] = new[] { "*" },
        };

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

    private static int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(text.Length / 4d));
    }

    public sealed record ChatTurnMetrics(
        string Content,
        int PromptTokens,
        int CompletionTokens,
        int TotalTokens,
        long ElapsedMilliseconds);
}

/// <summary>
/// Request payload for chat send endpoint.
/// </summary>
public sealed record ChatPromptRequest(string Prompt);
