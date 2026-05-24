using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

bool benchOnlyOpenSandbox = string.Equals(
    Environment.GetEnvironmentVariable("BENCH_ONLY_OPENSANDBOX"),
    "true",
    StringComparison.OrdinalIgnoreCase);

if (benchOnlyOpenSandbox)
{
    string configPath = Path.GetFullPath(
        Path.Combine(builder.AppHostDirectory, "..", "..", "deploy", "opensandbox.config.toml"));

    builder
        .AddContainer("opensandbox-server", "docker.io/opensandbox/server:v0.1.8", "v0.1.8")
        .WithEndpoint(port: 8080, targetPort: 8080, scheme: "http", name: "http", isExternal: true)
        .WithBindMount(configPath, "/etc/opensandbox/config.toml", isReadOnly: true)
        .WithContainerRuntimeArgs("-v", "/var/run/docker.sock:/var/run/docker.sock");
}
else
{
    string selectedRunner = NormalizeRunnerName(builder.Configuration["McpServer:CodeMode:Runner"]);

    IResourceBuilder<ProjectResource> mcpServer = builder
           .AddProject("mcp-server", "../McpServer/McpServer.csproj")
           .WithEnvironment("Hosting__EnableCliMode", "false")
           .WithEnvironment("CodeMode__Runner", selectedRunner)
           .WithEnvironment("CodeMode__TimeoutMs", "90000")
           .WithEnvironment("Copilot__SendTimeoutSeconds", "120");

    if (string.Equals(selectedRunner, "opensandbox", StringComparison.OrdinalIgnoreCase))
    {
           string configPath = Path.GetFullPath(
                  Path.Combine(builder.AppHostDirectory, "..", "..", "deploy", "opensandbox.config.toml"));

           IResourceBuilder<ContainerResource> openSandbox = builder
                  .AddContainer("opensandbox-server", "docker.io/opensandbox/server:v0.1.8", "v0.1.8")
                  .WithEndpoint(port: 8080, targetPort: 8080, scheme: "http", name: "http", isExternal: true)
                  .WithBindMount(configPath, "/etc/opensandbox/config.toml", isReadOnly: true)
                  .WithContainerRuntimeArgs("-v", "/var/run/docker.sock:/var/run/docker.sock");

           mcpServer = mcpServer
                  .WithEnvironment("OpenSandbox__Domain", "localhost:8080")
                  .WithEnvironment("OpenSandbox__Image", "python:3.12-slim")
                  .WithEnvironment("OpenSandbox__ReadyTimeoutSeconds", "60")
                  .WithEnvironment("OpenSandbox__RequestTimeoutSeconds", "30")
                  .WithEnvironment("OpenSandbox__ApiKey", string.Empty)
                  .WaitFor(openSandbox);
    }

    builder
           .AddProject("test-web", "../TestWeb/TestWeb.csproj")
           .WithEnvironment("Mcp__Endpoint", "http://localhost:5100/mcp")
           .WaitFor(mcpServer);
}

DistributedApplication app = builder.Build();
app.Run();

static string NormalizeRunnerName(string? configuredValue)
{
       string raw = configuredValue
              ?? Environment.GetEnvironmentVariable("MCP_CODEMODE_RUNNER")
              ?? "local";

       string normalized = raw.Trim().ToLowerInvariant();

       return normalized switch
       {
              "local" => "local",
              "opensandbox" => "opensandbox",
              "hyperlight" => "hyperlight",
              "hyperlight-sandbox" => "hyperlight",
              _ => "hyperlight",
       };
}
