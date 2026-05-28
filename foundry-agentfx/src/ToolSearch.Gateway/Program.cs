using Azure.Core;
using Azure.Identity;
using ToolSearch.Gateway;
using ToolSearch.Gateway.Registry;
using ToolSearch.Gateway.Search;
using ToolSearch.Gateway.ToolSearch;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton<TokenCredential>(new DefaultAzureCredential());

builder.Services.AddHttpClient("coffeeshop-mcp", client =>
{
    var coffeeshopUrl = builder.Configuration["Services:CoffeeshopMcp:Url"]
        ?? "http://localhost:5001";
    client.BaseAddress = new Uri(coffeeshopUrl);
    // MCP Streamable HTTP transport requires both content types in Accept
    client.DefaultRequestHeaders.Add("Accept", "application/json, text/event-stream");
});

builder.Services.AddHttpClient("foundry-iq", client =>
{
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

builder.Services.AddHttpClient("foundry-toolbox", client =>
{
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("Foundry-Features", "Toolboxes=V1Preview");
});

builder.Services.AddSingleton<CoffeeshopMcpSession>();

builder.Services.AddSingleton<IToolRegistry>(sp =>
{
    var coffeeshopSession = sp.GetRequiredService<CoffeeshopMcpSession>();
    var config = sp.GetRequiredService<IConfiguration>();
    var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
    var credential = sp.GetRequiredService<TokenCredential>();

    var tools = new List<ToolDescriptor>();
    tools.AddRange(CoffeeshopBackendRegistrar.Build(coffeeshopSession));
    tools.AddRange(FoundryBackendRegistrar.Build(config, httpFactory, credential));

    return new ToolRegistry(tools);
});
builder.Services.AddSingleton<IToolSearcher, WeightedToolSearcher>();
builder.Services.AddSingleton<MetaTools>();
builder.Services.AddSingleton(new UserContext());

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapMcp("/mcp");

// REST API for internal service-to-service communication
app.MapPost("/api/search-tools", async (
    SearchToolsRequest req,
    MetaTools metaTools,
    UserContext context) =>
{
    var results = metaTools.SearchTools(req.Query, req.Limit, context);
    return Results.Ok(results);
});

app.MapPost("/api/call-tool", async (
    CallToolRequest req,
    MetaTools metaTools,
    UserContext context,
    CancellationToken ct) =>
{
    var result = await metaTools.CallToolAsync(req.Name, req.Arguments, context, ct);
    return Results.Ok(result);
});

await app.RunAsync();

record SearchToolsRequest(string Query, int Limit = 10);
record CallToolRequest(string Name, System.Text.Json.JsonElement Arguments);
