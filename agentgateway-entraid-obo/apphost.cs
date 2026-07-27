#:sdk Aspire.AppHost.Sdk@13.4.6+87fe259e4fc244c599019a7b1304c85a1488f248

using System.Runtime.CompilerServices;

var builder = DistributedApplication.CreateBuilder(args);

// Fixed ports: gateway.yaml is a static bind-mounted file referencing host.docker.internal:<port>
// and cannot follow Aspire's dynamic service-discovery ports.
// ASPNETCORE_ENVIRONMENT: no launchSettings.json means ASP.NET Core defaults to Production
// when unset, silently disabling every IsDevelopment()-gated branch (/health, diagnostics).
var todomcpserver = builder.AddProject("todomcpserver", "src/AgenticTodo.TodoMcpServer/AgenticTodo.TodoMcpServer.csproj")
    .WithHttpEndpoint(port: 5003)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");
var todoagent = builder.AddProject("todoagent", "src/AgenticTodo.TodoAgent/AgenticTodo.TodoAgent.csproj")
    .WithHttpEndpoint(port: 5002)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");
var todoapi = builder.AddProject("todoapi", "src/AgenticTodo.TodoApi/AgenticTodo.TodoApi.csproj")
    .WithHttpEndpoint(port: 5001)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

var dotEnv = LoadDotEnv();

// tenant-id falls back to "common", not a fake GUID: agentgateway fetches JWKS eagerly at
// startup for every jwtAuth/mcpAuthentication policy, and an unresolvable tenant fails the
// WHOLE gateway process, not just one route.
var tenantId = builder.AddParameter("tenant-id", Env(dotEnv, "TENANT_ID", "common"), publishValueAsDefault: true);
var todoApiClientId = builder.AddParameter("todoapi-client-id", Env(dotEnv, "TODOAPI_CLIENT_ID", "00000000-0000-0000-0000-000000000001"), publishValueAsDefault: true);
var todoApiClientSecret = builder.AddParameter("todoapi-client-secret", Env(dotEnv, "TODOAPI_CLIENT_SECRET", "placeholder-secret"), secret: true);
// .env.local's key is TODOAGENT_BLUEPRINT_CLIENT_ID, not TODOAGENT_CLIENT_ID.
var todoAgentClientId = builder.AddParameter("todoagent-client-id", Env(dotEnv, "TODOAGENT_BLUEPRINT_CLIENT_ID", "00000000-0000-0000-0000-000000000002"), publishValueAsDefault: true);
var todoAgentClientSecret = builder.AddParameter("todoagent-client-secret", Env(dotEnv, "TODOAGENT_CLIENT_SECRET", "placeholder-secret"), secret: true);
var todoAgentAgentIdentityId = builder.AddParameter("todoagent-agent-identity-id", Env(dotEnv, "TODOAGENT_AGENT_IDENTITY_ID", "00000000-0000-0000-0000-000000000000"), publishValueAsDefault: true);
var todoMcpServerClientId = builder.AddParameter("todomcpserver-client-id", Env(dotEnv, "TODOMCPSERVER_CLIENT_ID", "00000000-0000-0000-0000-000000000003"), publishValueAsDefault: true);
var azureOpenAiResourceName = builder.AddParameter("azure-openai-resource-name", Env(dotEnv, "AZURE_OPENAI_RESOURCE_NAME", "placeholder-resource"), publishValueAsDefault: true);
var azureOpenAiProjectName = builder.AddParameter("azure-openai-project-name", Env(dotEnv, "AZURE_OPENAI_PROJECT_NAME", "placeholder-project"), publishValueAsDefault: true);
var azureOpenAiDeployment = builder.AddParameter("azure-openai-deployment", Env(dotEnv, "AZURE_OPENAI_DEPLOYMENT", "gpt-4o-mini"), publishValueAsDefault: true);
var azureOpenAiApiKey = builder.AddParameter("azure-openai-api-key", Env(dotEnv, "AZURE_OPENAI_API_KEY", "placeholder-key"), secret: true);

// NormalUserGroupId isn't wired anywhere -- the check is "is SuperAdminGroupId present",
// not "is any known group present", so there's nothing for it to drive (prd §8.4).
var superAdminGroupId = builder.AddParameter("superadmin-group-id", Env(dotEnv, "SUPERADMIN_GROUP_ID", "00000000-0000-0000-0000-000000000004"), publishValueAsDefault: true);

