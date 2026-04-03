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
        A lightweight `requests`-compatible shim is available for basic HTTP requests; set a timeout (for example: timeout=10).
        Assign the final value to a variable named exactly `result` (lowercase).
        If `result` is not set, captured stdout is returned when available.
        CRITICAL: Do NOT use `Result`, `RESULT`, or any other casing — only lowercase `result` is captured.
        Do NOT use bare identifiers as section dividers (e.g., `Result # comment`). Use only `#` comments.
        Code mode is fully isolated from tool-Search tools.
        Do NOT use: SearchTools, CallTool, Search, GetSchema, or Execute in this code.
        HTTP example:
            import requests
            response = requests.get("https://api.example.com/data", timeout=10)
            result = response.json()
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
    /// Tool-Search meta-tool calls are explicitly forbidden in code mode.
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

        // Code mode must stay isolated from tool-Search meta-tools.
        if (SandboxCodeGuard.ContainsForbiddenMetaToolUsage(code))
        {
            throw new InvalidOperationException(
            "Code mode is isolated from tool-Search tools. " +
            "Do not call SearchTools, CallTool, Search, GetSchema, or Execute inside code mode; use pure Python compute only.");
        }

        logger.LogInformation(
            "Generated Python code for OpenSandbox Execute:\n{Code}",
            code);

        try
        {
            using CancellationTokenSource timeoutCts = new(options.Timeout);
            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            RunnerResult remoteResult = await remoteExecutor(code, linkedCts.Token);
            activity?.SetTag("mcp.Execute.mode", "opensandbox");
            activity?.SetTag("mcp.Execute.callCount", remoteResult.CallsExecuted);
            activity?.SetTag("mcp.Execute.hasFinalValue", remoteResult.FinalValue is not null);
            return remoteResult;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            activity?.SetTag("mcp.Execute.mode", "opensandbox");
            activity?.SetTag("mcp.Execute.timeout", true);
            activity?.SetTag("error.type", nameof(OperationCanceledException));
            activity?.AddEvent(new ActivityEvent("remote.Execute.timeout"));

            throw new TimeoutException(
                $"OpenSandbox execution timed out after {options.Timeout.TotalSeconds:0.###}s.",
                ex);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            activity?.SetTag("mcp.Execute.mode", "opensandbox");
            activity?.SetTag("mcp.Execute.remoteFailed", true);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.AddEvent(new ActivityEvent("remote.Execute.failed"));
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
        using Activity? activity = ActivitySource.StartActivity("opensandbox.Execute.remote", ActivityKind.Internal);
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

        activity?.SetTag("mcp.Execute.callCount", 0);
        object? finalValue = payload.FinalValue;
        if (finalValue is null && !string.IsNullOrWhiteSpace(payload.Stdout))
        {
            finalValue = payload.Stdout.TrimEnd();
        }

        activity?.SetTag("mcp.Execute.hasFinalValue", finalValue is not null);

        return new RunnerResult(finalValue, 0);
    }



    /// <summary>
    /// Builds the Python command to Execute user code with proper isolation and error handling.
    /// </summary>
    private static string BuildPythonCommand(string encodedCode)
    {
        return $$"""
            python3 - <<'PY'
            import base64
            import contextlib
            import io
            import json
            import sys
            import traceback
            import types
            import urllib.error
            import urllib.parse
            import urllib.request

            code = base64.b64decode("{{encodedCode}}".encode("ascii")).decode("utf-8")

            # Install a default opener with explicit headers. Some public APIs reject
            # Python's default urllib user-agent and return HTTP 403.
            _opener = urllib.request.build_opener()
            _opener.addheaders = [
                ("User-Agent", "mcp-experiments/1.0"),
                ("Accept", "application/json"),
            ]
            urllib.request.install_opener(_opener)

            class _RequestsError(Exception):
                pass

            class _RequestsHttpError(_RequestsError):
                pass

            class _RequestsResponse:
                def __init__(self, status_code, content, headers):
                    self.status_code = status_code
                    self.content = content
                    self.headers = dict(headers.items()) if headers is not None else {}
                    self.text = content.decode("utf-8", errors="replace")

                def json(self):
                    if not self.text:
                        return None
                    return json.loads(self.text)

                def raise_for_status(self):
                    if self.status_code >= 400:
                        raise _RequestsHttpError(f"HTTP {self.status_code}")

            def _append_query(url, params):
                if not params:
                    return url

                query = urllib.parse.urlencode(params, doseq=True)
                separator = "&" if "?" in url else "?"
                return f"{url}{separator}{query}"

            def _normalize_body(data, json_payload, headers):
                if json_payload is not None:
                    headers.setdefault("Content-Type", "application/json")
                    return json.dumps(json_payload).encode("utf-8")

                if isinstance(data, str):
                    return data.encode("utf-8")

                return data

            def _requests_request(method, url, params=None, data=None, json=None, headers=None, timeout=None):
                request_headers = dict(headers or {})
                request_url = _append_query(url, params)
                request_body = _normalize_body(data, json, request_headers)
                request = urllib.request.Request(request_url, data=request_body, headers=request_headers, method=method.upper())

                try:
                    with urllib.request.urlopen(request, timeout=timeout) as response:
                        return _RequestsResponse(response.getcode(), response.read(), response.headers)
                except urllib.error.HTTPError as ex:
                    return _RequestsResponse(ex.code, ex.read(), ex.headers)

            requests = types.ModuleType("requests")
            requests.RequestException = _RequestsError
            requests.HTTPError = _RequestsHttpError
            requests.request = _requests_request
            requests.get = lambda url, **kwargs: _requests_request("GET", url, **kwargs)
            requests.post = lambda url, **kwargs: _requests_request("POST", url, **kwargs)
            requests.put = lambda url, **kwargs: _requests_request("PUT", url, **kwargs)
            requests.delete = lambda url, **kwargs: _requests_request("DELETE", url, **kwargs)
            requests.patch = lambda url, **kwargs: _requests_request("PATCH", url, **kwargs)
            sys.modules["requests"] = requests

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
