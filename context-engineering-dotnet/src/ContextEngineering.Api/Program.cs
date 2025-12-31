using Azure.AI.OpenAI;
using ContextEngineering.Core.Interfaces;
using ContextEngineering.Infrastructure;
using ContextEngineering.Infrastructure.Data;
using ContextEngineering.Infrastructure.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Scalar.AspNetCore;

// Use aliases to avoid ambiguity
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using DomainChatMessage = ContextEngineering.Core.Entities.ChatMessage;
using ScratchpadCategory = ContextEngineering.Core.Entities.ScratchpadCategory;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (includes OpenTelemetry)
builder.AddServiceDefaults();

// Add PostgreSQL with EF Core (Aspire integration)
builder.AddNpgsqlDbContext<AppDbContext>("contextdb", configureDbContextOptions: options =>
{
    options.UseNpgsql(npgsql => npgsql.MigrationsAssembly("ContextEngineering.Infrastructure"))
           .UseSnakeCaseNamingConvention();
});

// Add infrastructure services (repositories, plugins)
builder.Services.AddInfrastructure();

// Configure Azure OpenAI with OpenTelemetry instrumentation
var azureOpenAiEndpoint = builder.Configuration["AzureOpenAI:Endpoint"] ?? "";
var azureOpenAiKey = builder.Configuration["AzureOpenAI:ApiKey"] ?? "";
var azureOpenAiDeployment = builder.Configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o";

if (!string.IsNullOrEmpty(azureOpenAiEndpoint) && !string.IsNullOrEmpty(azureOpenAiKey))
{
    builder.Services.AddSingleton<IChatClient>(sp =>
    {
        var client = new AzureOpenAIClient(
            new Uri(azureOpenAiEndpoint),
            new System.ClientModel.ApiKeyCredential(azureOpenAiKey));
        
        // Build IChatClient with OpenTelemetry instrumentation
        return client.GetChatClient(azureOpenAiDeployment)
            .AsIChatClient()
            .AsBuilder()
            .UseOpenTelemetry(
                sourceName: "ContextEngineering",
                configure: cfg => cfg.EnableSensitiveData = builder.Environment.IsDevelopment())
            .Build();
    });
}
else
{
    // Fallback: register a placeholder that throws if used
    builder.Services.AddSingleton<IChatClient>(sp =>
        throw new InvalidOperationException("Azure OpenAI is not configured. Set AzureOpenAI:Endpoint and AzureOpenAI:ApiKey in configuration."));
}

// Add OpenAPI
builder.Services.AddOpenApi();

var app = builder.Build();

// Apply migrations on startup (development only)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Context Engineering API")
               .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.UseHttpsRedirection();
app.MapDefaultEndpoints();

// === Chat Endpoints ===

app.MapPost("/api/chat", async (
    ChatRequest request,
    IChatClient chatClient,
    IConversationRepository conversationRepository,
    ScratchpadPlugin scratchpadPlugin,
    ITokenCounter tokenCounter,
    CancellationToken ct) =>
{
    // Get or create conversation
    var thread = await conversationRepository.GetByUserIdAsync(request.UserId, ct)
                 ?? await conversationRepository.CreateAsync(request.UserId, ct);

    // Get message history
    var storedMessages = await conversationRepository.GetMessagesAsync(thread.Id, ct);

    // Get tools from scratchpad plugin
    var scratchpadTools = scratchpadPlugin.GetTools().ToList();

    // Build system prompt
    var systemPrompt = $"""
        You are a helpful AI assistant with access to a scratchpad for storing user preferences and completed tasks.
        The current user ID is: {request.UserId}
        
        Before responding to new conversations, use the read_scratchpad tool to check for any stored context about the user.
        When the user expresses preferences or completes tasks, use the update_scratchpad tool to save that information.
        
        Categories for scratchpad:
        - preferences: User preferences, communication style, interests
        - tasks: Completed tasks, achievements, progress updates
        """;

    // Build message history
    var messages = new List<AiChatMessage> { new(ChatRole.System, systemPrompt) };

    foreach (var msg in storedMessages)
    {
        messages.Add(new AiChatMessage(
            msg.Role == "user" ? ChatRole.User : ChatRole.Assistant,
            msg.Content));
    }

    // Add new user message
    messages.Add(new AiChatMessage(ChatRole.User, request.Message));

    // Configure chat options with plugin tools
    var chatOptions = new ChatOptions
    {
        Tools = [.. scratchpadTools]
    };

    // Get response from LLM with automatic tool invocation
    var response = await chatClient.GetResponseAsync(messages, chatOptions, ct);
    var responseText = response.Text ?? "I apologize, but I couldn't generate a response.";

    // Count tokens
    var userTokens = tokenCounter.CountTokens(request.Message);
    var assistantTokens = tokenCounter.CountTokens(responseText);

    // Save user message
    await conversationRepository.AddMessageAsync(thread.Id, new DomainChatMessage
    {
        Role = "user",
        Content = request.Message,
        TokenCount = userTokens
    }, ct);

    // Save assistant message
    await conversationRepository.AddMessageAsync(thread.Id, new DomainChatMessage
    {
        Role = "assistant",
        Content = responseText,
        TokenCount = assistantTokens
    }, ct);

    // Record token usage
    await conversationRepository.RecordTokenUsageAsync(thread.Id, userTokens, assistantTokens, false, ct);

    return Results.Ok(new ChatResponse(
        ThreadId: thread.Id,
        Message: responseText,
        TokensUsed: userTokens + assistantTokens,
        WasReduced: false));
})
.WithName("Chat")
.WithSummary("Send a chat message and get a response")
.WithDescription("Processes a chat message using Azure OpenAI with scratchpad context and chat history reduction.");

