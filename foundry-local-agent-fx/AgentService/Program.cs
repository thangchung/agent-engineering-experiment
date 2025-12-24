using AgentService;
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
    options.WithTitle("Foundry Local Agent API")
           .WithTheme(ScalarTheme.DeepSpace);
});

// Configuration
var foundryEndpoint = app.Configuration["FOUNDRY_ENDPOINT"] ?? "http://127.0.0.1:55930/v1";
var foundryModel = app.Configuration["FOUNDRY_MODEL"] ?? "qwen2.5-14b-instruct-generic-cpu:4";
var mcpToolsUrl = app.Configuration["MCP_TOOLS"] ?? "http://localhost:5001";

var logger = app.Services.GetRequiredService<ILogger<Program>>();
var httpClientFactory = app.Services.GetRequiredService<IHttpClientFactory>();

// Initialize MCP tools once at startup
IList<McpClientTool> mcpTools = [];
try
{
    using var httpClient = httpClientFactory.CreateClient();
    mcpTools = await GetMcpToolsAsync(mcpToolsUrl, httpClient);
    logger.LogInformation("Loaded {Count} MCP tools: {Tools}", 
        mcpTools.Count, 
        string.Join(", ", mcpTools.Select(t => t.Name)));
}
catch (Exception ex)
{
    logger.LogWarning(ex, "Failed to load MCP tools at startup. Chat will work without tools.");
}

// Create the FoundryLocalAgent
var agent = app.Services.CreateFoundryLocalAgent(
    foundryEndpoint: foundryEndpoint,
    model: foundryModel,
    mcpToolsUrl: $"{mcpToolsUrl}/mcp",
    mcpTools: mcpTools,
    instructions: "You are a helpful assistant with access to tools for weather and other information.",
    name: "FoundryLocalAgent",
    description: "AI Agent powered by Foundry Local with MCP tools");

// Store threads in memory (in production, use a proper store)
var threads = new Dictionary<string, FoundryLocalAgentThread>();

// Chat endpoint using FoundryLocalAgent
app.MapPost("/chat", async (ChatRequest request) =>
{
    try
    {
        logger.LogInformation("Processing chat: '{Message}'", request.Message);
        
        // Get or create thread
        FoundryLocalAgentThread agentThread;
        if (!string.IsNullOrEmpty(request.ThreadId) && threads.TryGetValue(request.ThreadId, out var existingThread))
        {
            agentThread = existingThread;
            logger.LogDebug("Using existing thread: {ThreadId}", request.ThreadId);
        }
        else
        {
            agentThread = (FoundryLocalAgentThread)agent.GetNewThread();
            threads[agentThread.ThreadId] = agentThread;
            logger.LogDebug("Created new thread: {ThreadId}", agentThread.ThreadId);
        }
        
        // Run the agent
        var messages = new[] { new ChatMessage(ChatRole.User, request.Message) };
        var response = await agent.RunAsync(messages, agentThread);
        
        // Get the assistant's response
        var responseText = response.Messages.LastOrDefault()?.Text ?? "No response generated.";
        
        return Results.Ok(new ChatResponse(responseText, agentThread.ThreadId));
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
    await context.Response.WriteAsync($"Foundry Endpoint: {foundryEndpoint}\n");
    await context.Response.WriteAsync($"Model: {foundryModel}\n");
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

// ==================== Helper Functions ====================

// Get tools from MCP server
static async Task<IList<McpClientTool>> GetMcpToolsAsync(string mcpToolsUrl, HttpClient httpClient)
{
    var transport = new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri($"{mcpToolsUrl}/mcp")
    }, httpClient, ownsHttpClient: false);
    
    await using var client = await McpClient.CreateAsync(transport);
    return await client.ListToolsAsync();
}

// Request/Response models
record ChatRequest(string Message, string? ThreadId = null);
record ChatResponse(string Response, string ThreadId);

// Make Program accessible for Aspire
public partial class Program { }
