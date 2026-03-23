using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenSandbox;
using OpenSandbox.Config;
using OpenSandbox.Core;
using OpenSandbox.Models;
using OpenTelemetry;

namespace McpServer.CodeMode;

/// <summary>
/// Runner that validates OpenSandbox connectivity, then executes constrained code.
/// </summary>
public sealed class OpenSandboxRunner : ISandboxRunner
{
    private static readonly ActivitySource ActivitySource = new("McpServer.CodeMode.OpenSandboxRunner");
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly OpenSandboxRunnerOptions options;
    private readonly ILogger<OpenSandboxRunner> logger;
    private readonly Func<CancellationToken, Task> preflight;
    private readonly Func<string, CancellationToken, Task<RunnerResult>> remoteExecutor;
    private readonly SemaphoreSlim preflightGate = new(1, 1);

    /// <inheritdoc/>
    public string SyntaxGuide =>
        """
        Runner: OpenSandbox (Python)
        Write Python code. Assign the final value to a variable named `result`.
        Code mode is fully isolated from tool-search tools.
        Do NOT use: search_tools, call_tool, search, get_schema, or execute in this code.
        Example:
            data = [1, 2, 3]
            result = sum(data)
        The last value of `result` is returned as the tool output.
        """;

    // 0 = unknown, 1 = healthy, -1 = unavailable (degraded mode).
    private int preflightState;
    private string? preflightFailureReason;

    /// <summary>
    /// Creates the OpenSandbox-backed runner.
    /// </summary>
    /// <param name="options">OpenSandbox connection and execution options.</param>
    /// <param name="loggerFactory">Logger factory for creating component loggers.</param>
    /// <param name="preflight">Optional override for the preflight probe (used in tests).</param>
    /// <param name="remoteExecutor">Optional override for the remote executor (used in tests).</param>
    public OpenSandboxRunner(
        OpenSandboxRunnerOptions options,
        ILoggerFactory loggerFactory,
        Func<CancellationToken, Task>? preflight = null,
        Func<string, CancellationToken, Task<RunnerResult>>? remoteExecutor = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (string.IsNullOrWhiteSpace(options.Domain))
        {
            throw new InvalidOperationException("OpenSandbox:Domain is required when CodeMode:Runner=opensandbox.");
        }

        this.options = options;
        this.logger = loggerFactory.CreateLogger<OpenSandboxRunner>();
        this.preflight = preflight ?? (ct => ProbeSandboxAsync(this.options, this.logger, ct));
        this.remoteExecutor = remoteExecutor ?? ((code, ct) => ExecuteInSandboxAsync(this.options, this.logger, code, ct));
    }

