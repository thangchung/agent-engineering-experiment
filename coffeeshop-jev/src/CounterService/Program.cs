using CounterService.Common;
using CounterService.Features.Menu;
using CounterService.Features.Orders;
using CounterService.Features.Orders.Agents;
using CounterService.Features.Orders.Common;
using Jev.Client;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddHttpClient("catalog", client =>
{
    client.BaseAddress = new Uri("http://catalog");
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<CatalogClient>();
builder.Services.AddSingleton<ICatalogClient>(sp => sp.GetRequiredService<CatalogClient>());

builder.Services.AddOpenAiChatClient();
builder.Services.AddOrderAgents();

builder.Services.AddJevClient(
    builder.Configuration["services:openjev:http:0"] ?? builder.Configuration["Jev:BaseUrl"] ?? "http://openjev",
    options =>
    {
        options.ApiKey = builder.Configuration["Jev:ApiKey"];
        options.CaptureContent = builder.Environment.IsDevelopment();
    });

builder.Services.AddSingleton<RunRegistry>();
builder.Services.AddSingleton<OrderStore>();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGetMenu();
app.MapPlaceOrder();
app.MapAnswerClarification();
app.MapListOrders();

app.Run();

public partial class Program;
