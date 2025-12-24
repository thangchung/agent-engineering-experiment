using System.ClientModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI;
using OpenAI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace AgentService;

/// <summary>
/// Custom AIAgent implementation for Foundry Local that handles non-standard tool calling.
/// Foundry Local's small models return tool calls as JSON in content, not in tool_calls field.
/// This agent wraps the hybrid approach with ToolCallParser to provide standard AIAgent interface.
/// </summary>
public class FoundryLocalAgent : AIAgent
{
    private readonly string _foundryEndpoint;
    private readonly string _model;
    private readonly string? _instructions;
    private readonly string? _name;
    private readonly string? _description;
    private readonly string? _id;
    private readonly IList<McpClientTool> _mcpTools;
    private readonly HttpClient _httpClient;
    private readonly string _mcpToolsUrl;
    private readonly ILogger? _logger;

    public FoundryLocalAgent(
        string foundryEndpoint,
        string model,
        string mcpToolsUrl,
        IList<McpClientTool> mcpTools,
        HttpClient httpClient,
        string? instructions = null,
        string? name = null,
        string? description = null,
        ILogger? logger = null)
    {
        _foundryEndpoint = foundryEndpoint;
        _model = model;
        _mcpToolsUrl = mcpToolsUrl;
        _mcpTools = mcpTools;
        _httpClient = httpClient;
        _instructions = instructions;
        _logger = logger;

        _name = name ?? "FoundryLocalAgent";
        _description = description ?? "AI Agent powered by Foundry Local with MCP tools";
        _id = Guid.NewGuid().ToString();
    }

    protected override string? IdCore => _id;
    public override string? Name => _name;
    public override string? Description => _description;

    public override AgentThread GetNewThread() => new FoundryLocalAgentThread();

    public override AgentThread DeserializeThread(JsonElement serializedThread, JsonSerializerOptions? jsonSerializerOptions = null)
    {
        return new FoundryLocalAgentThread(serializedThread, jsonSerializerOptions);
    }

    public override async Task<AgentRunResponse> RunAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var agentThread = thread as FoundryLocalAgentThread ?? new FoundryLocalAgentThread();
        
        // Add incoming messages to thread's message store
        foreach (var msg in messages)
        {
            agentThread.MessageStore.Add(msg);
        }

        // Get all messages from the thread for context
        var allMessages = agentThread.MessageStore.ToList();

        // Start tracing
        var endpointUri = new Uri(_foundryEndpoint);
        using var chatActivity = GenAITracing.StartChatSpan(
            model: _model,
            provider: GenAITracing.Providers.FoundryLocal,
            serverAddress: endpointUri.Host,
            serverPort: endpointUri.Port);

        GenAITracing.SetMessages(chatActivity, 
            inputMessages: allMessages.Select(m => new { role = m.Role.Value, content = m.Text }));
        GenAITracing.SetToolDefinitions(chatActivity, 
            _mcpTools.Select(t => new { type = "function", name = t.Name, description = t.Description }));

