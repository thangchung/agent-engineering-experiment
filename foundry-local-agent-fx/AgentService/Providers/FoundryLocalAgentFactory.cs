using System.ClientModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace AgentService.Providers;

/// <summary>
/// Factory for creating Foundry Local-based AI agents with OpenTelemetry instrumentation.
/// Uses a hybrid approach: OpenTelemetryChatClient for auto-instrumentation + manual tool parsing
/// for models that don't support native function calling (tool_calls).
/// </summary>
public static class FoundryLocalAgentFactory
{
    /// <summary>
    /// Source name for OpenTelemetry traces.
    /// </summary>
    public const string OtelSourceName = "AgentService.FoundryLocal";

    /// <summary>
    /// Creates a FoundryLocalAgent with full OpenTelemetry instrumentation.
    /// </summary>
    /// <param name="endpoint">Foundry Local endpoint (e.g., http://127.0.0.1:55930/v1)</param>
    /// <param name="model">Model name</param>
    /// <param name="mcpToolsUrl">MCP tools server URL</param>
    /// <param name="mcpTools">MCP tools to register</param>
    /// <param name="httpClient">HTTP client for MCP calls</param>
    /// <param name="instructions">System instructions</param>
    /// <param name="name">Agent name</param>
    /// <param name="description">Agent description</param>
    /// <param name="loggerFactory">Logger factory for OTel and logging</param>
    /// <param name="enableSensitiveData">Enable capture of message content (default: false)</param>
    /// <returns>A FoundryLocalAgent with OpenTelemetry instrumentation</returns>
    /// <remarks>
    /// <para>
    /// This agent uses a hybrid approach because Foundry Local's small models
    /// return tool calls as JSON in content, not in the standard tool_calls field.
    /// </para>
    /// <para>
    /// OpenTelemetry instrumentation follows GenAI Semantic Conventions v1.38:
    /// - Chat spans: "chat {model}" with gen_ai.* attributes
    /// - Tool spans: "execute_tool {tool.name}" (manual, for parsed tool calls)
    /// - Metrics: gen_ai.client.operation.duration, gen_ai.client.token.usage
    /// </para>
    /// </remarks>
    public static InstrumentedFoundryLocalAgent Create(
        string endpoint,
        string model,
        string mcpToolsUrl,
        IList<McpClientTool> mcpTools,
        HttpClient httpClient,
        string? instructions = null,
        string? name = null,
        string? description = null,
        ILoggerFactory? loggerFactory = null,
        bool? enableSensitiveData = null)
    {
        // Create OpenAI-compatible client for Foundry Local
        var openAiClient = new OpenAIClient(
            new ApiKeyCredential("not-needed"),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

        // Get IChatClient from OpenAI client
        IChatClient baseChatClient = openAiClient.GetChatClient(model).AsIChatClient();

        // Wrap with OpenTelemetry instrumentation
        // This provides: gen_ai.* spans and metrics automatically
        IChatClient instrumentedClient = new ChatClientBuilder(baseChatClient)
            .UseOpenTelemetry(
                loggerFactory: loggerFactory,
                sourceName: OtelSourceName,
                configure: otel =>
                {
                    if (enableSensitiveData.HasValue)
                    {
                        otel.EnableSensitiveData = enableSensitiveData.Value;
                    }
                })
            .Build();

        return new InstrumentedFoundryLocalAgent(
            instrumentedClient,
            model,
            mcpToolsUrl,
            mcpTools,
            httpClient,
            instructions,
            name,
            description,
            loggerFactory?.CreateLogger<InstrumentedFoundryLocalAgent>());
    }
}

/// <summary>
/// Foundry Local agent with built-in OpenTelemetry instrumentation.
/// Handles non-standard tool calling where models return JSON in content instead of tool_calls.
/// </summary>
public class InstrumentedFoundryLocalAgent : AIAgent
{
    private readonly IChatClient _chatClient;
    private readonly string _model;
    private readonly string _mcpToolsUrl;
    private readonly IList<McpClientTool> _mcpTools;
    private readonly HttpClient _httpClient;
    private readonly string? _instructions;
    private readonly string _name;
    private readonly string? _description;
    private readonly string _id;
    private readonly ILogger? _logger;

