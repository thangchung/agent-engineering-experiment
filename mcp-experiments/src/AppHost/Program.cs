using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Run OpenSandbox as an Aspire-managed container so local testing does not depend on docker-compose.
// Mount a Docker-mode config file to ensure OpenSandbox uses Docker runtime (not Kubernetes).
var configPath = Path.GetFullPath(
    Path.Combine(builder.AppHostDirectory, "..", "..", "deploy", "opensandbox.config.toml"));
var openSandbox = builder
       .AddContainer("opensandbox-server", "docker.io/opensandbox/server:v0.1.8", "v0.1.8")
       .WithEndpoint(port: 8080, targetPort: 8080, scheme: "http", name: "http", isExternal: true)
       .WithBindMount(configPath, "/etc/opensandbox/config.toml", isReadOnly: true)
       // Aspire's WithBindMount silently drops Unix socket files; pass the mount via raw runtime args.
       .WithContainerRuntimeArgs("-v", "/var/run/docker.sock:/var/run/docker.sock");

openSandbox = ConfigureOpenTelemetryForOpenSandbox(builder, openSandbox);

// Register MCP server and expose it on a stable local port for the tester UI.
var mcpServer = builder
       .AddProject("mcp-server", "../McpServer/McpServer.csproj")
       .WithEnvironment("Hosting__EnableCliMode", "false")
       // Default to LocalConstrainedRunner for reliable local development without external sandbox dependencies.
       // To use OpenSandbox instead, uncomment the lines below and ensure the opensandbox-server container is running:
       .WithEnvironment("CodeMode__Runner", "opensandbox")
       // Timeout hierarchy: Copilot send (120s) > CodeMode (90s) > ReadyTimeout (60s) + execution headroom.
       // CodeMode__TimeoutMs must exceed ReadyTimeoutSeconds so sandbox creation can finish.
       .WithEnvironment("CodeMode__TimeoutMs", "90000")
       .WithEnvironment("Copilot__SendTimeoutSeconds", "120")
       .WithEnvironment("OpenSandbox__Domain", "localhost:8080")
       .WithEnvironment("OpenSandbox__Image", "python:3.12-slim")
       .WithEnvironment("OpenSandbox__ReadyTimeoutSeconds", "60")
       .WithEnvironment("OpenSandbox__RequestTimeoutSeconds", "30")
       .WithEnvironment("OpenSandbox__ApiKey", string.Empty)
       .WaitFor(openSandbox);

// Register the Blazor tester and point it to the MCP server endpoint.
// WaitFor ensures test-web starts only after mcp-server is ready.
builder
       .AddProject("test-web", "../TestWeb/TestWeb.csproj")
       .WithEnvironment("Mcp__Endpoint", "http://localhost:5100/mcp")
       .WaitFor(mcpServer);

DistributedApplication app = builder.Build();
app.Run();

static IResourceBuilder<ContainerResource> ConfigureOpenTelemetryForOpenSandbox(
       IDistributedApplicationBuilder builder,
       IResourceBuilder<ContainerResource> resource)
{
       Dictionary<string, string> otelEnvironment = new(StringComparer.OrdinalIgnoreCase);

       AddIfPresent("OTEL_EXPORTER_OTLP_ENDPOINT");
       AddIfPresent("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT");
       AddIfPresent("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT");
       AddIfPresent("OTEL_EXPORTER_OTLP_PROTOCOL");
       AddIfPresent("OTEL_EXPORTER_OTLP_TRACES_PROTOCOL");
       AddIfPresent("OTEL_EXPORTER_OTLP_METRICS_PROTOCOL");
       AddIfPresent("OTEL_TRACES_SAMPLER");
       AddIfPresent("OTEL_TRACES_SAMPLER_ARG");
       AddIfPresent("OTEL_RESOURCE_ATTRIBUTES");

       // If OTLP endpoints are not explicitly set, reuse Aspire dashboard OTLP endpoint when available.
       // If both are absent, do not set OTEL exporter env vars so OpenSandbox can stay in no-export/noop mode.
       bool hasExplicitOtlpEndpoint = otelEnvironment.ContainsKey("OTEL_EXPORTER_OTLP_ENDPOINT")
              || otelEnvironment.ContainsKey("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT")
              || otelEnvironment.ContainsKey("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT");

       if (!hasExplicitOtlpEndpoint)
       {
              string? aspireOtlpEndpoint = builder.Configuration["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"];
              if (!string.IsNullOrWhiteSpace(aspireOtlpEndpoint))
              {
                     otelEnvironment["OTEL_EXPORTER_OTLP_ENDPOINT"] = RewriteLocalhostForContainer(aspireOtlpEndpoint);
              }
       }

       if (!otelEnvironment.ContainsKey("OTEL_SERVICE_NAME"))
       {
              otelEnvironment["OTEL_SERVICE_NAME"] = "opensandbox-server";
       }

       foreach ((string key, string value) in otelEnvironment)
       {
              resource = resource.WithEnvironment(key, value);
       }

       return resource;

       void AddIfPresent(string key)
       {
              string? value = builder.Configuration[key];
              if (string.IsNullOrWhiteSpace(value))
              {
                     return;
              }

              otelEnvironment[key] = IsOtlpEndpointVariable(key)
                     ? RewriteLocalhostForContainer(value)
                     : value;
       }
}

static bool IsOtlpEndpointVariable(string key)
{
       return key is "OTEL_EXPORTER_OTLP_ENDPOINT"
              or "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"
              or "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT";
}

static string RewriteLocalhostForContainer(string endpoint)
{
       if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri))
       {
              return endpoint;
       }

       bool isLocalHost = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
              || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
              || uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase);

       if (!isLocalHost)
       {
              return endpoint;
       }

       UriBuilder builder = new(uri)
       {
              Host = "host.docker.internal",
       };

       return builder.Uri.ToString().TrimEnd('/');
}
