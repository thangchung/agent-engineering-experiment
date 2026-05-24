using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyperlightSandbox.Api;
using HyperlightSandbox.Guest.Python;

namespace McpServer.CodeMode.Hyperlight;

public sealed class HyperlightSandboxRunner : ISandboxRunner, IDisposable
{
    private static readonly Regex AbsoluteHttpUrlRegex = new(
        "https?://[^\\s\"'\\)\\]]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly ActivitySource ActivitySource = new("McpServer.CodeMode.HyperlightSandboxRunner");

    private readonly TimeSpan timeout;
    private readonly int maxToolCalls;
    private readonly ILogger<HyperlightSandboxRunner> logger;
    private readonly IReadOnlyList<Uri> allowedBaseUris;
    private readonly Sandbox sandbox;
    private bool disposed;

    public HyperlightSandboxRunner(
        TimeSpan timeout,
        int maxToolCalls,
        ILogger<HyperlightSandboxRunner> logger,
        IReadOnlyList<string>? allowedBaseUrls = null)
    {
        this.timeout = timeout;
        this.maxToolCalls = maxToolCalls;
        this.logger = logger;
        allowedBaseUris = NormalizeAllowedBaseUris(allowedBaseUrls);

        SandboxBuilder builder = new SandboxBuilder()
            .WithPythonModule()
            .WithTempOutput();

        sandbox = builder.Build();
        foreach (Uri allowedBaseUri in allowedBaseUris)
        {
            sandbox.AllowDomain(allowedBaseUri.AbsoluteUri.TrimEnd('/'));
        }
    }

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
        Runner: hyperlight (Python sandbox)
        Write pure Python code.
        For HTTP calls in this sandbox, prefer built-in `http_get(url)` and `http_post(url, body="")`.
        A lightweight `requests` compatibility shim object is preloaded as `requests` for fallback compatibility.
        Do not import `requests` in generated code.
        Do NOT rely on `urllib`, `http.client`, or other stdlib HTTP modules in guest code.
        Prefer assigning the final value to `result`.
        If `result` is not set, stdout fallback is returned when available.
        {{baseUrlSection}}
        {{allowedSection}}
        Code mode is isolated from tool-Search tools.
        Do NOT use: SearchTools, CallTool, Search, GetSchema, or Execute in this code.
        Example:
            resp = http_get(f"{BASE_URL}/pet/findByStatus?status=sold")
            if resp["status"] >= 400:
                raise Exception(f"HTTP {resp['status']}")
            result = resp["body"]
        The final `result` value (or stdout fallback) is returned as tool output.
        """;
        }
    }

    public async Task<RunnerResult> RunAsync(string code, CancellationToken ct)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        logger.LogInformation(
            "Hyperlight constrained execution requested. Timeout: {Timeout}, MaxToolCalls: {MaxToolCalls}.",
            timeout,
            maxToolCalls);

        using Activity? activity = ActivitySource.StartActivity("codemode.run", ActivityKind.Internal);
        activity?.SetTag("mcp.code.length", code.Length);
        activity?.SetTag("mcp.Execute.timeout.ms", timeout.TotalMilliseconds);
        activity?.SetTag("mcp.Execute.maxToolCalls", maxToolCalls);

        if (SandboxCodeGuard.ContainsForbiddenMetaToolUsage(code))
        {
            throw new InvalidOperationException(
                "Code mode is isolated from tool-Search tools. " +
                "Do not call SearchTools, CallTool, Search, GetSchema, or Execute inside code mode; use pure Python compute only. " +
                "If you need an MCP tool, call it outside Execute. If you stay in Execute, rewrite the task as direct Python logic or HTTP requests with http_get/http_post.");
        }

        if (ContainsForbiddenHardcodedApiUsage(code))
        {
            throw new InvalidOperationException(
                "Code mode HTTP calls must stay within configured OpenAPI data sources. " +
                "Use BASE_URL (or ALLOWED_BASE_URLS) instead of hardcoded external URLs.");
        }

        logger.LogInformation("Generated Python code for Hyperlight Execute:\n{Code}", code);

        using CancellationTokenSource timeoutCts = new(timeout);
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        ExecutionResult executionResult = await Task.Run(() => ExecuteInSandbox(code), linkedCts.Token);

        activity?.SetTag("mcp.Execute.callCount", 0);
        activity?.SetTag("mcp.Execute.hasFinalValue", executionResult.Success);

        object? finalValue = ExtractFinalValue(executionResult);
        return new RunnerResult(finalValue, 0);
    }

    private ExecutionResult ExecuteInSandbox(string code)
    {
        string serializedCode = JsonSerializer.Serialize(code);
        string serializedDefaultBaseUrl = JsonSerializer.Serialize(
            allowedBaseUris.Count > 0 ? allowedBaseUris[0].AbsoluteUri.TrimEnd('/') : string.Empty);

        string wrapper = $$"""
code = {{serializedCode}}
BASE_URL = {{serializedDefaultBaseUrl}}

try:
    import sys
except Exception:
    sys = None

_native_http_get = None
_native_http_post = None

try:
    _native_http_get = http_get
except Exception:
    _native_http_get = None

try:
    _native_http_post = http_post
except Exception:
    _native_http_post = None

if _native_http_get is None or _native_http_post is None:
    try:
        from hyperlight import http_get as _hl_http_get, http_post as _hl_http_post
        if _native_http_get is None:
            _native_http_get = _hl_http_get
        if _native_http_post is None:
            _native_http_post = _hl_http_post
    except Exception:
        pass

def http_get(url):
    if _native_http_get is None:
        raise Exception("http_get is unavailable in this Hyperlight guest runtime")
    return _native_http_get(url)

def http_post(url, body=""):
    if _native_http_post is None:
        raise Exception("http_post is unavailable in this Hyperlight guest runtime")
    return _native_http_post(url, body)

def _encode_query(params):
    if params is None:
        return ""
    if isinstance(params, dict):
        parts = []
        for key, value in params.items():
            if isinstance(value, (list, tuple)):
                for item in value:
                    parts.append(str(key) + "=" + str(item))
            else:
                parts.append(str(key) + "=" + str(value))
        return "&".join(parts)
    return str(params)

def _append_query(url, params):
    query = _encode_query(params)
    if not query:
        return url
    return url + ("&" if "?" in url else "?") + query

class _RequestsHttpError(Exception):
    pass

class _RequestsResponse:
    def __init__(self, status_code, text):
        self.status_code = int(status_code) if status_code is not None else 0
        self.text = text if text is not None else ""
        self.content = self.text.encode("utf-8") if hasattr(self.text, "encode") else self.text

    def json(self):
        try:
            import json as _json
            return _json.loads(self.text)
        except Exception:
            return self.text

    def raise_for_status(self):
        if self.status_code >= 400:
            raise _RequestsHttpError("HTTP " + str(self.status_code))

def _requests_request(method, url, params=None, data=None, json=None, headers=None, timeout=None):
    del headers, timeout
    method_upper = str(method).upper()
    target = _append_query(str(url), params)

    if method_upper == "GET":
        raw = http_get(target)
    elif method_upper == "POST":
        body = data
        if json is not None:
            try:
                import json as _json
                body = _json.dumps(json)
            except Exception:
                body = str(json)
        if body is None:
            body = ""
        raw = http_post(target, str(body))
    else:
        raise Exception("Method not supported in hyperlight requests shim: " + method_upper)

    if isinstance(raw, dict):
        return _RequestsResponse(raw.get("status", 0), raw.get("body", ""))
    return _RequestsResponse(0, str(raw))

class _RequestsModule:
    RequestException = Exception
    HTTPError = _RequestsHttpError

    def request(self, method, url, **kwargs):
        return _requests_request(method, url, **kwargs)

    def get(self, url, **kwargs):
        return _requests_request("GET", url, **kwargs)

    def post(self, url, **kwargs):
        return _requests_request("POST", url, **kwargs)

requests = _RequestsModule()
if sys is not None:
    try:
        sys.modules["requests"] = requests
    except Exception:
        pass

scope = {"BASE_URL": BASE_URL}
scope["requests"] = requests
scope["http_get"] = http_get
scope["http_post"] = http_post
try:
    exec(code, scope, scope)
    _result = scope.get("result")
    if _result is None:
        print("__MCP_RESULT__")
    else:
        try:
            import json as _json
            print("__MCP_RESULT_JSON__" + _json.dumps(_result, ensure_ascii=False, default=str))
        except Exception:
            print("__MCP_RESULT__" + str(_result))
except Exception as ex:
    print("__MCP_ERROR__" + str(ex))
    raise
""";

        ExecutionResult result = sandbox.Run(wrapper);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Hyperlight Python execution failed with exit code {result.ExitCode}: {result.Stderr}");
        }

        return result;
    }

    private object? ExtractFinalValue(ExecutionResult result)
    {
        string lastLine = result.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? string.Empty;

        if (lastLine.StartsWith("__MCP_RESULT__", StringComparison.Ordinal))
        {
            string value = lastLine["__MCP_RESULT__".Length..];
            return string.IsNullOrEmpty(value) ? null : value;
        }

        if (lastLine.StartsWith("__MCP_RESULT_JSON__", StringComparison.Ordinal))
        {
            string value = lastLine["__MCP_RESULT_JSON__".Length..];
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<object>(value);
            }
            catch
            {
                return value;
            }
        }

        if (lastLine.StartsWith("__MCP_ERROR__", StringComparison.Ordinal))
        {
            return lastLine;
        }

        return result.Stdout.TrimEnd();
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

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(HyperlightSandboxRunner));
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        sandbox.Dispose();
    }

}
