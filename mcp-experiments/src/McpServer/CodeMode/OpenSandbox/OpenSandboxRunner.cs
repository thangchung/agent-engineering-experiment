using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenSandbox;
using OpenSandbox.Config;
using OpenSandbox.Core;
using OpenSandbox.Models;
using OpenTelemetry;

namespace McpServer.CodeMode.OpenSandbox;

/// <summary>
/// Runner that validates OpenSandbox connectivity, then executes constrained code.
/// </summary>
public sealed class OpenSandboxRunner : ISandboxRunner
{
    private static readonly Regex AbsoluteHttpUrlRegex = new(
        "https?://[^\\s\"'\\)\\]]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly ActivitySource ActivitySource = new("McpServer.CodeMode.OpenSandboxRunner");
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly OpenSandboxRunnerOptions options;
    private readonly ILogger<OpenSandboxRunner> logger;
    private readonly IReadOnlyList<Uri> allowedBaseUris;
    private readonly Func<string, CancellationToken, Task<RunnerResult>> remoteExecutor;

    public string SyntaxGuide
    {
        get
        {
            string defaultBaseUrl = allowedBaseUris.Count > 0
                ? allowedBaseUris[0].AbsoluteUri.TrimEnd('/')
                : string.Empty;

            string baseUrlSection = defaultBaseUrl.Length > 0
                ? $"Use the injected `BASE_URL` variable for API calls. Default `BASE_URL` is \"{defaultBaseUrl}\"."
                : "Use the injected `BASE_URL` variable for API calls. No default URL was discovered from OpenAPI sources.";

            string allowedSection = allowedBaseUris.Count > 0
                ? $"Only call API URLs under configured OpenAPI bases via `BASE_URL`: {string.Join(", ", allowedBaseUris.Select(static u => $"\"{u.AbsoluteUri.TrimEnd('/')}\""))}."
                : "No API base allowlist is currently configured, so URL host restrictions are not enforced.";