var gateway = builder.AddContainer("agentgateway", "cr.agentgateway.dev/agentgateway", "v1.4.0-beta.1")
    .WithBindMount("./gateway.yaml", "/app/gateway.yaml")
    .WithBindMount("./data", "/data")
    .WithArgs("-f", "/app/gateway.yaml")
    // Fixed host port: Entra needs a STABLE Scalar OAuth redirect URI -- a random port would
    // mean re-registering it in Entra on every `aspire run`.
    .WithHttpEndpoint(port: 3000, targetPort: 3000)
    .WithHttpEndpoint(port: 16000, targetPort: 15000, name: "admin")
    .WithHttpEndpoint(port: 4000, targetPort: 4000, name: "llm")
    .WithEnvironment("TENANT_ID", tenantId)
    .WithEnvironment("TODOAPI_CLIENT_ID", todoApiClientId)
    .WithEnvironment("TODOAPI_CLIENT_SECRET", todoApiClientSecret)
    .WithEnvironment("TODOAGENT_CLIENT_ID", todoAgentClientId)
    .WithEnvironment("TODOMCPSERVER_CLIENT_ID", todoMcpServerClientId)
    .WithEnvironment("AZURE_OPENAI_RESOURCE_NAME", azureOpenAiResourceName)
    .WithEnvironment("AZURE_OPENAI_PROJECT_NAME", azureOpenAiProjectName)
    .WithEnvironment("AZURE_OPENAI_DEPLOYMENT", azureOpenAiDeployment)
    .WithEnvironment("AZURE_OPENAI_API_KEY", azureOpenAiApiKey)
    .WaitFor(todoapi)
    .WaitFor(todoagent)
    .WaitFor(todomcpserver);

todomcpserver
    .WithEnvironment("AzureAd__TenantId", tenantId)
    .WithEnvironment("AzureAd__ClientId", todoMcpServerClientId)
    .WithEnvironment("Authorization__SuperAdminGroupId", superAdminGroupId);

todoagent
    .WithEnvironment("AzureAd__TenantId", tenantId)
    .WithEnvironment("AzureAd__ClientId", todoAgentClientId)
    .WithEnvironment("AzureAd__ClientCredentials__0__ClientSecret", todoAgentClientSecret)
    .WithEnvironment("AgentIdentity__AgentIdentityId", todoAgentAgentIdentityId)
    .WithEnvironment("Mcp__ClientId", todoMcpServerClientId)
    .WithEnvironment("Mcp__Endpoint", $"{gateway.GetEndpoint("http")}/mcp")
    // Must match gateway.yaml llm.models[].name exactly, not the raw Azure deployment name (see gateway.yaml).
    .WithEnvironment("AI__Model", "agentic-todo")
    .WithEnvironment("AgentGateway__LlmEndpoint", gateway.GetEndpoint("llm"))
    .WithEnvironment("Authorization__SuperAdminGroupId", superAdminGroupId);

todoapi
    .WithEnvironment("AzureAd__TenantId", tenantId)
    .WithEnvironment("AzureAd__ClientId", todoApiClientId)
    .WithEnvironment("Agent__ClientId", todoAgentClientId)
    .WithEnvironment("Agent__GatewayBaseUrl", gateway.GetEndpoint("http"))
    .WithEnvironment("Authorization__SuperAdminGroupId", superAdminGroupId);

builder.Build().Run();

// [CallerFilePath] locates .env.local/.env next to THIS file -- file-based apps run from a
// temp build dir, so CWD/AppContext.BaseDirectory don't work. .env.local is the committed
// placeholder template; .env (git-ignored, real values) loads after it and wins on conflict.
static Dictionary<string, string> LoadDotEnv([CallerFilePath] string sourceFile = "")
{
    var directory = Path.GetDirectoryName(sourceFile)!;
    var values = new Dictionary<string, string>();

    foreach (var fileName in new[] { ".env.local", ".env" })
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) continue;

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex < 0) continue;
            values[trimmed[..separatorIndex]] = trimmed[(separatorIndex + 1)..];
        }
    }

    return values;
}

// .env/.env.local value -> process env var of the same name -> fallback placeholder.
static string Env(Dictionary<string, string> dotEnv, string key, string fallback) =>
    dotEnv.TryGetValue(key, out var fromFile) && !string.IsNullOrEmpty(fromFile) ? fromFile
    : Environment.GetEnvironmentVariable(key) is { Length: > 0 } fromProcessEnv ? fromProcessEnv
    : fallback;
