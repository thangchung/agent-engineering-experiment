using Aspire.Hosting;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Run OpenSandbox as an Aspire-managed container so local testing does not depend on docker-compose.
// Mount a Docker-mode config file to ensure OpenSandbox uses Docker runtime (not Kubernetes).
// var configPath = Path.GetFullPath(
//     Path.Combine(builder.AppHostDirectory, "..", "..", "deploy", "opensandbox.config.toml"));
// var openSandbox = builder
//        .AddContainer("opensandbox-server", "docker.io/opensandbox/server:v0.1.8", "v0.1.8")
//        .WithEndpoint(port: 8080, targetPort: 8080, scheme: "http", name: "http", isExternal: true)
//        .WithBindMount(configPath, "/etc/opensandbox/config.toml", isReadOnly: true)
//        // Aspire's WithBindMount silently drops Unix socket files; pass the mount via raw runtime args.
//        .WithContainerRuntimeArgs("-v", "/var/run/docker.sock:/var/run/docker.sock");

// Register MCP server and expose it on a stable local port for the tester UI.
var mcpServer = builder
       .AddProject("mcp-server", "../McpServer/McpServer.csproj");
       // Default to LocalConstrainedRunner for reliable local development without external sandbox dependencies.
       // To use OpenSandbox instead, uncomment the lines below and ensure the opensandbox-server container is running:
       // .WithEnvironment("CodeMode__Runner", "opensandbox")
       // .WithEnvironment("CodeMode__TimeoutMs", "30000")
       // .WithEnvironment("OpenSandbox__Domain", "localhost:8080")
       // .WithEnvironment("OpenSandbox__ReadyTimeoutSeconds", "300")
       // .WithEnvironment("OpenSandbox__RequestTimeoutSeconds", "120")
       // .WithEnvironment("OpenSandbox__ApiKey", string.Empty)
       // .WaitFor(openSandbox)

// Register the Blazor tester and point it to the MCP server endpoint.
// WaitFor ensures test-web starts only after mcp-server is ready.
builder
       .AddProject("test-web", "../TestWeb/TestWeb.csproj")
       .WithEnvironment("Mcp__Endpoint", "http://localhost:5100/mcp")
       .WaitFor(mcpServer);

DistributedApplication app = builder.Build();
app.Run();
