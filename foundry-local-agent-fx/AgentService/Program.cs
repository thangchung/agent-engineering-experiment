using AgentService;
using AgentService.Providers;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Add OpenAPI
builder.Services.AddOpenApi();

// Add HttpClient for MCP
builder.Services.AddHttpClient();

var app = builder.Build();
app.MapDefaultEndpoints();

// OpenAPI and Scalar API documentation
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.WithTitle("Multi-Provider Agent API")
           .WithTheme(ScalarTheme.DeepSpace);
});

// =============================================================================
// Configuration
// =============================================================================
var agentProvider = app.Configuration["AGENT_PROVIDER"] ?? "Ollama";
var foundryEndpoint = app.Configuration["FOUNDRY_ENDPOINT"] ?? "http://127.0.0.1:55930/v1";
var foundryModel = app.Configuration["FOUNDRY_MODEL"] ?? "qwen2.5-14b-instruct-generic-cpu:4";
var ollamaEndpoint = app.Configuration["OLLAMA_ENDPOINT"] ?? "http://localhost:11434/";
var ollamaModel = app.Configuration["OLLAMA_MODEL"] ?? "llama3.2:3b";
var mcpToolsUrl = app.Configuration["MCP_TOOLS"] ?? "http://localhost:5001";
var instructions = app.Configuration["Agent:Instructions"] 
    ?? "You are a helpful assistant with access to tools for weather and other information.";

var logger = app.Services.GetRequiredService<ILogger<Program>>();
var httpClientFactory = app.Services.GetRequiredService<IHttpClientFactory>();

// Parse provider type
var providerType = AgentProviderFactory.ParseProviderType(agentProvider);
logger.LogInformation("Using agent provider: {Provider}", providerType);

// Initialize MCP client and tools - keep client alive for app lifetime
// McpClientTool holds reference to session, so we must not dispose until shutdown
McpClient? mcpClient = null;
IList<McpClientTool> mcpTools = [];
try
{
    var transport = new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri($"{mcpToolsUrl}/mcp")
    }, httpClientFactory.CreateClient(), ownsHttpClient: false);
    
    mcpClient = await McpClient.CreateAsync(transport);
    mcpTools = await mcpClient.ListToolsAsync();
    
    logger.LogInformation("Loaded {Count} MCP tools: {Tools}", 
        mcpTools.Count, 
        string.Join(", ", mcpTools.Select(t => t.Name)));
}
catch (Exception ex)
{
    logger.LogWarning(ex, "Failed to load MCP tools at startup. Chat will work without tools.");
}

// Register MCP client disposal on app shutdown
app.Lifetime.ApplicationStopping.Register(() =>
{
    if (mcpClient is not null)
    {
        logger.LogInformation("Disposing MCP client...");
        mcpClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
});

// =============================================================================
// Create the Agent using Factory
// =============================================================================
var providerConfig = providerType switch
{
    AgentProviderType.Ollama => new AgentProviderConfig(ollamaEndpoint, ollamaModel, $"{mcpToolsUrl}/mcp"),
    AgentProviderType.FoundryLocal => new AgentProviderConfig(foundryEndpoint, foundryModel, $"{mcpToolsUrl}/mcp"),
    _ => new AgentProviderConfig(foundryEndpoint, foundryModel, $"{mcpToolsUrl}/mcp")
};

var agent = AgentProviderFactory.Create(
    services: app.Services,
    providerType: providerType,
    config: providerConfig,
    mcpTools: mcpTools,
    instructions: instructions,
    name: $"{providerType}Agent",
    description: $"AI Agent powered by {providerType} with MCP tools");

logger.LogInformation("Agent created: {Name} using {Provider} at {Endpoint}", 
    agent.Name, providerType, providerConfig.Endpoint);

// Store threads in memory (in production, use a proper store)
var threads = new Dictionary<string, AgentThread>();
var threadIds = new Dictionary<AgentThread, string>();

// Helper to get thread ID from AgentThread
string GetThreadId(AgentThread thread)
{
    if (threadIds.TryGetValue(thread, out var existingId))
        return existingId;
    
    // For custom thread types, extract ID if available
    var id = thread switch
    {
        FoundryLocalAgentThread foundry => foundry.ThreadId,
        _ => Guid.NewGuid().ToString("N")
    };
    
    threadIds[thread] = id;
    return id;
}

// Chat endpoint using Agent
app.MapPost("/chat", async (ChatRequest request) =>
{
    try
    {
        logger.LogInformation("Processing chat: '{Message}'", request.Message);
        
        // Get or create thread
        AgentThread agentThread;
        string threadId;
        
        if (!string.IsNullOrEmpty(request.ThreadId) && threads.TryGetValue(request.ThreadId, out var existingThread))
        {
            agentThread = existingThread;
            threadId = request.ThreadId;
            logger.LogDebug("Using existing thread: {ThreadId}", threadId);
        }
        else
        {
            agentThread = agent.GetNewThread();
            threadId = GetThreadId(agentThread);
            threads[threadId] = agentThread;
            logger.LogDebug("Created new thread: {ThreadId}", threadId);
        }
        
        // Run the agent
        var messages = new[] { new ChatMessage(ChatRole.User, request.Message) };
        var response = await agent.RunAsync(messages, agentThread);
        
        // Get the assistant's response
        var responseText = response.Messages.LastOrDefault()?.Text ?? "No response generated.";
        
        return Results.Ok(new ChatResponse(responseText, threadId));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing chat request");
        return Results.Problem(ex.Message);
    }
});

// Interactive console mode (for testing)
app.MapGet("/interactive", async (HttpContext context) =>
{
    context.Response.ContentType = "text/plain";
    await context.Response.WriteAsync("Use POST /chat endpoint with JSON body: {\"message\": \"your question\", \"threadId\": \"optional-thread-id\"}\n");
    await context.Response.WriteAsync($"Provider: {providerType}\n");
    await context.Response.WriteAsync($"Endpoint: {providerConfig.Endpoint}\n");
    await context.Response.WriteAsync($"Model: {providerConfig.Model}\n");
    await context.Response.WriteAsync($"MCP Tools Server: {mcpToolsUrl}\n");
    await context.Response.WriteAsync($"Agent: {agent.Name}\n");
    await context.Response.WriteAsync($"Active threads: {threads.Count}\n");
    await context.Response.WriteAsync($"Available tools: {string.Join(", ", mcpTools.Select(t => t.Name))}\n");
});

// List threads endpoint
app.MapGet("/threads", () => Results.Ok(threads.Keys.ToList()));

// Clear thread endpoint
app.MapDelete("/threads/{threadId}", (string threadId) =>
{
    if (threads.Remove(threadId))
        return Results.Ok(new { message = $"Thread {threadId} deleted" });
    return Results.NotFound(new { message = $"Thread {threadId} not found" });
});

app.Run();

// Request/Response models
record ChatRequest(string Message, string? ThreadId = null);
record ChatResponse(string Response, string ThreadId);

// Make Program accessible for Aspire
public partial class Program { }
