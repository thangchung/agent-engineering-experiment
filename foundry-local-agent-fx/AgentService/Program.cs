using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using AgentService;
using Microsoft.Extensions.AI;
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
var mcpToolsUrl = app.Configuration["MCP_TOOLS"] ?? "http://localhost:5001";
var useHybridMode = app.Configuration.GetValue<bool>("USE_HYBRID_MODE", true);

var logger = app.Services.GetRequiredService<ILogger<Program>>();
var httpClientFactory = app.Services.GetRequiredService<IHttpClientFactory>();

// Chat endpoint - Native First, Fallback to Hybrid
app.MapPost("/chat", async (ChatRequest request) =>
{
    try
    {
        var response = await ProcessChatWithFallbackAsync(
            request.Message, 
            foundryEndpoint, 
            foundryModel, 
            mcpToolsUrl,
            httpClientFactory,
            logger,
            forceHybrid: useHybridMode);
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
    await context.Response.WriteAsync($"Mode: {(useHybridMode ? "Hybrid (forced)" : "Native first, fallback to Hybrid")}\n");
    
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

// ==================== Native First, Fallback to Hybrid ====================

/// <summary>
/// Process chat with native-first approach, falling back to hybrid if native tool calling doesn't work.
/// </summary>
static async Task<string> ProcessChatWithFallbackAsync(
    string userMessage,
    string foundryEndpoint,
    string foundryModel,
    string mcpToolsUrl,
    IHttpClientFactory httpClientFactory,
    ILogger logger,
    bool forceHybrid = false)
{
    // Start GenAI chat span
    var endpointUri = new Uri(foundryEndpoint);
    using var chatActivity = GenAITracing.StartChatSpan(
        model: foundryModel,
        provider: GenAITracing.Providers.FoundryLocal,
        serverAddress: endpointUri.Host,
        serverPort: endpointUri.Port);
    
    // Set input message (Opt-In sensitive data)
    GenAITracing.SetMessages(chatActivity, inputMessages: new[] 
    { 
        new { role = "user", content = userMessage } 
    });
    
    try
    {
        logger.LogInformation("Processing chat: '{Message}'", userMessage);
        logger.LogInformation("Connecting to Foundry Local at {Endpoint} with model {Model}", foundryEndpoint, foundryModel);
    
    using var httpClient = httpClientFactory.CreateClient();
    
    // Get available tools from MCP server
    logger.LogInformation("Fetching tools from MCP server at {McpUrl}", mcpToolsUrl);
    IList<McpClientTool> mcpTools;
    try
    {
        mcpTools = await GetMcpToolsAsync(mcpToolsUrl, httpClient);
        logger.LogInformation("Found {Count} tools: {Tools}", 
            mcpTools.Count, 
            string.Join(", ", mcpTools.Select(t => t.Name)));
        
        // Set tool definitions (Opt-In)
        GenAITracing.SetToolDefinitions(chatActivity, mcpTools.Select(t => new 
        { 
            type = "function",
            name = t.Name,
            description = t.Description
        }));
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to connect to MCP server, proceeding without tools");
        mcpTools = [];
    }
    
    // Convert MCP tools to AITool list for Microsoft.Extensions.AI
    var aiTools = mcpTools.Cast<AITool>().ToList();
    
    // Try native approach first (unless forced hybrid)
    if (!forceHybrid)
    {
        logger.LogInformation("Attempting native approach with automatic function invocation...");
        var nativeResult = await TryNativeApproachAsync(
            userMessage, foundryEndpoint, foundryModel, aiTools, logger);
        
        if (nativeResult.Success)
        {
            logger.LogInformation("Native approach succeeded!");
            
            // Set output message
            GenAITracing.SetMessages(chatActivity, outputMessages: new[] 
            { 
                new { role = "assistant", content = nativeResult.Response } 
            });
            GenAITracing.SetResponseAttributes(chatActivity, 
                responseModel: foundryModel,
                finishReason: "stop");
            
            return nativeResult.Response;
        }
        
        logger.LogWarning("Native approach did not process tool call, falling back to hybrid...");
    }
    else
    {
        logger.LogInformation("Hybrid mode forced by configuration");
    }
    
    // Fallback to hybrid approach
    logger.LogInformation("Using hybrid approach with ToolCallParser...");
    var result = await ProcessHybridAsync(
        userMessage, foundryEndpoint, foundryModel, mcpToolsUrl, 
        mcpTools, httpClient, chatActivity, logger);
    
    // Set output message
    GenAITracing.SetMessages(chatActivity, outputMessages: new[] 
    { 
        new { role = "assistant", content = result } 
    });
    GenAITracing.SetResponseAttributes(chatActivity, 
        responseModel: foundryModel,
        finishReason: "stop");
    
    return result;
    }
    catch (Exception ex)
    {
        GenAITracing.RecordError(chatActivity, ex);
        throw;
    }
}

/// <summary>
/// Try native Microsoft.Extensions.AI approach with automatic function invocation.
/// </summary>
static async Task<(bool Success, string Response)> TryNativeApproachAsync(
    string userMessage,
    string foundryEndpoint,
    string foundryModel,
    IList<AITool> tools,
    ILogger logger)
{
    try
    {
        // Create native chat client with function invocation
        var openAiClient = new OpenAIClient(
            new ApiKeyCredential("not-needed"),
            new OpenAIClientOptions { Endpoint = new Uri(foundryEndpoint) });

        var chatClient = openAiClient.GetChatClient(foundryModel);
        
        // Build IChatClient with function invocation pipeline
        var client = chatClient
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();
        
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(ChatRole.User, userMessage)
        };
        
        var options = new ChatOptions
        {
            Tools = tools
        };
        
        var response = await client.GetResponseAsync(messages, options);
        var content = response.Text ?? "";
        
        // Check if the response still contains unprocessed tool JSON
        // This indicates native tool calling didn't work
        var toolCalls = ToolCallParser.Parse(content);
        
        if (toolCalls.Count > 0 && toolCalls.Any(tc => tools.Any(t => t is AIFunction f && f.Name == tc.Name)))
        {
            // Native approach returned tool JSON instead of executing it
            logger.LogDebug("Response contains {Count} unprocessed tool call(s): {Tools}", 
                toolCalls.Count, 
                string.Join(", ", toolCalls.Select(tc => tc.Name)));
            return (false, content);
        }
        
        // Native approach worked - either no tool was needed, or tool was executed
        return (true, content);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Native approach failed with exception");
        return (false, "");
    }
}

/// <summary>
/// Hybrid approach using ToolCallParser for Foundry Local's JSON-in-content format.
/// </summary>
static async Task<string> ProcessHybridAsync(
    string userMessage,
    string foundryEndpoint,
    string foundryModel,
    string mcpToolsUrl,
    IList<McpClientTool> mcpTools,
    HttpClient httpClient,
    Activity? parentActivity,
    ILogger logger)
{
    // Create OpenAI client pointing to Foundry Local
    var openAiClient = new OpenAIClient(
        new ApiKeyCredential("no-need"),
        new OpenAIClientOptions { Endpoint = new Uri(foundryEndpoint) });
    
    var chatClient = openAiClient.GetChatClient(foundryModel);
    
    // Build system prompt with tool definitions
    var toolDefs = BuildToolDefsFromMcp(mcpTools);
    var jsonExample = @"{""name"": ""tool_name"", ""arguments"": {""param"": ""value""}}";
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

    var messages = new List<OpenAI.Chat.ChatMessage>
    {
        new SystemChatMessage(systemPrompt),
        new UserChatMessage(userMessage)
    };

    logger.LogInformation("[Hybrid] Sending request to model...");
    
    // First request
    var response = await chatClient.CompleteChatAsync(messages);
    var content = response.Value.Content.FirstOrDefault()?.Text ?? "";
    
    logger.LogInformation("[Hybrid] Model response: {Content}", content);
    
    // Try to parse as tool calls (Foundry Local format - JSON in content)
    var toolCalls = ToolCallParser.Parse(content);
    
    // Filter to only valid tool calls that match available tools
    var validToolCalls = toolCalls
        .Where(tc => mcpTools.Any(t => t.Name == tc.Name))
        .ToList();
    
    if (validToolCalls.Count > 0)
    {
        logger.LogInformation("[Hybrid] Detected {Count} tool call(s): {Tools}", 
            validToolCalls.Count, 
            string.Join(", ", validToolCalls.Select(tc => tc.Name)));
        
        // Execute all tools in parallel with tracing
        var toolTasks = validToolCalls.Select(async toolCall =>
        {
            // Create tool execution span as child of chat span
            using var toolActivity = GenAITracing.StartToolSpan(
                toolName: toolCall.Name,
                toolCallId: Guid.NewGuid().ToString("N")[..12],
                arguments: toolCall.Arguments);
            
            logger.LogInformation("[Hybrid] Executing tool: {Tool}({Args})", 
                toolCall.Name, 
                JsonSerializer.Serialize(toolCall.Arguments));
            
            try
            {
                var result = await CallMcpToolAsync(mcpToolsUrl, httpClient, toolCall.Name, toolCall.Arguments);
                
                // Record tool result
                GenAITracing.SetToolResult(toolActivity, result);
                
                logger.LogInformation("[Hybrid] Tool '{Tool}' result: {Result}", toolCall.Name, result);
                
                return new { ToolName = toolCall.Name, Result = result, Success = true };
            }
            catch (Exception ex)
            {
                GenAITracing.RecordError(toolActivity, ex);
                logger.LogError(ex, "[Hybrid] Tool '{Tool}' failed", toolCall.Name);
                return new { ToolName = toolCall.Name, Result = $"Error: {ex.Message}", Success = false };
            }
        });
        
        var toolResults = await Task.WhenAll(toolTasks);
        
        // Build combined results for final response
        var resultsText = string.Join("\n\n", toolResults.Select(r => 
            $"Tool '{r.ToolName}' returned:\n{r.Result}"));
        
        // Send results back to model for final response
        var finalPrompt = $"""
            User asked: "{userMessage}"
            
            I called {validToolCalls.Count} tool(s) and got these results:
            
            {resultsText}
            
            Please provide a natural, helpful response to the user based on this information.
            """;
        
        var finalMessages = new List<OpenAI.Chat.ChatMessage>
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
        if (tool.JsonSchema is { } schema)
        {
            sb.AppendLine($"  Parameters: {JsonSerializer.Serialize(schema)}");
        }
    }
    return sb.ToString();
}

// Request/Response models
record ChatRequest(string Message);
record ChatResponse(string Response);

// Make Program accessible for Aspire
public partial class Program { }
