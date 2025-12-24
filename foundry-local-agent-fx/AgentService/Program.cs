using System.ClientModel;
using System.Text.Json;
using AgentService;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI;
using OpenAI.Chat;
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
var mcpToolsUrl = app.Configuration["services__mcp-tools__http__0"] ?? "http://localhost:5001";

var logger = app.Services.GetRequiredService<ILogger<Program>>();
var httpClientFactory = app.Services.GetRequiredService<IHttpClientFactory>();

// Chat endpoint
app.MapPost("/chat", async (ChatRequest request) =>
{
    try
    {
        var response = await ProcessChatAsync(
            request.Message, 
            foundryEndpoint, 
            foundryModel, 
            mcpToolsUrl,
            httpClientFactory,
            logger);
        return Results.Ok(new ChatResponse(response));
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
    await context.Response.WriteAsync("Use POST /chat endpoint with JSON body: {\"message\": \"your question\"}\n");
    await context.Response.WriteAsync($"Foundry Endpoint: {foundryEndpoint}\n");
    await context.Response.WriteAsync($"Model: {foundryModel}\n");
    await context.Response.WriteAsync($"MCP Tools Server: {mcpToolsUrl}\n");
    
    // List available tools from MCP server
    try
    {
        using var httpClient = httpClientFactory.CreateClient();
        var tools = await GetMcpToolsAsync(mcpToolsUrl, httpClient);
        await context.Response.WriteAsync($"Available tools: {string.Join(", ", tools.Select(t => t.Name))}\n");
    }
    catch (Exception ex)
    {
        await context.Response.WriteAsync($"Error connecting to MCP server: {ex.Message}\n");
    }
});

app.Run();

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

// Call MCP tool
static async Task<string> CallMcpToolAsync(
    string mcpToolsUrl, 
    HttpClient httpClient,
    string toolName, 
    Dictionary<string, JsonElement> arguments)
{
    var transport = new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri($"{mcpToolsUrl}/mcp")
    }, httpClient, ownsHttpClient: false);
    
    await using var client = await McpClient.CreateAsync(transport);
    
    // Convert JsonElement arguments to object?
    var convertedArgs = arguments.ToDictionary(
        kvp => kvp.Key,
        kvp => ConvertJsonElement(kvp.Value));
    
    var result = await client.CallToolAsync(toolName, convertedArgs);
    
    // Extract text content from result
    var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
    return textContent?.Text ?? "Tool returned no content";
}

// Convert JsonElement to object?
static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch
{
    JsonValueKind.String => element.GetString(),
    JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
    JsonValueKind.True => true,
    JsonValueKind.False => false,
    JsonValueKind.Null => null,
    _ => element.GetRawText()
};

// Build tool definitions for LLM prompt from MCP tools
static string BuildToolDefsFromMcp(IList<McpClientTool> tools)
{
    var sb = new System.Text.StringBuilder();
    foreach (var tool in tools)
    {
        sb.AppendLine($"- {tool.Name}: {tool.Description}");
        // McpClientTool inherits from AIFunction, use JsonSchema
        if (tool.JsonSchema is { } schema)
        {
            sb.AppendLine($"  Parameters: {JsonSerializer.Serialize(schema)}");
        }
    }
    return sb.ToString();
}

// Chat processing logic
static async Task<string> ProcessChatAsync(
    string userMessage,
    string foundryEndpoint,
    string foundryModel,
    string mcpToolsUrl,
    IHttpClientFactory httpClientFactory,
    ILogger logger)
{
    logger.LogInformation("Connecting to Foundry Local at {Endpoint} with model {Model}", foundryEndpoint, foundryModel);
    
    using var httpClient = httpClientFactory.CreateClient();
    
    // Get available tools from MCP server
    logger.LogInformation("Fetching tools from MCP server at {McpUrl}", mcpToolsUrl);
    IList<McpClientTool> mcpTools;
    try
    {
        mcpTools = await GetMcpToolsAsync(mcpToolsUrl, httpClient);
        logger.LogInformation("Found {Count} tools: {Tools}", mcpTools.Count, string.Join(", ", mcpTools.Select(t => t.Name)));
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to connect to MCP server, proceeding without tools");
        mcpTools = [];
    }
    
    // Create OpenAI client pointing to Foundry Local
    var openAiClient = new OpenAIClient(
        new ApiKeyCredential("no-need"),
        new OpenAIClientOptions { Endpoint = new Uri(foundryEndpoint) });
    
    var chatClient = openAiClient.GetChatClient(foundryModel);
    
    // Build system prompt with tool definitions
    var toolDefs = BuildToolDefsFromMcp(mcpTools);
    var jsonExample = """{"name": "tool_name", "arguments": {"param": "value"}}""";
    var systemPrompt = mcpTools.Count > 0 
        ? $"""
            You are a helpful assistant with access to tools.
            
            When you need to use a tool, respond ONLY with a JSON object in this exact format:
            {jsonExample}
            
            Available tools:
            {toolDefs}
            
            If you don't need a tool, respond normally with text.
            """
        : "You are a helpful assistant.";

    var messages = new List<ChatMessage>
    {
        new SystemChatMessage(systemPrompt),
        new UserChatMessage(userMessage)
    };

    logger.LogInformation("Sending request to model...");
    
    // First request
    var response = await chatClient.CompleteChatAsync(messages);
    var content = response.Value.Content.FirstOrDefault()?.Text ?? "";
    
    logger.LogInformation("Model response: {Content}", content);
    
    // Try to parse as tool call (Foundry Local format - JSON in content)
    var toolCall = ToolCallParser.Parse(content);
    
    if (toolCall is not null && mcpTools.Any(t => t.Name == toolCall.Name))
    {
        logger.LogInformation("Detected tool call: {Tool}({Args})", 
            toolCall.Name, 
            JsonSerializer.Serialize(toolCall.Arguments));
        
        // Execute the tool via MCP
        var toolResult = await CallMcpToolAsync(mcpToolsUrl, httpClient, toolCall.Name, toolCall.Arguments);
        
        logger.LogInformation("Tool result: {Result}", toolResult);
        
        // Send result back to model for final response
        var finalPrompt = $"""
            User asked: "{userMessage}"
            
            I called the {toolCall.Name} tool and got this result:
            {toolResult}
            
            Please provide a natural, helpful response to the user based on this information.
            """;
        
        var finalMessages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a helpful assistant. Provide a natural response based on the tool results."),
            new UserChatMessage(finalPrompt)
        };
        
        var finalResponse = await chatClient.CompleteChatAsync(finalMessages);
        return finalResponse.Value.Content.FirstOrDefault()?.Text ?? "I couldn't generate a response.";
    }
    
    // No tool call detected, return direct response
    return content;
}

// Request/Response models
record ChatRequest(string Message);
record ChatResponse(string Response);

// Make Program accessible for Aspire
public partial class Program { }
