using McpServer;
using McpServer.CodeMode;
using McpServer.OpenApi;
using McpServer.Registry;
using McpServer.Search;
using McpServer.Tools;

// Streamable-HTTP MCP server. Connect via MCP Inspector or any HTTP-based MCP client:
//   Transport: Streamable HTTP
//   URL:       http://localhost:5100/mcp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// OpenAPI-generated handlers use the default HTTP client and resolve the base URL
// from OpenAPI servers or configuration override.
builder.Services.AddHttpClient();

string openApiSpecPath = OpenApiToolCatalogBuilder.ResolveSpecPath(builder.Configuration, AppContext.BaseDirectory);
string? openApiBaseUrlOverride = builder.Configuration["OpenApi:BaseUrl"];

// Keep status pinned and load external API tools from OpenAPI instead of hard-coding.
List<ToolDescriptor> tools =
[
    new ToolDescriptor(
        Name: "status",
        Description: "Returns MCP server health and timestamp.",
        InputJsonSchema: """{"type":"object","properties":{}}""",
        Tags: ["system"],
        IsPinned: true,
        IsSynthetic: false,
        IsVisible: _ => true,
        Handler: (_, _) => Task.FromResult<object?>(new { ok = true, timestamp = DateTimeOffset.UtcNow })),
];

tools.AddRange(OpenApiToolCatalogBuilder.BuildTools(openApiSpecPath, openApiBaseUrlOverride));

builder.Services.AddSingleton<IToolRegistry>(new ToolRegistry(tools));
builder.Services.AddSingleton<IToolSearcher, WeightedToolSearcher>();
builder.Services.AddSingleton<MetaTools>();
builder.Services.AddSingleton<DiscoveryTools>();
// LocalConstrainedRunner: 5-second timeout and 10 tool-call limit per execute code block.
// Replaced by OpenSandboxRunner in Phase 3.
builder.Services.AddSingleton<ISandboxRunner>(sp => new LocalConstrainedRunner(TimeSpan.FromSeconds(5), maxToolCalls: 10, sp.GetRequiredService<ILogger<LocalConstrainedRunner>>()));
builder.Services.AddSingleton<ExecuteTool>();
builder.Services.AddSingleton<WorkflowCoordinator>();
// Default anonymous context — replace with per-request auth resolution in production.
builder.Services.AddSingleton(new UserContext());

// WithHttpTransport uses the MCP streamable-HTTP protocol (not legacy SSE).
// WithToolsFromAssembly discovers all [McpServerToolType] classes in this assembly.
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

// Expose the root IServiceProvider so tool handlers can resolve services
// (e.g. OpenBreweryDbClient). Handlers are registered before Build() so they cannot
// receive the provider via constructor injection; this static accessor bridges that gap.
ToolServiceProvider.Root = app.Services;

// /mcp is the default endpoint URL the MCP Inspector UI expects.
app.MapMcp("/mcp");

await app.RunAsync();
