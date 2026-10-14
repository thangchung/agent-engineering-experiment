using AGUI.Server;
using CounterService.Common;
using CounterService.Features.Menu;
using CounterService.Features.Orders;
using CounterService.Features.Orders.Agents;
using CounterService.Features.Orders.Common;
using CounterService.Features.Orders.Workflow;
using DecisionModel.Client;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
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

var profile = DecisionProfile.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(profile);
builder.Services.AddDecisionModelClient(
    profile.NormalizeBaseUrl(builder.Configuration["DecisionModel:BaseUrl"] ?? builder.Configuration["services:decision-model:http:0"] ?? "http://decision-model"),
    options =>
    {
        options.ApiKey = builder.Configuration["DecisionModel:ApiKey"];
        options.Model = profile.Model;
        options.Path = profile.Path;
        options.ApiKeyHeader = profile.ApiKeyHeader;
        options.ProviderName = profile.Name;
        if (profile.EntraWhenNoKey && string.IsNullOrEmpty(options.ApiKey))
        {
            var credential = new Azure.Identity.DefaultAzureCredential();
            options.TokenProvider = async ct => (await credential.GetTokenAsync(new Azure.Core.TokenRequestContext(["https://cognitiveservices.azure.com/.default"]), ct).ConfigureAwait(false)).Token;
        }
        options.CaptureContent = builder.Environment.IsDevelopment();
    });

builder.Services.AddSingleton<RunRegistry>();
builder.Services.AddSingleton<OrderStore>();
builder.Services.AddAGUIServer();
builder.Services.AddSingleton(sp => new CoffeeShopAgent(
    sp.GetRequiredService<DecisionModelClient>(),
    sp.GetRequiredService<ICatalogClient>(),
    sp.GetRequiredKeyedService<AIAgent>(AgentKeys.Counter),
    sp.GetRequiredKeyedService<AIAgent>(AgentKeys.Barista),
    sp.GetRequiredKeyedService<AIAgent>(AgentKeys.Kitchen),
    sp.GetRequiredService<OrderStore>(),
    sp.GetRequiredService<DecisionProfile>(),
    sp.GetRequiredService<ILogger<CoffeeShopAgent>>()));

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}


app.MapGetMenu();
app.MapPlaceOrder();
app.MapAnswerClarification();
app.MapListOrders();
app.MapAGUIServer("/agui", app.Services.GetRequiredService<CoffeeShopAgent>())
    .WithMetadata(new AGUIStreamOptions().MapResultAsStateSnapshot(CoffeeShopAgent.ProgressTool));

app.Run();

public partial class Program;