            return $$"""
        Runner: OpenSandbox (Python)
        Write pure Python code.
        You may call HTTP APIs directly from Python code when needed.
        A lightweight `requests`-compatible shim is available for basic HTTP requests; set a timeout (for example: timeout=10).
        Prefer assigning the final value to `result`.
        If `result` is not set, captured stdout is returned when available.
        CRITICAL: Do NOT use `Result`, `RESULT`, or any other casing - only lowercase `result` is captured.
        Do NOT use bare identifiers as section dividers (e.g., `Result # comment`). Use only `#` comments.
        {{baseUrlSection}}
        {{allowedSection}}
        Code mode is fully isolated from tool-Search tools.
        Do NOT use: SearchTools, CallTool, Search, GetSchema, or Execute in this code.
        HTTP example:
            import requests
            response = requests.get(f"{BASE_URL}/breweries/random", timeout=10)
            result = response.json()
        Compute example:
            data = [1, 2, 3]
            result = sum(data)
        The final `result` value (or stdout fallback) is returned as tool output.
        """;
        }
    }

    public OpenSandboxRunner(
        OpenSandboxRunnerOptions options,
        ILoggerFactory loggerFactory,
        IReadOnlyList<string>? allowedBaseUrls = null,
        Func<string, CancellationToken, Task<RunnerResult>>? remoteExecutor = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (string.IsNullOrWhiteSpace(options.Domain))
        {
            throw new InvalidOperationException("OpenSandbox:Domain is required when CodeMode:Runner=opensandbox.");
        }

        this.options = options;
        logger = loggerFactory.CreateLogger<OpenSandboxRunner>();
        allowedBaseUris = NormalizeAllowedBaseUris(allowedBaseUrls);
        this.remoteExecutor = remoteExecutor ?? ((code, ct) => ExecuteInSandboxAsync(this.options, logger, code, ct));
    }

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

        if (SandboxCodeGuard.ContainsForbiddenMetaToolUsage(code))
        {
            throw new InvalidOperationException(
                "Code mode is isolated from tool-Search tools. " +
                "Do not call SearchTools, CallTool, Search, GetSchema, or Execute inside code mode; use pure Python compute only.");
        }

        if (ContainsForbiddenHardcodedApiUsage(code))
        {
            throw new InvalidOperationException(
                "Code mode HTTP calls must stay within configured OpenAPI data sources. " +
                "Use BASE_URL (or ALLOWED_BASE_URLS) instead of hardcoded external URLs.");
        }

        logger.LogInformation("Generated Python code for OpenSandbox Execute:\n{Code}", code);

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

    private async Task<RunnerResult> ExecuteInSandboxAsync(
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
        string defaultBaseUrl = allowedBaseUris.Count > 0 ? allowedBaseUris[0].AbsoluteUri.TrimEnd('/') : string.Empty;
        string command = BuildPythonCommand(encodedCode, defaultBaseUrl);

        logger.LogInformation("Sending generated Python code to OpenSandbox server:\n{Code}", code);

        using var suppressScope = SuppressInstrumentationScope.Begin();

        logger.LogInformation(
            "Creating OpenSandbox instance for domain {Domain} with image {Image}.",
            options.Domain,
            options.Image);

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

        logger.LogInformation("OpenSandbox instance is ready. Running generated Python command.");

        Execution execution;
        try
        {
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
            string stderr = string.Join(
                '\n',
                execution.Logs.Stderr.Select(message => message.Text).Where(text => !string.IsNullOrWhiteSpace(text)));
            string stdoutForError = string.Join(
                '\n',
                execution.Logs.Stdout.Select(message => message.Text).Where(text => !string.IsNullOrWhiteSpace(text)));

            logger.LogWarning(
                "OpenSandbox execution failed. stderr: {Stderr}; stdout: {Stdout}",
                stderr,
                stdoutForError);

            throw new InvalidOperationException(
                $"OpenSandbox execution failed: {execution.Error.Name}: {execution.Error.Value}. {stderr} {stdoutForError}");
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

    private static string BuildPythonCommand(string encodedCode, string defaultBaseUrl)
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
            BASE_URL = {{JsonSerializer.Serialize(defaultBaseUrl)}}

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

            scope = {"requests": requests, "BASE_URL": BASE_URL}
            captured_stdout = io.StringIO()
            captured_stderr = io.StringIO()
            try:
                with contextlib.redirect_stdout(captured_stdout), contextlib.redirect_stderr(captured_stderr):
                    exec(code, scope, scope)
                payload = {
                    "ok": True,
                    "finalValue": scope.get("result"),
                    "stdout": captured_stdout.getvalue(),
                    "stderr": captured_stderr.getvalue(),
                }
            except BaseException as ex:
                payload = {
                    "ok": False,
                    "error": str(ex),
                    "traceback": traceback.format_exc(),
                    "stdout": captured_stdout.getvalue(),
                    "stderr": captured_stderr.getvalue(),
                }

            try:
                print(json.dumps(payload, ensure_ascii=False, default=str))
            except Exception as serialization_ex:
                fallback_payload = {
                    "ok": False,
                    "error": f"Failed to serialize execution payload: {serialization_ex}",
                    "traceback": traceback.format_exc(),
                    "stdout": captured_stdout.getvalue(),
                    "stderr": captured_stderr.getvalue(),
                }
                print(json.dumps(fallback_payload, ensure_ascii=False, default=str))
            PY
            """ + "\n";
    }

    private bool ContainsForbiddenHardcodedApiUsage(string code)
    {
        if (allowedBaseUris.Count == 0)
        {
            return false;
        }

        MatchCollection matches = AbsoluteHttpUrlRegex.Matches(code);
        foreach (Match match in matches)
        {
            if (!Uri.TryCreate(match.Value, UriKind.Absolute, out Uri? discoveredUrl))
            {
                continue;
            }

            bool isAllowed = allowedBaseUris.Any(baseUri =>
                discoveredUrl.Scheme.Equals(baseUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
                discoveredUrl.Host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase) &&
                discoveredUrl.Port == baseUri.Port &&
                discoveredUrl.AbsoluteUri.StartsWith(baseUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase));

            if (!isAllowed)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<Uri> NormalizeAllowedBaseUris(IReadOnlyList<string>? allowedBaseUrls)
    {
        if (allowedBaseUrls is null || allowedBaseUrls.Count == 0)
        {
            return Array.Empty<Uri>();
        }

        return allowedBaseUrls
            .Select(baseUrl => Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? parsed) ? parsed : null)
            .Where(uri => uri is not null)
            .Select(static uri => uri!)
            .ToArray();
    }
}
