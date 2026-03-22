using Aspire.Hosting;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Register MCP server and expose it on a stable local port for the tester UI.
var mcpServer = builder
       .AddProject("mcp-server", "../McpServer/McpServer.csproj");

// Register the Blazor tester and point it to the MCP server endpoint.
// WaitFor ensures test-web starts only after mcp-server is ready.
builder
       .AddProject("test-web", "../TestWeb/TestWeb.csproj")
       .WithEnvironment("Mcp__Endpoint", "http://localhost:5100/mcp")
       .WaitFor(mcpServer);

DistributedApplication app = builder.Build();
app.Run();
