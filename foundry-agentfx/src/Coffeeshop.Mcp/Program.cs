using Coffeeshop.Mcp.Services;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddScoped<IMenuService, InMemoryMenuService>();
builder.Services.AddScoped<ICustomerService, InMemoryCustomerService>();
builder.Services.AddScoped<IAuditService, OrderAuditService>();
builder.Services.AddScoped<IOrderService, InMemoryOrderService>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapMcp("/mcp");

await app.RunAsync();
