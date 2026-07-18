using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using LoopRuntime.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Web;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace LoopRuntime.Agents;

public sealed class RemoteMcpTools : IMcpTools
{
    private readonly Uri _endpoint;
    private readonly string[] _scopes;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationHeaderProvider _authorizationHeaderProvider;
    private readonly string _agentIdentityId;
    private readonly ILogger<RemoteMcpTools> _logger;

    public RemoteMcpTools(
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        IAuthorizationHeaderProvider authorizationHeaderProvider,
        ILogger<RemoteMcpTools> logger)
    {
        // Aspire service discovery injects services__agentgateway__gateway__0 when the
        // project references the AgentGateway container; MCP path suffix is config-driven.
        var configured = configuration["Services:AgentGateway:Gateway:0"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "AgentGateway URL is not configured. Ensure the project references the 'agentgateway' resource in AppHost.");
        }

        var mcpPathSuffix = configuration["Mcp:PathSuffix"] ?? "/mcp";
        _endpoint = new Uri(configured.TrimEnd('/') + mcpPathSuffix, UriKind.Absolute);
        _scopes = configuration.GetSection("Mcp:Scopes").Get<string[]>()
            ?? throw new InvalidOperationException("Mcp:Scopes is not configured.");
        _agentIdentityId = configuration["AgentIdentity:AgentIdentityId"]
            ?? throw new InvalidOperationException("AgentIdentity:AgentIdentityId is not configured.");
        _httpContextAccessor = httpContextAccessor;
        _authorizationHeaderProvider = authorizationHeaderProvider;
        _logger = logger;
    }

    public async Task<string> WriteFileAsync(string sessionId, string name, string content, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Calling MCP write_file. SessionId={SessionId} Name={Name} Endpoint={Endpoint}", sessionId, name, _endpoint);
        StampMcpTags("write_file", sessionId);
        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        var result = await client.CallToolAsync("write_file", new Dictionary<string, object?>
        {
            ["sessionId"] = sessionId,
            ["name"] = name,
            ["content"] = content
        }, cancellationToken: cancellationToken).ConfigureAwait(false);

        var path = ExtractTextContent(result);
        _logger.LogInformation("MCP write_file returned. SessionId={SessionId} Path={Path}", sessionId, path);
        return path;
    }

    public async Task<string> EditFileAsync(string sessionId, string path, string content, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Calling MCP edit_file. SessionId={SessionId} Path={Path}", sessionId, path);
        StampMcpTags("edit_file", sessionId);
        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        var result = await client.CallToolAsync("edit_file", new Dictionary<string, object?>
        {
            ["sessionId"] = sessionId,
            ["path"] = path,
            ["content"] = content
        }, cancellationToken: cancellationToken).ConfigureAwait(false);

        var editedPath = ExtractTextContent(result);
        _logger.LogInformation("MCP edit_file returned. SessionId={SessionId} Path={Path}", sessionId, editedPath);
        return editedPath;
    }

    public async Task<string> LoadFileAsync(string path, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Calling MCP load_file. Path={Path} Endpoint={Endpoint}", path, _endpoint);
        StampMcpTags("load_file", path);
        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("MCP client created for load_file. Path={Path}", path);
        var result = await client.CallToolAsync("load_file", new Dictionary<string, object?>
        {
            ["path"] = path
        }, cancellationToken: cancellationToken).ConfigureAwait(false);

        var text = ExtractTextContent(result);
        _logger.LogInformation("MCP load_file returned. Path={Path} Length={Length}", path, text.Length);
        return text;
    }

    public async Task<RunResult> RunPythonAsync(string path, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Calling MCP run_python. Path={Path}", path);
        StampMcpTags("run_python", path);
        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        var result = await client.CallToolAsync("run_python", new Dictionary<string, object?>
        {
            ["path"] = path
        }, cancellationToken: cancellationToken).ConfigureAwait(false);

        var payload = ExtractTextContent(result);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new RunResult(1, string.Empty, "Remote MCP tool returned no payload.", false);
        }

        try
        {
            var runResult = JsonSerializer.Deserialize<RunResult>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? new RunResult(1, string.Empty, "Failed to deserialize MCP tool output.", false);
            _logger.LogInformation("MCP run_python returned. Path={Path} ExitCode={ExitCode} TimedOut={TimedOut}", path, runResult.ExitCode, runResult.TimedOut);
            return runResult;
        }
        catch (JsonException)
        {
            _logger.LogError("Failed to deserialize run_python result. Path={Path} Payload={Payload}", path, payload);
            return new RunResult(1, string.Empty, payload, false);
        }
    }

    private async Task<McpClient> CreateClientAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating MCP client. Endpoint={Endpoint} Mode=StreamableHttp", _endpoint);
        try
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = await CreateAuthorizationHeaderAsync(cancellationToken).ConfigureAwait(false);

            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = _endpoint,
                TransportMode = HttpTransportMode.StreamableHttp
            }, httpClient, NullLoggerFactory.Instance, ownsHttpClient: true);

            var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("MCP client created successfully. Endpoint={Endpoint}", _endpoint);
            return client;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create MCP client. Endpoint={Endpoint}", _endpoint);
            throw;
        }
    }

    private async Task<AuthenticationHeaderValue> CreateAuthorizationHeaderAsync(CancellationToken cancellationToken)
    {
        var principal = _httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No authenticated user is available for downstream MCP token acquisition.");
        var header = await _authorizationHeaderProvider.CreateAuthorizationHeaderForUserAsync(
            _scopes,
            new AuthorizationHeaderProviderOptions().WithAgentIdentity(_agentIdentityId),
            principal,
            cancellationToken).ConfigureAwait(false);
        return AuthenticationHeaderValue.Parse(header);
    }

    public static string ExtractTextContent(CallToolResult result)
    {
        var text = result.Content?
            .OfType<TextContentBlock>()
            .Select(static block => block.Text)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

        return text ?? string.Empty;
    }

    private static void StampMcpTags(string toolName, string sessionIdOrPath)
    {
        var current = Activity.Current;
        if (current is null)
        {
            return;
        }

        current.SetTag("mcp.method.name", "tools/call");
        current.SetTag("mcp.resource.type", "tool");
        current.SetTag("mcp.target", "LoopRuntime.Mcp");
        current.SetTag("gen_ai.tool.name", toolName);

        if (toolName is "load_file" or "run_python")
        {
            current.SetTag("mcp.target.path", sessionIdOrPath);
        }
        else
        {
            current.SetTag("mcp.session.id", sessionIdOrPath);
        }

        current.AddEvent(new ActivityEvent("mcp.tool.call", tags: new ActivityTagsCollection([
            new KeyValuePair<string, object?>("mcp.tool.name", toolName)
        ])));
    }
}
