#:sdk Aspire.AppHost.Sdk@13.3.5+70b33bcb5f64c75e3ab6f57616545f35bd43dc81
#:project src/Coffeeshop.Mcp/Coffeeshop.Mcp.csproj
#:project src/ToolSearch.Gateway/ToolSearch.Gateway.csproj
#:project src/Claw.Api/Claw.Api.csproj
#:project src/Claw.Channels/Claw.Channels.csproj

var builder = DistributedApplication.CreateBuilder(args);

// ── Secrets ─────────────────────────────────────────────────────────────────
// Set via `Parameters:` in appsettings.Development.json OR dotnet user-secrets
// Example: dotnet user-secrets set "Parameters:slack-bot-token" "xoxb-..."
var slackBotToken     = builder.AddParameter("slack-bot-token",     secret: true);
var slackAppToken     = builder.AddParameter("slack-app-token",     secret: true);
var slackSigningSecret = builder.AddParameter("slack-signing-secret", secret: true);
//var foundryIqApiKey   = builder.AddParameter("foundry-iq-api-key",  secret: true);
//var foundryApiKey     = builder.AddParameter("foundry-api-key",     secret: true);
var braveSearchApiKey = builder.AddParameter("brave-search-api-key", secret: true);
var appInsightsConn   = builder.AddParameter("appinsights-connection-string", secret: true);

// ── Config Parameters ────────────────────────────────────────────────────────
// Set defaults in appsettings.json Parameters section; override per-environment
var agentProvider    = builder.AddParameter("agent-provider");
var foundryEndpoint  = builder.AddParameter("foundry-endpoint");
var foundryModel     = builder.AddParameter("foundry-model");
var foundryIqEndpoint = builder.AddParameter("foundry-iq-endpoint");
var foundryIqKbName  = builder.AddParameter("foundry-iq-kb-name");
var toolboxEndpoint  = builder.AddParameter("toolbox-endpoint");

// ── Services ──────────────────────────────────────────────────────────────────
var coffeeshop = builder.AddProject<Projects.Coffeeshop_Mcp>("coffeeshop-mcp")
    .WithHttpEndpoint(port: 5001, name: "http")
    .WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", appInsightsConn);

var gateway = builder.AddProject<Projects.ToolSearch_Gateway>("toolsearch-gateway")
    .WithHttpEndpoint(port: 5002, name: "http")
    // Coffeeshop backend URL — resolved automatically from Aspire service discovery
    .WithEnvironment("Services__CoffeeshopMcp__Url", coffeeshop.GetEndpoint("http"))
    // Foundry IQ (optional — tool enabled only when both endpoint + kb name are non-empty)
    .WithEnvironment("FoundryIQ__SearchEndpoint",    foundryIqEndpoint)
    .WithEnvironment("FoundryIQ__KnowledgeBaseName", foundryIqKbName)
    //.WithEnvironment("FoundryIQ__ApiKey",            foundryIqApiKey)
    // Foundry Toolbox (optional — tools enabled only when McpEndpoint is non-empty)
    //.WithEnvironment("Foundry__ApiKey",              foundryApiKey)
    .WithEnvironment("Toolbox__McpEndpoint",         toolboxEndpoint)
    // Brave Search (optional — web_search tool enabled only when ApiKey is non-empty)
    .WithEnvironment("BraveSearch__ApiKey",          braveSearchApiKey)
    .WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", appInsightsConn)
    .WaitFor(coffeeshop);

var clawApi = builder.AddProject<Projects.Claw_Api>("claw-api")
    .WithHttpEndpoint(port: 5000, name: "http")
    // Gateway URL — resolved automatically from Aspire service discovery
    .WithEnvironment("Services__ToolSearchGateway__Url", gateway.GetEndpoint("http"))
    // Agent provider: "copilot" (default, uses logged-in GitHub account locally)
    //                 "foundry"  (requires Foundry__Endpoint)
    .WithEnvironment("Agent__Provider",        agentProvider)
    .WithEnvironment("Agent__HostedMode",      "foundry")
    .WithEnvironment("Foundry__Endpoint",      foundryEndpoint)
    .WithEnvironment("Foundry__Model",         foundryModel)
    // GitHub Copilot token — leave empty locally to use the logged-in user's token
    //.WithEnvironment("Copilot__GitHubToken",   copilotGitHubToken)
    // GenAI telemetry: capture prompt/completion content in Aspire dashboard
    .WithEnvironment("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT", "true")
    .WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", appInsightsConn)
    .WaitFor(gateway);

builder.AddProject<Projects.Claw_Channels>("claw-channels")
    .WithHttpEndpoint(port: 5003, name: "http")
    // Forward Slack events to claw-api /invocations — resolved by Aspire
    .WithEnvironment("Agent__Provider",         agentProvider)
    .WithEnvironment("Agent__BaseUrl",          clawApi.GetEndpoint("http"))
    // Override cloud path from appsettings.Production.json — claw-api locally serves /invocations
    .WithEnvironment("Agent__InvocationsPath",  "/invocations")
    // Slack tokens
    .WithEnvironment("Slack__BotToken",        slackBotToken)
    .WithEnvironment("Slack__AppToken",        slackAppToken)
    .WithEnvironment("Slack__SigningSecret",   slackSigningSecret)
    .WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", appInsightsConn)
    .WaitFor(clawApi);

builder.Build().Run();