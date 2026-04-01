using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenSandbox;
using OpenSandbox.Config;
using OpenSandbox.Core;
using OpenSandbox.Models;
using OpenTelemetry;

namespace McpServer.CodeMode.OpenSandbox;

/// <summary>
/// Runner that validates OpenSandbox connectivity, then executes constrained code.
/// Implements retry logic with exponential backoff and comprehensive health checks.
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
    private readonly Func<string, CancellationToken, Task<RunnerResult>> remoteExecutor;

    /// <inheritdoc/>
    public string SyntaxGuide =>
        """
        Runner: OpenSandbox (Python)
        Write pure Python code.
        You may call HTTP APIs directly from Python code when needed.
        Prefer Python standard library modules for HTTP calls, for example `urllib.request` with an explicit timeout.
        IMPORTANT: Always set a User-Agent header on HTTP requests. Many APIs reject the default Python User-Agent with 403 Forbidden.
        Prefer assigning the final value to `result`.
        If `result` is not set, captured stdout is returned when available.
        Code mode is fully isolated from tool-search tools.
        Do NOT use: search_tools, call_tool, search, get_schema, or execute in this code.
        HTTP example:
            import urllib.request, json
            req = urllib.request.Request("https://api.example.com/data", headers={"User-Agent": "Mozilla/5.0"})
            with urllib.request.urlopen(req, timeout=10) as resp:
                data = json.loads(resp.read().decode())
            result = data
        Compute example:
            data = [1, 2, 3]
            result = sum(data)
        The final `result` value (or stdout fallback) is returned as tool output.
        """;



    /// <summary>
    /// Creates the OpenSandbox-backed runner.
    /// </summary>
    /// <param name="options">OpenSandbox connection and execution options.</param>
    /// <param name="loggerFactory">Logger factory for creating component loggers.</param>
    /// <param name="remoteExecutor">Optional override for the remote executor (used in tests).</param>
    public OpenSandboxRunner(
        OpenSandboxRunnerOptions options,
        ILoggerFactory loggerFactory,
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
        this.remoteExecutor = remoteExecutor ?? ((code, ct) => ExecuteInSandboxAsync(this.options, this.logger, code, ct));
    }

    /// <summary>
    /// Executes Python code inside an OpenSandbox container with retry logic.
    /// The code must assign its final value to a <c>result</c> variable.
    /// Tool-search meta-tool calls are explicitly forbidden in code mode.
    /// </summary>
    public async Task<RunnerResult> RunAsync(string code, CancellationToken ct)
    {
        logger.LogInformation(
            "OpenSandbox execution requested. Timeout: {Timeout}, MaxToolCalls: {MaxToolCalls}.",
            options.Timeout,
            options.MaxToolCalls);

        using Activity? activity = ActivitySource.StartActivity("opensandbox.run", ActivityKind.Internal);
        activity?.SetTag("mcp.code", code);
        activity?.SetTag("mcp.code.length", code.Length);
        activity?.SetTag("mcp.sandbox.domain", options.Domain);
        activity?.SetTag("mcp.sandbox.image", options.Image);

        // Code mode must stay isolated from tool-search meta-tools.
        if (SandboxCodeGuard.ContainsForbiddenMetaToolUsage(code))
        {
            throw new InvalidOperationException(
            "Code mode is isolated from tool-search tools. " +
            "Do not call search_tools, call_tool, search, get_schema, or execute inside code mode; use pure Python compute only.");
        }

        logger.LogInformation(
            "Generated Python code for OpenSandbox execute:\n{Code}",
            code);

        try
        {
            using CancellationTokenSource timeoutCts = new(options.Timeout);
            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            RunnerResult remoteResult = await remoteExecutor(code, linkedCts.Token);
            activity?.SetTag("mcp.execute.mode", "opensandbox");
            activity?.SetTag("mcp.execute.callCount", remoteResult.CallsExecuted);
            activity?.SetTag("mcp.execute.hasFinalValue", remoteResult.FinalValue is not null);
            return remoteResult;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            activity?.SetTag("mcp.execute.mode", "opensandbox");
            activity?.SetTag("mcp.execute.timeout", true);
            activity?.SetTag("error.type", nameof(OperationCanceledException));
            activity?.AddEvent(new ActivityEvent("remote.execute.timeout"));

            throw new TimeoutException(
                $"OpenSandbox execution timed out after {options.Timeout.TotalSeconds:0.###}s.",
                ex);
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
    /// Executes Python code in OpenSandbox with retry logic and comprehensive error handling.
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

        var config = ConnectionConfigBuilder.Build(options);
        string encodedCode = Convert.ToBase64String(Encoding.UTF8.GetBytes(code));
        string command = BuildPythonCommand(encodedCode);

        logger.LogInformation(
            "Sending generated Python code to OpenSandbox server:\n{Code}",
            code);

        // Suppress HTTP auto-instrumentation for SDK internal polling and RPC chatter.
        using var suppressScope = SuppressInstrumentationScope.Begin();

        logger.LogInformation(
            "Creating OpenSandbox instance for domain {Domain} with image {Image}.",
            options.Domain,
            options.Image);

        // Retry sandbox creation with exponential backoff
        await using Sandbox sandbox = await RetryHelper.RetryAsync(
            async () => await Sandbox.CreateAsync(new SandboxCreateOptions
            {
                ConnectionConfig = config,
                Image = options.Image,
                TimeoutSeconds = options.TimeoutSeconds,
                ReadyTimeoutSeconds = options.ReadyTimeoutSeconds,
            }).WaitAsync(ct),
            maxAttempts: 3,
            initialDelay: TimeSpan.FromSeconds(0.5),
            cancellationToken: ct);

        logger.LogInformation(
            "OpenSandbox instance is ready. Running generated Python command.");

        Execution execution;
        try
        {
            // Retry sandbox command execution
            execution = await RetryHelper.RetryAsync(
                async () => await sandbox.Commands.RunAsync(command, cancellationToken: ct).WaitAsync(ct),
                maxAttempts: 2,
                initialDelay: TimeSpan.FromSeconds(0.5),
                cancellationToken: ct);

            logger.LogInformation("OpenSandbox command execution completed.");
        }
        finally
        {
            try
            {
                await sandbox.KillAsync();
                logger.LogInformation("OpenSandbox instance killed.");
            }
            catch (Exception killEx)
            {
                logger.LogWarning(killEx, "Failed to kill OpenSandbox instance cleanly.");
            }
        }

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

        logger.LogInformation("OpenSandbox execution raw output: {PayloadLine}", payloadLine);
        SandboxExecutionPayload? payload = JsonSerializer.Deserialize<SandboxExecutionPayload>(payloadLine, PayloadJsonOptions);
        logger.LogInformation("OpenSandbox execution payload deserialized: {Payload}", payload);
        if (payload is null)
        {
            throw new InvalidOperationException("OpenSandbox execution payload is empty.");
        }

        if (!payload.Ok)
        {
            throw new InvalidOperationException(
                $"OpenSandbox script error: {payload.Error ?? "Unknown error"}. {payload.Traceback} {payload.Stderr}");
        }

        logger.LogInformation("OpenSandbox remote execution succeeded for domain {Domain}.", options.Domain);

        activity?.SetTag("mcp.execute.callCount", 0);
        object? finalValue = payload.FinalValue;
        if (finalValue is null && !string.IsNullOrWhiteSpace(payload.Stdout))
        {
            finalValue = payload.Stdout.TrimEnd();
        }

        activity?.SetTag("mcp.execute.hasFinalValue", finalValue is not null);

        return new RunnerResult(finalValue, 0);
    }



    /// <summary>
    /// Builds the Python command to execute user code with proper isolation and error handling.
    /// </summary>
    private static string BuildPythonCommand(string encodedCode)
    {
        return $$"""
            python3 - <<'PY'
            import base64
            import contextlib
            import io
            import json
            import traceback

            code = base64.b64decode("{{encodedCode}}".encode("ascii")).decode("utf-8")
            scope = {}
            captured_stdout = io.StringIO()
            captured_stderr = io.StringIO()
            try:
                with contextlib.redirect_stdout(captured_stdout), contextlib.redirect_stderr(captured_stderr):
                    exec(code, scope, scope)
                payload = {
                    "ok": True,
                    "finalValue": scope.get("result"),
                    "stdout": captured_stdout.getvalue(),
                    "stderr": captured_stderr.getvalue()
                }
            except BaseException as ex:
                payload = {
                    "ok": False,
                    "error": str(ex),
                    "traceback": traceback.format_exc(),
                    "stdout": captured_stdout.getvalue(),
                    "stderr": captured_stderr.getvalue()
                }

            try:
                print(json.dumps(payload, ensure_ascii=False, default=str))
            except Exception as serialization_ex:
                fallback_payload = {
                    "ok": False,
                    "error": f"Failed to serialize execution payload: {serialization_ex}",
                    "traceback": traceback.format_exc(),
                    "stdout": captured_stdout.getvalue(),
                    "stderr": captured_stderr.getvalue()
                }
                print(json.dumps(fallback_payload, ensure_ascii=False, default=str))
            PY
            """;
    }
}
