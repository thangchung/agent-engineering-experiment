using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using SemanticCache.Api.Features.Cache;
using SemanticCache.Api.Features.Chat;
using SemanticCache.Api.Features.Metrics;
using SemanticCache.Api.Features.Users;
using SemanticCache.Api.Infrastructure.Database;
using SemanticCache.Api.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (telemetry, health checks, etc.)
builder.AddServiceDefaults();

// Add PostgreSQL with EF Core and snake_case naming
builder.AddNpgsqlDbContext<AppDbContext>("semanticcachedb", configureDbContextOptions: options =>
{
    options.UseNpgsql(npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(3);
    }).UseSnakeCaseNamingConvention();
});

// Add Redis distributed caching and HybridCache (L1 in-memory + L2 Redis)
builder.AddRedisDistributedCache("redis");
#pragma warning disable EXTEXP0018 // Type is for evaluation purposes only
builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(30),
        LocalCacheExpiration = TimeSpan.FromMinutes(5)
    };
});
#pragma warning restore EXTEXP0018

// Configure Azure OpenAI
var azureOpenAIEndpoint = builder.Configuration["AzureOpenAI:Endpoint"] 
    ?? throw new InvalidOperationException("AzureOpenAI:Endpoint configuration is required");
var azureOpenAIKey = builder.Configuration["AzureOpenAI:ApiKey"];

AzureOpenAIClient openAIClient;
if (!string.IsNullOrEmpty(azureOpenAIKey))
{
    openAIClient = new AzureOpenAIClient(new Uri(azureOpenAIEndpoint), new Azure.AzureKeyCredential(azureOpenAIKey));
}
else
{
    openAIClient = new AzureOpenAIClient(new Uri(azureOpenAIEndpoint), new DefaultAzureCredential());
}
builder.Services.AddSingleton(openAIClient);

// Register application services
builder.Services.AddScoped<VectorSimilarityService>();
builder.Services.AddScoped<SemanticCacheService>();
builder.Services.AddScoped<AgentMemoryService>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<DatabaseSeeder>();

// Add OpenAPI with Scalar UI
builder.Services.AddOpenApi();

var app = builder.Build();

// Seed database in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync();
}

// Configure the HTTP request pipeline
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "Semantic Cache API";
        options.Theme = ScalarTheme.Purple;
    });
}

app.UseHttpsRedirection();

// Map feature endpoints
app.MapChatEndpoints();
app.MapCacheEndpoints();
app.MapUserEndpoints();
app.MapMetricsEndpoints();

app.Run();
