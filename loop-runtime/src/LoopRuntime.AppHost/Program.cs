using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = DistributedApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("LoopRuntime.AppHost"))
    .WithMetrics(metrics => metrics.AddMeter("LoopRuntime.AppHost"))
    .WithLogging(_ => { })
    .UseOtlpExporter();

// ── Secrets ─────────────────────────────────────────────────────────────────
// Set via dotnet user-secrets (dev) or env vars (CI). Never in code.
//   dotnet user-secrets set "Parameters:azure-openai-endpoint" "https://..."
//   dotnet user-secrets set "Parameters:azure-openai-api-key"  "sk-..."
//   dotnet user-secrets set "Parameters:federated-token-file" "/path/to/assertion.jwt"
var azureOpenAiEndpoint   = builder.AddParameter("azure-openai-endpoint");
var azureOpenAiApiKey     = builder.AddParameter("azure-openai-api-key");
var federatedTokenFile    = builder.AddParameter("federated-token-file");

// ── Config Parameters ────────────────────────────────────────────────────────
// Set defaults in appsettings.json Parameters section; override per-environment.
var aiModel               = builder.AddParameter("ai-model");
var aiProvider            = builder.AddParameter("ai-provider");

// Entra tenant used by all services/gateway for JWT validation and token exchange.
var tenantId              = "768437b2-e373-41b5-9748-875e1507d85b";

// Derive the Azure resource name from the endpoint so the gateway can build
// {resourceName}.openai.azure.com upstream hosts without duplicating secrets.
var azureEndpointValue = builder.Configuration["Parameters:azure-openai-endpoint"]
    ?? throw new InvalidOperationException("Parameters:azure-openai-endpoint is required.");
var azureResourceName = new Uri(azureEndpointValue).Host.Split('.')[0];

// ── Services ─────────────────────────────────────────────────────────────────

var mcp = builder
    .AddProject<Projects.LoopRuntime_Mcp>("mcp")
    .WithHttpEndpoint(port: 5001, name: "http")
    .WithEnvironment("AzureAd__TenantId", tenantId)
    .WithEnvironment("AzureAd__ClientId", "b228aac6-5596-4cd6-bf8e-8bc343613268");

// AgentGateway container — JWT enforcement + routing (Phase 4).
// Must be declared before projects so they can reference it.
var agentgateway = builder
    .AddContainer("agentgateway", "agentgateway", "local-latest")
    .WithBindMount("./gateway.yaml", "/app/gateway.yaml")
    .WithArgs("--file", "/app/gateway.yaml")
    .WithHttpEndpoint(port: 16000, targetPort: 15000, name: "admin")
    .WithHttpEndpoint(port: 16001, targetPort: 16001, name: "mcp")
    .WithHttpEndpoint(targetPort: 4317, name: "otel")
    .WithHttpEndpoint(port: 3032, targetPort: 3032, name: "gateway", isProxied: false)
    .WithHttpEndpoint(port: 4000, targetPort: 4000, name: "llm")
    .WithBindMount("./data", "/data")
    .WithEnvironment("AZURE_OPENAI_API_KEY", azureOpenAiApiKey)
    .WithEnvironment("AZURE_OPENAI_RESOURCE_NAME", azureResourceName)
    .WaitFor(mcp);

var checker = builder
    .AddProject<Projects.LoopRuntime_Checker>("checker")
    .WithHttpEndpoint(port: 5002, name: "http")
    .WithEnvironment("AI__Provider", aiProvider)
    .WithEnvironment("AI__Model", aiModel)
    .WithEnvironment("AgentGateway__LlmEndpoint", agentgateway.GetEndpoint("llm"))
    .WithEnvironment("Services__AgentGateway__Gateway__0", agentgateway.GetEndpoint("gateway"))
    .WithEnvironment("Mcp__PathSuffix", "/mcp-from-checker")
    .WithEnvironment("Mcp__Scopes__0", "api://b228aac6-5596-4cd6-bf8e-8bc343613268/access_as_user")
    .WithEnvironment("AgentIdentity__AgentIdentityId", "139e29cb-f6e9-4771-ac42-1d89e554c9fe")
    .WithEnvironment("AzureAd__TenantId", tenantId)
    .WithEnvironment("AzureAd__ClientId", "40d39e9a-69f7-4bf2-b50e-c126c07fd2bd")
    .WithEnvironment("AzureAd__ClientCredentials__0__SourceType", "SignedAssertionFilePath")
    .WithEnvironment("AzureAd__ClientCredentials__0__SignedAssertionFileDiskPath", federatedTokenFile)
    .WithEnvironment("AZURE_FEDERATED_TOKEN_FILE", federatedTokenFile)
    .WithEnvironment("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT", "true")
    .WaitFor(agentgateway);

builder
    .AddProject<Projects.LoopRuntime_Executor>("executor")
    .WithHttpEndpoint(port: 5003, name: "http")
    .WithEnvironment("AI__Provider", aiProvider)
    .WithEnvironment("AI__Model", aiModel)
    .WithEnvironment("AgentGateway__LlmEndpoint", agentgateway.GetEndpoint("llm"))
    .WithEnvironment("Services__AgentGateway__Gateway__0", agentgateway.GetEndpoint("gateway"))
    .WithEnvironment("Mcp__PathSuffix", "/mcp-from-exec")
    .WithEnvironment("Mcp__Scopes__0", "api://b228aac6-5596-4cd6-bf8e-8bc343613268/access_as_user")
    .WithEnvironment("Checker__Scopes__0", "api://40d39e9a-69f7-4bf2-b50e-c126c07fd2bd/access_as_user")
    .WithEnvironment("AgentIdentity__AgentIdentityId", "6e1590ce-042e-49e8-bfd1-75104fa813a7")
    .WithEnvironment("AzureAd__TenantId", tenantId)
    .WithEnvironment("AzureAd__ClientId", "ba511c70-c2c8-4374-94d3-26742a4ede58")
    .WithEnvironment("AzureAd__ClientCredentials__0__SourceType", "SignedAssertionFilePath")
    .WithEnvironment("AzureAd__ClientCredentials__0__SignedAssertionFileDiskPath", federatedTokenFile)
    .WithEnvironment("AZURE_FEDERATED_TOKEN_FILE", federatedTokenFile)
    .WithEnvironment("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT", "true")
    .WaitFor(checker);

builder.Build().Run();