app.MapGet("/api/scratchpad/{userId}", async (
    string userId,
    IScratchpadRepository scratchpadRepository,
    CancellationToken ct) =>
{
    var scratchpads = await scratchpadRepository.GetAllByUserIdAsync(userId, ct);
    
    if (scratchpads.Count == 0)
    {
        return Results.Ok(new { UserId = userId, Content = "" });
    }
    
    var content = scratchpads
        .Select(s => $"[{s.Category.ToUpperInvariant()}]\n{s.Content}")
        .Aggregate((a, b) => $"{a}\n\n{b}");
    
    return Results.Ok(new { UserId = userId, Content = content });
})
.WithName("GetScratchpad")
.WithSummary("Get all scratchpad entries for a user");

app.MapPut("/api/scratchpad/{userId}/{category}", async (
    string userId,
    string category,
    UpdateScratchpadRequest request,
    IScratchpadRepository scratchpadRepository,
    CancellationToken ct) =>
{
    if (category != ScratchpadCategory.Preferences && category != ScratchpadCategory.Tasks)
    {
        return Results.BadRequest($"Invalid category. Use '{ScratchpadCategory.Preferences}' or '{ScratchpadCategory.Tasks}'.");
    }

    var scratchpad = await scratchpadRepository.UpsertAsync(userId, category, request.Content, ct);
    return Results.Ok(new { UserId = userId, Category = category, Message = $"Updated at {scratchpad.UpdatedAt:u}" });
})
.WithName("UpdateScratchpad")
.WithSummary("Update a scratchpad entry for a user");

app.MapGet("/api/conversations/{userId}", async (
    string userId,
    IConversationRepository conversationRepository,
    CancellationToken ct) =>
{
    var thread = await conversationRepository.GetByUserIdAsync(userId, ct);
    
    if (thread is null)
    {
        return Results.NotFound();
    }
    
    return Results.Ok(new ConversationSummary
    {
        ThreadId = thread.Id,
        UserId = thread.UserId,
        MessageCount = thread.MessageCount,
        TotalTokensUsed = thread.TotalTokensUsed,
        ReductionEventCount = thread.ReductionEventCount,
        CreatedAt = thread.CreatedAt
    });
})
.WithName("GetConversation")
.WithSummary("Get conversation summary for a user");

app.MapPost("/api/conversations/{userId}/new", async (
    string userId,
    IConversationRepository conversationRepository,
    CancellationToken ct) =>
{
    var thread = await conversationRepository.CreateAsync(userId, ct);
    return Results.Created($"/api/conversations/{userId}", new { ThreadId = thread.Id });
})
.WithName("CreateConversation")
.WithSummary("Start a new conversation thread");

app.Run();

// === Request/Response Records ===

record ChatRequest(string UserId, string Message);
record ChatResponse(Guid ThreadId, string Message, int TokensUsed, bool WasReduced);
record UpdateScratchpadRequest(string Content);

record ConversationSummary
{
    public Guid ThreadId { get; init; }
    public string? UserId { get; init; }
    public int MessageCount { get; init; }
    public int TotalTokensUsed { get; init; }
    public int ReductionEventCount { get; init; }
    public DateTime CreatedAt { get; init; }
}