        try
        {
            var result = await ProcessWithToolsAsync(allMessages, chatActivity, cancellationToken);
            
            var responseMessage = new ChatMessage(ChatRole.Assistant, result);
            agentThread.MessageStore.Add(responseMessage);

            GenAITracing.SetMessages(chatActivity, 
                outputMessages: new[] { new { role = "assistant", content = result } });
            GenAITracing.SetResponseAttributes(chatActivity, responseModel: _model, finishReason: "stop");

            return new AgentRunResponse(responseMessage);
        }
        catch (Exception ex)
        {
            GenAITracing.RecordError(chatActivity, ex);
            throw;
        }
    }

    public override async IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // For now, implement streaming as a single response
        // Foundry Local streaming with tool parsing is complex
        var response = await RunAsync(messages, thread, options, cancellationToken);
        
        foreach (var msg in response.Messages)
        {
            yield return new AgentRunResponseUpdate(msg.Role, msg.Text);
        }
    }

    private async Task<string> ProcessWithToolsAsync(
        IList<ChatMessage> chatMessages,
        Activity? parentActivity,
        CancellationToken cancellationToken)
    {
        var openAiClient = new OpenAIClient(
            new ApiKeyCredential("not-needed"),
            new OpenAIClientOptions { Endpoint = new Uri(_foundryEndpoint) });

        var chatClient = openAiClient.GetChatClient(_model);

        // Build system prompt with tool definitions
        var toolDefs = BuildToolDefs();
        var jsonExample = @"{""name"": ""tool_name"", ""arguments"": {""param"": ""value""}}";
        
        var systemPrompt = _mcpTools.Count > 0
            ? $"""
                {_instructions ?? "You are a helpful assistant with access to tools."}
                
                When you need to use a tool, respond ONLY with a JSON object in this exact format:
                {jsonExample}

                Available tools:
                {toolDefs}
                
                If you don't need a tool, respond normally with text.
                """
            : _instructions ?? "You are a helpful assistant.";

        // Convert messages to OpenAI format
        var openAiMessages = new List<OpenAI.Chat.ChatMessage>
        {
            new SystemChatMessage(systemPrompt)
        };

        foreach (var msg in chatMessages)
        {
            if (msg.Role == ChatRole.User)
                openAiMessages.Add(new UserChatMessage(msg.Text ?? ""));
            else if (msg.Role == ChatRole.Assistant)
                openAiMessages.Add(new AssistantChatMessage(msg.Text ?? ""));
        }

        _logger?.LogDebug("[FoundryLocalAgent] Sending request to model...");

        var response = await chatClient.CompleteChatAsync(openAiMessages, cancellationToken: cancellationToken);
        var content = response.Value.Content.FirstOrDefault()?.Text ?? "";

        _logger?.LogDebug("[FoundryLocalAgent] Model response: {Content}", content);

        // Parse tool calls from content (Foundry Local format)
        var toolCalls = ToolCallParser.Parse(content);
        var validToolCalls = toolCalls
            .Where(tc => _mcpTools.Any(t => t.Name == tc.Name))
            .ToList();

        if (validToolCalls.Count == 0)
        {
            // No tool calls, return the response as-is
            return content;
        }

        _logger?.LogInformation("[FoundryLocalAgent] Executing {Count} tool(s): {Tools}",
            validToolCalls.Count,
            string.Join(", ", validToolCalls.Select(tc => tc.Name)));

        // Execute tools in parallel
        var toolTasks = validToolCalls.Select(async toolCall =>
        {
            using var toolActivity = GenAITracing.StartToolSpan(
                toolName: toolCall.Name,
                toolCallId: Guid.NewGuid().ToString("N")[..12],
                arguments: toolCall.Arguments);

            try
            {
                var result = await CallMcpToolAsync(toolCall.Name, toolCall.Arguments, cancellationToken);
                GenAITracing.SetToolResult(toolActivity, result);
                return new { toolCall.Name, Result = result ?? "", Error = (string?)null };
            }
            catch (Exception ex)
            {
                GenAITracing.RecordError(toolActivity, ex);
                return new { toolCall.Name, Result = "", Error = (string?)ex.Message };
            }
        });

        var toolResults = await Task.WhenAll(toolTasks);

        // Build tool results message
        var toolResultsText = string.Join("\n\n", toolResults.Select(r =>
            r.Error is null
                ? $"Tool '{r.Name}' result:\n{r.Result}"
                : $"Tool '{r.Name}' error: {r.Error}"));

        // Add tool results to conversation and get final response
        openAiMessages.Add(new AssistantChatMessage(content));
        openAiMessages.Add(new UserChatMessage($"Tool execution results:\n{toolResultsText}\n\nPlease provide your final response based on these results."));

        var finalResponse = await chatClient.CompleteChatAsync(openAiMessages, cancellationToken: cancellationToken);
        return finalResponse.Value.Content.FirstOrDefault()?.Text ?? toolResultsText;
    }

    private string BuildToolDefs()
    {
        return string.Join("\n\n", _mcpTools.Select(tool =>
        {
            var paramsDesc = tool.JsonSchema.TryGetProperty("properties", out var props)
                ? string.Join(", ", props.EnumerateObject().Select(p => $"{p.Name}: {GetTypeDescription(p.Value)}"))
                : "no parameters";

            return $"- {tool.Name}: {tool.Description}\n  Parameters: {paramsDesc}";
        }));
    }

    private static string GetTypeDescription(JsonElement element)
    {
        if (element.TryGetProperty("type", out var typeEl))
            return typeEl.GetString() ?? "any";
        return "any";
    }

    private async Task<string> CallMcpToolAsync(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken)
    {
        // Use proper MCP SDK transport instead of raw HTTP POST
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(_mcpToolsUrl)
        }, _httpClient, ownsHttpClient: false);
        
        await using var mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        
        // Convert JsonElement arguments to object?
        var convertedArgs = arguments.ToDictionary(
            kvp => kvp.Key,
            kvp => ConvertJsonElement(kvp.Value));
        
        var result = await mcpClient.CallToolAsync(toolName, convertedArgs, cancellationToken: cancellationToken);
        
        // Extract text content from result
        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        return textContent?.Text ?? "Tool returned no content";
    }

    private static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.GetRawText()
    };
}

