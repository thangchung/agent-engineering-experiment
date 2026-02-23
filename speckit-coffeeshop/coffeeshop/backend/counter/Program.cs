using System.ComponentModel;
using System.Threading.Channels;
using CoffeeShop.Counter.Application.Agents;
using CoffeeShop.Counter.Application.Ports;
using CoffeeShop.Counter.Application.UseCases;
using CoffeeShop.Counter.Domain;
using CoffeeShop.Counter.Endpoints;
using CoffeeShop.Counter.Infrastructure;
using CoffeeShop.Counter.Workers;
using GitHub.Copilot.SDK;
using Microsoft.Agents.AI.GitHub.Copilot;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------
// Aspire service defaults (OTel, health checks, service discovery, resilience)
// -----------------------------------------------------------------------
builder.AddServiceDefaults();

// -----------------------------------------------------------------------
// In-memory stores (singletons — shared across all request/worker threads)
// -----------------------------------------------------------------------
builder.Services.AddSingleton<InMemoryCustomerStore>();
builder.Services.AddSingleton<InMemoryOrderStore>();

// -----------------------------------------------------------------------
// In-process channels
// All four channels use SingleWriter=false (multiple writers OK) and
// SingleReader=true (each worker has exactly one reader loop).
// -----------------------------------------------------------------------
// Named wrapper types for unambiguous DI injection of the two dispatch channels
// and two reply channels. Declared as top-level classes at end of this file.
builder.Services.AddSingleton<BaristaDispatchChannel>(_ =>
    new BaristaDispatchChannel(Channel.CreateUnbounded<OrderDispatch>(
        new UnboundedChannelOptions { SingleWriter = false, SingleReader = true })));

builder.Services.AddSingleton<KitchenDispatchChannel>(_ =>
    new KitchenDispatchChannel(Channel.CreateUnbounded<OrderDispatch>(
        new UnboundedChannelOptions { SingleWriter = false, SingleReader = true })));

builder.Services.AddSingleton<BaristaReplyChannel>(_ =>
    new BaristaReplyChannel(Channel.CreateUnbounded<WorkerResult>(
        new UnboundedChannelOptions { SingleWriter = false, SingleReader = true })));

builder.Services.AddSingleton<KitchenReplyChannel>(_ =>
    new KitchenReplyChannel(Channel.CreateUnbounded<WorkerResult>(
        new UnboundedChannelOptions { SingleWriter = false, SingleReader = true })));

// -----------------------------------------------------------------------
// HTTP client — product-catalog (MCP service via Aspire service discovery)
// Base address "http://product-catalog" resolved by Aspire service discovery.
// 5-second timeout enforced per FR-021 fail-fast requirement.
// -----------------------------------------------------------------------
builder.Services.AddHttpClient<McpProductCatalogClient>(client =>
{
    client.BaseAddress = new Uri("http://product-catalog");
    client.Timeout = TimeSpan.FromSeconds(5);
}).AddServiceDiscovery();

// Register interface → concrete type so use cases can depend on IProductCatalogClient
builder.Services.AddTransient<IProductCatalogClient>(
    sp => sp.GetRequiredService<McpProductCatalogClient>());

// -----------------------------------------------------------------------
// Application use cases
// -----------------------------------------------------------------------
builder.Services.AddTransient<LookupCustomer>();
builder.Services.AddTransient<GetMenu>();
builder.Services.AddTransient<PlaceOrder>();

// -----------------------------------------------------------------------
// Worker IHostedServices (in-process channel reader loops)
// IWorkerAgent stub — replaced with real MAF agent in full implementation
// -----------------------------------------------------------------------
builder.Services.AddSingleton<IWorkerAgent, NoOpWorkerAgent>();
builder.Services.AddHostedService<BaristaWorker>();
builder.Services.AddHostedService<KitchenWorker>();

// -----------------------------------------------------------------------
// OpenAPI
// -----------------------------------------------------------------------
builder.Services.AddOpenApi();