    /// <summary>
    /// Executes Python code inside an OpenSandbox container. The code must assign its final value
    /// to a <c>result</c> variable. Tool-search meta-tool calls are explicitly forbidden in code mode.
    /// </summary>
    public async Task<RunnerResult> RunAsync(string code, CancellationToken ct)
    {
        logger.LogInformation("Code generated {code}. Timeout: {timeout}, MaxToolCalls: {maxToolCalls}.", code, options.Timeout, options.MaxToolCalls);

        using Activity? activity = ActivitySource.StartActivity("opensandbox.run", ActivityKind.Internal);
        activity?.SetTag("mcp.code", code);
        activity?.SetTag("mcp.code.length", code.Length);
        activity?.SetTag("mcp.sandbox.domain", options.Domain);
        activity?.SetTag("mcp.sandbox.image", options.Image);

        // Code mode must stay isolated from tool-search meta-tools.
        if (ContainsForbiddenMetaToolUsage(code))
        {
            throw new InvalidOperationException(
            "Code mode is isolated from tool-search tools. " +
            "Do not call search_tools, call_tool, search, get_schema, or execute inside code mode; use pure Python compute only.");
        }

        await EnsurePreflightAsync(activity, ct);
        if (preflightState != 1)
        {
            string reason = string.IsNullOrWhiteSpace(preflightFailureReason)
                ? "OpenSandbox preflight did not become healthy."
                : preflightFailureReason;

            throw new InvalidOperationException($"OpenSandbox is unavailable for execute: {reason}");
        }

        try
        {
            RunnerResult remoteResult = await remoteExecutor(code, ct);
            activity?.SetTag("mcp.execute.mode", "opensandbox");
            activity?.SetTag("mcp.execute.callCount", remoteResult.CallsExecuted);
            activity?.SetTag("mcp.execute.hasFinalValue", remoteResult.FinalValue is not null);
            return remoteResult;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            activity?.SetTag("mcp.execute.mode", "opensandbox");
            activity?.SetTag("mcp.execute.remoteFailed", true);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.AddEvent(new ActivityEvent("remote.execute.failed"));
            throw;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="code"/> attempts to invoke
    /// tool-search meta-tools inside code mode.
    /// </summary>
    private static bool ContainsForbiddenMetaToolUsage(string code) =>
        code.Contains("call_tool(", StringComparison.Ordinal) ||
        code.Contains("search_tools(", StringComparison.Ordinal) ||
        code.Contains("search(", StringComparison.Ordinal) ||
        code.Contains("get_schema(", StringComparison.Ordinal) ||
        code.Contains("await call_tool(", StringComparison.Ordinal);

    /// <summary>
    /// Runs OpenSandbox preflight once and caches availability for strict OpenSandbox execution.
    /// </summary>
    private async Task EnsurePreflightAsync(Activity? activity, CancellationToken ct)
    {
        if (preflightState != 0)
        {
            activity?.SetTag("mcp.preflight.state", preflightState == 1 ? "healthy" : "unavailable");
            return;
        }

        await preflightGate.WaitAsync(ct);
        try
        {
            if (preflightState != 0)
            {
                activity?.SetTag("mcp.preflight.state", preflightState == 1 ? "healthy" : "unavailable");
                return;
            }

            try
            {
                await preflight(ct);
                preflightState = 1;
                activity?.SetTag("mcp.preflight.state", "healthy");
                activity?.AddEvent(new ActivityEvent("preflight.completed"));
            }
            catch (SandboxReadyTimeoutException ex) when (!ct.IsCancellationRequested)
            {
                preflightState = -1;
                preflightFailureReason = ex.Message;
                activity?.SetTag("mcp.preflight.state", "unavailable");
                activity?.SetTag("mcp.preflight.degraded", false);
                activity?.SetTag("error.type", nameof(SandboxReadyTimeoutException));
                activity?.AddEvent(new ActivityEvent("preflight.timeout"));

                logger.LogWarning(
                    ex,
                    "OpenSandbox preflight timed out for domain {Domain} after {ReadyTimeoutSeconds}s. " +
                    "Execute remains strict OpenSandbox mode. Increase OpenSandbox:ReadyTimeoutSeconds if startup is slow.",
                    options.Domain,
                    options.ReadyTimeoutSeconds);
            }
            catch (TimeoutException ex) when (!ct.IsCancellationRequested)
            {
                preflightState = -1;
                preflightFailureReason = ex.Message;
                activity?.SetTag("mcp.preflight.state", "unavailable");
                activity?.SetTag("mcp.preflight.degraded", false);
                activity?.SetTag("error.type", nameof(TimeoutException));
                activity?.AddEvent(new ActivityEvent("preflight.timeout"));

                logger.LogWarning(
                    ex,
                    "OpenSandbox preflight timed out for domain {Domain} after {ReadyTimeoutSeconds}s. " +
                    "Execute remains strict OpenSandbox mode. Increase OpenSandbox:ReadyTimeoutSeconds if startup is slow.",
                    options.Domain,
                    options.ReadyTimeoutSeconds);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                preflightState = -1;
                preflightFailureReason = ex.Message;
                activity?.SetTag("mcp.preflight.state", "unavailable");
                activity?.SetTag("mcp.preflight.degraded", false);
                activity?.SetTag("error.type", ex.GetType().Name);
                activity?.AddEvent(new ActivityEvent("preflight.failed"));

                logger.LogWarning(
                    ex,
                    "OpenSandbox preflight failed for domain {Domain}. Execute remains strict OpenSandbox mode.",
                    options.Domain);
            }
        }
        finally
        {
            preflightGate.Release();
        }
    }

    /// <summary>
    /// Executes Python code in OpenSandbox and maps output to runner result.
    /// </summary>
    private static async Task<RunnerResult> ExecuteInSandboxAsync(
        OpenSandboxRunnerOptions options,
        ILogger logger,
        string code,
        CancellationToken ct)
    {
        using Activity? activity = ActivitySource.StartActivity("opensandbox.execute.remote", ActivityKind.Internal);
        activity?.SetTag("mcp.sandbox.domain", options.Domain);
        activity?.SetTag("mcp.sandbox.image", options.Image);

        ConnectionConfig config = new(new ConnectionConfigOptions
        {
            Domain = options.Domain,
            ApiKey = string.IsNullOrWhiteSpace(options.ApiKey) ? null : options.ApiKey,
            RequestTimeoutSeconds = options.RequestTimeoutSeconds,
            UseServerProxy = options.UseServerProxy,
        });

        string encodedCode = Convert.ToBase64String(Encoding.UTF8.GetBytes(code));
        string command = $$"""
            python3 - <<'PY'
            import base64
            import json
            import traceback

            code = base64.b64decode("{{encodedCode}}".encode("ascii")).decode("utf-8")
            scope = {}
            try:
                exec(code, scope, scope)
                payload = {
                    "ok": True,
                    "finalValue": scope.get("result")
                }
            except Exception as ex:
                payload = {
                    "ok": False,
                    "error": str(ex),
                    "traceback": traceback.format_exc()
                }

            print(json.dumps(payload, ensure_ascii=False, default=str))
            PY
            """;

        // Suppress HTTP auto-instrumentation for SDK internal polling and RPC chatter.
        using var suppressScope = SuppressInstrumentationScope.Begin();

        await using Sandbox sandbox = await Sandbox.CreateAsync(new SandboxCreateOptions
        {
            ConnectionConfig = config,
            Image = options.Image,
            TimeoutSeconds = options.TimeoutSeconds,
            ReadyTimeoutSeconds = options.ReadyTimeoutSeconds,
        });

        Execution execution = await sandbox.Commands.RunAsync(command, cancellationToken: ct);
        await sandbox.KillAsync();

        if (execution.Error is not null)
        {
            throw new InvalidOperationException($"OpenSandbox execution failed: {execution.Error.Name}: {execution.Error.Value}");
        }

        string stdout = string.Join(
            '\n',
            execution.Logs.Stdout.Select(message => message.Text).Where(text => !string.IsNullOrWhiteSpace(text)));

        string? payloadLine = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();

        if (string.IsNullOrWhiteSpace(payloadLine))
        {
            throw new InvalidOperationException("OpenSandbox execution produced no parseable output.");
        }

        SandboxExecutionPayload? payload = JsonSerializer.Deserialize<SandboxExecutionPayload>(payloadLine, PayloadJsonOptions);
        if (payload is null)
        {
            throw new InvalidOperationException("OpenSandbox execution payload is empty.");
        }

        if (!payload.Ok)
        {
            throw new InvalidOperationException(
                $"OpenSandbox script error: {payload.Error ?? "Unknown error"}. {payload.Traceback}");
        }

        logger.LogInformation("OpenSandbox remote execution succeeded for domain {Domain}.", options.Domain);

        activity?.SetTag("mcp.execute.callCount", 0);
        activity?.SetTag("mcp.execute.hasFinalValue", payload.FinalValue is not null);

        return new RunnerResult(payload.FinalValue, 0);
    }

    /// <summary>
    /// Creates a short-lived sandbox and runs a trivial command to validate connectivity.
    /// </summary>
    private static async Task ProbeSandboxAsync(OpenSandboxRunnerOptions options, ILogger logger, CancellationToken ct)
    {
        using Activity? activity = ActivitySource.StartActivity("opensandbox.preflight", ActivityKind.Internal);
        activity?.SetTag("mcp.sandbox.domain", options.Domain);
        activity?.SetTag("mcp.sandbox.image", options.Image);
        activity?.SetTag("mcp.sandbox.timeoutSeconds", options.TimeoutSeconds);

        ConnectionConfig config = new(new ConnectionConfigOptions
        {
            Domain = options.Domain,
            ApiKey = string.IsNullOrWhiteSpace(options.ApiKey) ? null : options.ApiKey,
            RequestTimeoutSeconds = options.RequestTimeoutSeconds,
            UseServerProxy = options.UseServerProxy,
        });

        // Suppress HTTP auto-instrumentation for OpenSandbox SDK internal polling.
        // The SDK retries readiness checks via HTTP GET, generating 100+ error spans per sandbox boot.
        // Our custom opensandbox.preflight activity and events still record the meaningful milestones.
        using var suppressScope = SuppressInstrumentationScope.Begin();

        await using Sandbox sandbox = await Sandbox.CreateAsync(new SandboxCreateOptions
        {
            ConnectionConfig = config,
            Image = options.Image,
            TimeoutSeconds = options.TimeoutSeconds,
            ReadyTimeoutSeconds = options.ReadyTimeoutSeconds,
        });

        activity?.AddEvent(new ActivityEvent("sandbox.created"));

        _ = await sandbox.Commands.RunAsync("echo opensandbox-ready");
        activity?.AddEvent(new ActivityEvent("probe.completed"));

        await sandbox.KillAsync();
        activity?.AddEvent(new ActivityEvent("sandbox.killed"));

        logger.LogInformation("OpenSandbox preflight succeeded for domain {Domain}.", options.Domain);

        ct.ThrowIfCancellationRequested();
    }

    private sealed record SandboxExecutionPayload(bool Ok, object? FinalValue, string? Error, string? Traceback);
}
