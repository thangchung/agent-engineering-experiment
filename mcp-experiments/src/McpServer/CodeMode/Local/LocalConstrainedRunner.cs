using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace McpServer.CodeMode.Local;

/// <summary>
/// Local execution backend that runs isolated Python compute without tool-bridge access.
/// </summary>
public sealed class LocalConstrainedRunner : ISandboxRunner
{
    private static readonly Regex AbsoluteHttpUrlRegex = new(
        "https?://[^\\s\"'\\)\\]]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly TimeSpan timeout;
    private readonly int maxToolCalls;
    private readonly ILogger<LocalConstrainedRunner> logger;

    // Single source of truth for the URL allowlist. String representations are derived on use.
    private readonly IReadOnlyList<Uri> allowedBaseUris;

    private static readonly ActivitySource ActivitySource = new("McpServer.CodeMode.LocalConstrainedRunner");
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Creates a local runner with runtime limits and optional allowed API base URL policy.
    /// </summary>
    public LocalConstrainedRunner(
        TimeSpan timeout,
        int maxToolCalls,
        ILogger<LocalConstrainedRunner> logger,
        IReadOnlyList<string>? allowedBaseUrls = null)
    {
        this.timeout = timeout;
        this.maxToolCalls = maxToolCalls;
        this.logger = logger;
        allowedBaseUris = NormalizeAllowedBaseUris(allowedBaseUrls);
    }

    /// <inheritdoc/>
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
        Runner: local (Python)
        Write pure Python code.
        You may call HTTP APIs directly from Python code when needed.
        A lightweight `requests`-compatible shim is available for basic HTTP requests; set a timeout (for example: timeout=10).
        Prefer assigning the final value to `result`.
        If `result` is not set, captured stdout is returned when available.
        {{baseUrlSection}}
        {{allowedSection}}
        Code mode is isolated from tool-search tools.
        Do NOT use: search_tools, call_tool, search, get_schema, or execute in this code.
        Example:
            import requests
            response = requests.get(f"{BASE_URL}/pet/findByStatus", params={"status": "sold"}, timeout=10)
            response.raise_for_status()
            result = response.json()
        The final `result` value (or stdout fallback) is returned as tool output.
        """;
        }
    }

    /// <summary>
    /// Runs isolated Python code and returns the final <c>result</c> value.
    /// </summary>
    public async Task<RunnerResult> RunAsync(string code, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        logger.LogInformation(
            "Local constrained execution requested. Timeout: {Timeout}, MaxToolCalls: {MaxToolCalls}.",
            timeout,
            maxToolCalls);

        using Activity? activity = ActivitySource.StartActivity("codemode.run", ActivityKind.Internal);
        activity?.SetTag("mcp.code", code);
        activity?.SetTag("mcp.code.length", code.Length);
        activity?.SetTag("mcp.execute.timeout.ms", timeout.TotalMilliseconds);
        activity?.SetTag("mcp.execute.maxToolCalls", maxToolCalls);

        if (SandboxCodeGuard.ContainsForbiddenMetaToolUsage(code))
        {
            throw new InvalidOperationException(
                "Code mode is isolated from tool-search tools. " +
                "Do not call search_tools, call_tool, search, get_schema, or execute inside code mode; use pure Python compute only. " +
                "If you need an MCP tool, call it outside execute. If you stay in execute, rewrite the task as direct Python logic or HTTP requests with the requests-compatible shim.");
        }

        if (ContainsForbiddenHardcodedApiUsage(code))
        {
            throw new InvalidOperationException(
                "Code mode HTTP calls must stay within configured OpenAPI data sources. " +
                "Use BASE_URL (or ALLOWED_BASE_URLS) instead of hardcoded external URLs.");
        }

        logger.LogInformation(
            "Generated Python code for local constrained execute:\n{Code}",
            code);

        using CancellationTokenSource timeoutCts = new(timeout);
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        object? finalValue = await ExecutePythonLocallyAsync(code, linkedCts.Token);
        activity?.SetTag("mcp.execute.callCount", 0);
        activity?.SetTag("mcp.execute.hasFinalValue", finalValue is not null);
        return new RunnerResult(finalValue, 0);
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

    private async Task<object?> ExecutePythonLocallyAsync(string code, CancellationToken ct)
    {
        string encodedCode = Convert.ToBase64String(Encoding.UTF8.GetBytes(code));
        string serializedDefaultBaseUrl = JsonSerializer.Serialize(
            allowedBaseUris.Count > 0 ? allowedBaseUris[0].AbsoluteUri.TrimEnd('/') : string.Empty);
        string wrapper = $$"""
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
BASE_URL = {{serializedDefaultBaseUrl}}

# Install a default opener with explicit headers. Some public APIs reject
# Python's default urllib user-agent and return HTTP 403.
_opener = urllib.request.build_opener()
_opener.addheaders = [
    ("User-Agent", "mcp-experiments-local-runner/1.0"),
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

scope = {
    "BASE_URL": BASE_URL
}
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
except Exception as ex:
    payload = {
        "ok": False,
        "error": str(ex),
        "traceback": traceback.format_exc(),
        "stdout": captured_stdout.getvalue(),
        "stderr": captured_stderr.getvalue()
    }

print(json.dumps(payload, ensure_ascii=False, default=str))
""";

        ProcessStartInfo startInfo = new()
        {
            FileName = "python3",
            Arguments = "-",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start local Python process.");
        }

        await process.StandardInput.WriteAsync(wrapper.AsMemory(), ct);
        await process.StandardInput.FlushAsync();
        process.StandardInput.Close();

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Local Python execution failed with exit code {process.ExitCode}: {stderr}");
        }

        string? payloadLine = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();

        if (string.IsNullOrWhiteSpace(payloadLine))
        {
            throw new InvalidOperationException("Local Python execution produced no parseable output.");
        }

        LocalExecutionPayload? payload = JsonSerializer.Deserialize<LocalExecutionPayload>(payloadLine, PayloadJsonOptions);
        if (payload is null)
        {
            throw new InvalidOperationException("Local Python execution payload is empty.");
        }

        if (!payload.Ok)
        {
            if (!string.IsNullOrWhiteSpace(payload.Error) &&
                payload.Error.Contains("HTTP Error 403", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Local Python script error: HTTP 403 Forbidden. " +
                    "The remote API may reject the request based on headers, rate limits, or bot protection. " +
                    "Try adding an explicit User-Agent/Accept header in your Python request or retry later.");
            }

            throw new InvalidOperationException(
                $"Local Python script error: {payload.Error ?? "Unknown error"}. {payload.Traceback} {payload.Stderr}");
        }

        object? finalValue = payload.FinalValue;
        if (finalValue is null && !string.IsNullOrWhiteSpace(payload.Stdout))
        {
            finalValue = payload.Stdout.TrimEnd();
        }

        return finalValue;
    }

    private static IReadOnlyList<Uri> NormalizeAllowedBaseUris(IReadOnlyList<string>? input)
    {
        if (input is null || input.Count == 0)
        {
            return [];
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<Uri> normalized = [];

        foreach (string value in input)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
            {
                continue;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            Uri withSlash = EnsureTrailingSlash(uri);
            if (!seen.Add(withSlash.AbsoluteUri))
            {
                continue;
            }

            normalized.Add(withSlash);
        }

        return normalized;
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        if (uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
        {
            return uri;
        }

        return new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
    }

    private sealed record LocalExecutionPayload(bool Ok, object? FinalValue, string? Error, string? Traceback, string? Stdout, string? Stderr);
}