    public InstrumentedFoundryLocalAgent(
        IChatClient chatClient,
        string model,
        string mcpToolsUrl,
        IList<McpClientTool> mcpTools,
        HttpClient httpClient,
        string? instructions = null,
        string? name = null,
        string? description = null,
        ILogger? logger = null)
    {
        _chatClient = chatClient;
        _model = model;
        _mcpToolsUrl = mcpToolsUrl;
        _mcpTools = mcpTools;
        _httpClient = httpClient;
        _instructions = instructions;
        _name = name ?? "FoundryLocalAgent";
        _description = description ?? "AI Agent powered by Foundry Local with MCP tools";
        _id = Guid.NewGuid().ToString();
        _logger = logger;
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

        try
        {
            var result = await ProcessWithToolsAsync(allMessages, cancellationToken);

            var responseMessage = new ChatMessage(ChatRole.Assistant, result);
            agentThread.MessageStore.Add(responseMessage);

            return new AgentRunResponse(responseMessage);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing chat request");
            throw;
        }
    }

    public override async IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await RunAsync(messages, thread, options, cancellationToken);

        foreach (var msg in response.Messages)
        {
            yield return new AgentRunResponseUpdate(msg.Role, msg.Text);
        }
    }

    private async Task<string> ProcessWithToolsAsync(
        IList<ChatMessage> chatMessages,
        CancellationToken cancellationToken)
    {
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

        // Build messages for chat client
        var meaiMessages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt)
        };
        meaiMessages.AddRange(chatMessages);

        _logger?.LogDebug("[FoundryLocalAgent] Sending request to model...");

        // OpenTelemetryChatClient automatically traces this call
        var response = await _chatClient.GetResponseAsync(meaiMessages, cancellationToken: cancellationToken);
        var content = response.Text ?? "";

        _logger?.LogDebug("[FoundryLocalAgent] Model response: {Content}", content);

        // Parse tool calls from content (Foundry Local format - JSON in content, not tool_calls)
        var toolCalls = ToolCallParser.Parse(content);
        var validToolCalls = toolCalls
            .Where(tc => _mcpTools.Any(t => t.Name == tc.Name))
            .ToList();

        if (validToolCalls.Count == 0)
        {
            return content;
        }

        _logger?.LogInformation("[FoundryLocalAgent] Executing {Count} tool(s): {Tools}",
            validToolCalls.Count,
            string.Join(", ", validToolCalls.Select(tc => tc.Name)));

        // Execute tools
        var toolResults = await ExecuteToolsAsync(validToolCalls, cancellationToken);

        // Build tool results message
        var toolResultsText = string.Join("\n\n", toolResults.Select(r =>
            r.Error is null
                ? $"Tool '{r.Name}' result:\n{r.Result}"
                : $"Tool '{r.Name}' error: {r.Error}"));

        // Add tool results to conversation and get final response
        meaiMessages.Add(new ChatMessage(ChatRole.Assistant, content));
        meaiMessages.Add(new ChatMessage(ChatRole.User,
            $"Tool execution results:\n{toolResultsText}\n\nPlease provide your final response based on these results."));

        // OpenTelemetryChatClient automatically traces this call too
        var finalResponse = await _chatClient.GetResponseAsync(meaiMessages, cancellationToken: cancellationToken);
        return finalResponse.Text ?? toolResultsText;
    }

    private async Task<List<ToolExecutionResult>> ExecuteToolsAsync(
        List<ToolCall> toolCalls,
        CancellationToken cancellationToken)
    {
        var toolTasks = toolCalls.Select(async toolCall =>
        {
            try
            {
                var result = await CallMcpToolAsync(toolCall.Name, toolCall.Arguments, cancellationToken);
                return new ToolExecutionResult(toolCall.Name, result ?? "", null);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Tool execution failed: {ToolName}", toolCall.Name);
                return new ToolExecutionResult(toolCall.Name, "", ex.Message);
            }
        });

        return (await Task.WhenAll(toolTasks)).ToList();
    }

    private record ToolExecutionResult(string Name, string Result, string? Error);

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
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(_mcpToolsUrl)
        }, _httpClient, ownsHttpClient: false);

        await using var mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

        var convertedArgs = arguments.ToDictionary(
            kvp => kvp.Key,
            kvp => ConvertJsonElement(kvp.Value));

        var result = await mcpClient.CallToolAsync(toolName, convertedArgs, cancellationToken: cancellationToken);

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