/// <summary>
/// Thread implementation for FoundryLocalAgent.
/// Stores conversation messages in memory with a unique thread ID.
/// </summary>
public class FoundryLocalAgentThread : InMemoryAgentThread
{
    /// <summary>
    /// Gets the unique identifier for this thread.
    /// </summary>
    public string ThreadId { get; }

    public FoundryLocalAgentThread() : base() 
    { 
        ThreadId = Guid.NewGuid().ToString("N");
    }

    public FoundryLocalAgentThread(JsonElement serializedThreadState, JsonSerializerOptions? jsonSerializerOptions = null)
        : base(serializedThreadState, jsonSerializerOptions) 
    {
        // Try to read ThreadId from serialized state, otherwise generate new one
        if (serializedThreadState.TryGetProperty("threadId", out var threadIdEl) && 
            threadIdEl.ValueKind == JsonValueKind.String)
        {
            ThreadId = threadIdEl.GetString() ?? Guid.NewGuid().ToString("N");
        }
        else
        {
            ThreadId = Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>
    /// Serializes the thread state including the ThreadId.
    /// </summary>
    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        var baseState = base.Serialize(jsonSerializerOptions);
        
        // Merge ThreadId into the serialized state
        using var doc = JsonDocument.Parse(baseState.GetRawText());
        var dict = new Dictionary<string, object?>
        {
            ["threadId"] = ThreadId
        };
        
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.Clone();
        }
        
        return JsonSerializer.SerializeToElement(dict);
    }
}

/// <summary>
/// Extension methods to create FoundryLocalAgent easily.
/// </summary>
public static class FoundryLocalAgentExtensions
{
    /// <summary>
    /// Creates a FoundryLocalAgent from configuration.
    /// </summary>
    public static FoundryLocalAgent CreateFoundryLocalAgent(
        this IServiceProvider services,
        string foundryEndpoint,
        string model,
        string mcpToolsUrl,
        IList<McpClientTool> mcpTools,
        string? instructions = null,
        string? name = null,
        string? description = null)
    {
        var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
        var logger = services.GetService<ILogger<FoundryLocalAgent>>();

        return new FoundryLocalAgent(
            foundryEndpoint,
            model,
            mcpToolsUrl,
            mcpTools,
            httpClientFactory.CreateClient(),
            instructions,
            name,
            description,
            logger);
    }
}
