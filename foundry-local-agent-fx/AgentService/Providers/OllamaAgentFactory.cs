using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OllamaSharp;

namespace AgentService.Providers;

/// <summary>
/// Factory for creating Ollama-based AI agents using the official Microsoft Agent Framework pattern.
/// Uses OllamaApiClient.CreateAIAgent() which auto-wraps with FunctionInvokingChatClient for tool support.
/// Includes OpenTelemetry GenAI Semantic Conventions v1.38 instrumentation.
/// </summary>
public static class OllamaAgentFactory
{
    /// <summary>
    /// Source name for OpenTelemetry traces.
    /// Use this to filter/subscribe to Ollama agent traces.
    /// </summary>
    public const string OtelSourceName = "AgentService.Ollama";

    /// <summary>
    /// Creates a ChatClientAgent backed by Ollama with MCP tools support and full OpenTelemetry instrumentation.
    /// </summary>
    /// <param name="endpoint">Ollama server endpoint (e.g., http://localhost:11434/)</param>
    /// <param name="model">Model name (e.g., gpt-oss:20b, llama3.1:70b)</param>
    /// <param name="mcpTools">MCP tools to register with the agent</param>
    /// <param name="instructions">System instructions for the agent</param>
    /// <param name="name">Agent name for identification</param>
    /// <param name="description">Agent description</param>
    /// <param name="loggerFactory">Optional logger factory</param>
    /// <param name="enableSensitiveData">Enable capture of message content, tool args/results (default: false, or set OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT=true)</param>
    /// <returns>A ChatClientAgent instance with automatic function invocation and OpenTelemetry tracing</returns>
    /// <remarks>
    /// <para>
    /// This implementation follows OpenTelemetry Semantic Conventions for Generative AI v1.38:
    /// https://opentelemetry.io/docs/specs/semconv/gen-ai/
    /// </para>
    /// <para>
    /// Traces emitted (via OpenTelemetryChatClient):
    /// - Span: "chat {model}" (CLIENT kind)
    /// - Attributes: gen_ai.operation.name, gen_ai.request.model, gen_ai.provider.name ("ollama")
    /// - Attributes: gen_ai.response.model, gen_ai.response.id, gen_ai.response.finish_reasons
    /// - Attributes: gen_ai.usage.input_tokens, gen_ai.usage.output_tokens
    /// - Opt-in: gen_ai.input.messages, gen_ai.output.messages, gen_ai.tool.definitions
    /// </para>
    /// <para>
    /// Metrics emitted:
    /// - gen_ai.client.operation.duration (histogram, seconds)
    /// - gen_ai.client.token.usage (histogram, tokens, by input/output type)
    /// </para>
    /// <para>
    /// Tool execution spans (via FunctionInvokingChatClient):
    /// - Span: "execute_tool {tool.name}" (INTERNAL kind)
    /// - Attributes: gen_ai.tool.name, gen_ai.tool.call.id
    /// - Opt-in: gen_ai.tool.call.arguments, gen_ai.tool.call.result
    /// </para>
    /// <para>
    /// Note: This pattern requires models that support native function calling (tool_calls).
    /// Small models like llama3.2:3b may not work - use FoundryLocalAgent for those.
    /// </para>
    /// </remarks>
    public static ChatClientAgent Create(
        string endpoint,
        string model,
        IList<McpClientTool> mcpTools,
        string? instructions = null,
        string? name = null,
        string? description = null,
        ILoggerFactory? loggerFactory = null,
        bool? enableSensitiveData = null)
    {
        // Build the IChatClient pipeline with full observability:
        // OllamaApiClient -> OpenTelemetryChatClient -> FunctionInvokingChatClient
        //
        // Pipeline order matters:
        // 1. OpenTelemetryChatClient wraps the base client for tracing all requests
        // 2. FunctionInvokingChatClient handles tool execution (also traced by OTel client)
        
        var ollamaClient = new OllamaApiClient(new Uri(endpoint), model);
        
        var tools = mcpTools.Cast<AITool>().ToList();
        
        // Build the chat client pipeline with OpenTelemetry instrumentation
        IChatClient chatClient = new ChatClientBuilder(ollamaClient)
            .UseOpenTelemetry(
                loggerFactory: loggerFactory,
                sourceName: OtelSourceName,
                configure: otel =>
                {
                    // Enable sensitive data capture if explicitly set, otherwise respect env var
                    // OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT=true
                    if (enableSensitiveData.HasValue)
                    {
                        otel.EnableSensitiveData = enableSensitiveData.Value;
                    }
                })
            .UseFunctionInvocation(loggerFactory: loggerFactory)
            .Build();

        // Create the agent with the instrumented chat client
        return new ChatClientAgent(
            chatClient: chatClient,
            instructions: instructions ?? "You are a helpful assistant with access to tools.",
            name: name ?? "OllamaAgent",
            description: description,
            tools: tools);
    }
}
