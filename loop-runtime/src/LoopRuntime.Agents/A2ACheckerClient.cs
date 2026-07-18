using System.Net.Http.Headers;
using System.Text.Json;
using A2A;
using LoopRuntime.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.A2A;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Web;

namespace LoopRuntime.Agents;

/// <summary>
/// Calls the Checker service over the A2A protocol using the MAF A2AAgent.
/// </summary>
public sealed class A2ACheckerClient : IChecker
{
    private readonly Uri _endpoint;
    private readonly string[] _scopes;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationHeaderProvider _authorizationHeaderProvider;
    private readonly string _agentIdentityId;
    private readonly ILogger<A2ACheckerClient> _logger;

    public A2ACheckerClient(
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        IAuthorizationHeaderProvider authorizationHeaderProvider,
        ILogger<A2ACheckerClient> logger)
    {
        // Aspire service discovery injects services__agentgateway__gateway__0 when the
        // Executor references the AgentGateway container.
        var baseUrl = configuration["Services:AgentGateway:Gateway:0"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "AgentGateway URL is not configured. Ensure the Executor project references the 'agentgateway' resource in AppHost.");
        }

        var a2aBaseUrl = baseUrl.TrimEnd('/');
        _endpoint = new Uri(a2aBaseUrl, UriKind.Absolute);
        _scopes = configuration.GetSection("Checker:Scopes").Get<string[]>()
            ?? throw new InvalidOperationException("Checker:Scopes is not configured.");
        _agentIdentityId = configuration["AgentIdentity:AgentIdentityId"]
            ?? throw new InvalidOperationException("AgentIdentity:AgentIdentityId is not configured.");
        _httpContextAccessor = httpContextAccessor;
        _authorizationHeaderProvider = authorizationHeaderProvider;
        _logger = logger;
    }

    public async Task<Verdict> ReviewAsync(string sessionId, string path, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Calling checker via A2A. SessionId={SessionId} Path={Path}", sessionId, path);
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = await CreateAuthorizationHeaderAsync(cancellationToken).ConfigureAwait(false);
        var a2aClient = new A2AClient(_endpoint, httpClient);
        var agent = a2aClient.AsAIAgent("checker", "checker", "Reviews Python code and returns a verdict.");

        var response = await agent.RunAsync(
            [new ChatMessage(ChatRole.User, $"REVIEW {sessionId} {path}")],
            options: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var json = response.Messages
            .LastOrDefault(m => m.Role == ChatRole.Assistant)
            ?.Text
            ?? response.Text
            ?? string.Empty;

        _logger.LogInformation("Checker A2A response received. Length={Length}", json.Length);

        if (string.IsNullOrWhiteSpace(json))
        {
            return Failed("Checker A2A returned an empty response.");
        }

        try
        {
            var verdict = JsonSerializer.Deserialize<Verdict>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (verdict is null)
            {
                return Failed("Checker A2A response could not be deserialized.");
            }

            return verdict;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize checker A2A response. Payload={Payload}", json);
            return Failed($"Checker A2A response was not valid JSON: {ex.Message}");
        }
    }

    private static Verdict Failed(string reason)
    {
        return new Verdict(
            false,
            new Feedback(reason, ["Check checker service logs."]),
            new RunResult(1, string.Empty, reason, false));
    }

    private async Task<AuthenticationHeaderValue> CreateAuthorizationHeaderAsync(CancellationToken cancellationToken)
    {
        var principal = _httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No authenticated user is available for downstream Checker token acquisition.");
        var header = await _authorizationHeaderProvider.CreateAuthorizationHeaderForUserAsync(
            _scopes,
            new AuthorizationHeaderProviderOptions().WithAgentIdentity(_agentIdentityId),
            principal,
            cancellationToken).ConfigureAwait(false);
        return AuthenticationHeaderValue.Parse(header);
    }
}