// -----------------------------------------------------------------------
// AG-UI support (MAF GitHub Copilot agent exposed via AG-UI protocol)
// -----------------------------------------------------------------------
builder.Services.AddAGUI();

// -----------------------------------------------------------------------
// Build
// -----------------------------------------------------------------------
var app = builder.Build();

// -----------------------------------------------------------------------
// Seed in-memory stores once the host is fully initialised
// -----------------------------------------------------------------------
app.Lifetime.ApplicationStarted.Register(() =>
{
    var customers = app.Services.GetRequiredService<InMemoryCustomerStore>();
    var orders = app.Services.GetRequiredService<InMemoryOrderStore>();
    SeedData.Load(customers, orders);

    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation(
        "Seed data loaded: {CustomerCount} customers, {OrderCount} orders.",
        SeedData.Customers.Count,
        SeedData.Orders.Count);
});

// -----------------------------------------------------------------------
// Middleware pipeline
// -----------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();  // /health, /alive, /healthz

// -----------------------------------------------------------------------
// Endpoint groups
// -----------------------------------------------------------------------
app.MapCustomerEndpoints();          // GET /api/v1/customers/lookup
app.MapMenuEndpoints();              // GET /api/v1/menu
app.MapOrderEndpoints();             // POST /api/v1/orders

// -----------------------------------------------------------------------
// GitHub Copilot Agent (AG-UI) — create agent AFTER DI container is built
// so tool lambdas can capture IServiceScopeFactory for per-request scopes.
// -----------------------------------------------------------------------
var scopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();

var copilotClient = new CopilotClient();
await copilotClient.StartAsync();

await using var counterAgent = new GitHubCopilotAgent(
    copilotClient,
    ownsClient: true,
    id: "CoffeeShopCounter",
    name: "CoffeeShopCounter",
    description: "Coffee shop counter agent",
    tools:
    [
        AIFunctionFactory.Create(
            async (
                [Description("Customer email, C-XXXX customer ID, or ORD-XXXX order ID")] string identifier,
                CancellationToken ct) =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var uc = scope.ServiceProvider.GetRequiredService<LookupCustomer>();
                var customer = await uc.ExecuteAsync(identifier, ct);
                return CustomerEndpoints.ToResponse(customer);
            },
            name: "lookup_customer",
            description: "Look up a customer by email, customer ID (C-XXXX), or order ID (ORD-XXXX)"),

        AIFunctionFactory.Create(
            async (CancellationToken ct) =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var uc = scope.ServiceProvider.GetRequiredService<GetMenu>();
                return await uc.ExecuteAsync(ct);
            },
            name: "get_menu",
            description: "Get all available menu items from the product catalog"),

        AIFunctionFactory.Create(
            async (
                [Description("Customer ID, e.g. C-1001")] string customerId,
                [Description("Items to order")] List<OrderItemRequest> items,
                [Description("Optional special notes for the order")] string? notes,
                CancellationToken ct) =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var uc = scope.ServiceProvider.GetRequiredService<PlaceOrder>();
                return await uc.ExecuteAsync(
                    new PlaceOrderRequest(customerId, items.AsReadOnly(), notes),
                    ct);
            },
            name: "place_order",
            description: "Place a new order for an identified customer"),
    ],
    instructions: CounterAgent.AgentInstructions);

// Wrap the raw GitHubCopilotAgent in a session-persisting decorator so that
// each AG-UI thread reuses a single GitHub Copilot CLI session across turns
// (MapAGUI always passes null as the AgentSession, which would otherwise cause
// GitHubCopilotAgent to call CopilotClient.CreateSessionAsync on every message).
var sessionAwareAgent = new SessionPersistingAgent(counterAgent);

app.MapAGUI("/api/v1/copilotkit", sessionAwareAgent);

await app.RunAsync();

// Make Program accessible from integration tests (Aspire testing)
public partial class Program { }
