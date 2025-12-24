using System.Diagnostics;
using System.Text.Json;

namespace AgentService;

/// <summary>
/// OpenTelemetry tracing helper for GenAI operations following semantic conventions.
/// See: https://opentelemetry.io/docs/specs/semconv/gen-ai/gen-ai-spans/
/// </summary>
public static class GenAITracing
{
    public const string SourceName = "AgentService.GenAI";
    
    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");

    // GenAI Semantic Convention attribute names
    public static class Attributes
    {
        // Required
        public const string OperationName = "gen_ai.operation.name";
        public const string ProviderName = "gen_ai.provider.name";
        
        // Request attributes
        public const string RequestModel = "gen_ai.request.model";
        public const string RequestTemperature = "gen_ai.request.temperature";
        public const string RequestMaxTokens = "gen_ai.request.max_tokens";
        
        // Response attributes
        public const string ResponseModel = "gen_ai.response.model";
        public const string ResponseId = "gen_ai.response.id";
        public const string ResponseFinishReasons = "gen_ai.response.finish_reasons";
        
        // Token usage
        public const string UsageInputTokens = "gen_ai.usage.input_tokens";
        public const string UsageOutputTokens = "gen_ai.usage.output_tokens";
        
        // Content (Opt-In - sensitive)
        public const string SystemInstructions = "gen_ai.system_instructions";
        public const string InputMessages = "gen_ai.input.messages";
        public const string OutputMessages = "gen_ai.output.messages";
        
        // Tool attributes
        public const string ToolName = "gen_ai.tool.name";
        public const string ToolCallId = "gen_ai.tool.call.id";
        public const string ToolCallArguments = "gen_ai.tool.call.arguments";
        public const string ToolCallResult = "gen_ai.tool.call.result";
        public const string ToolDefinitions = "gen_ai.tool.definitions";
        public const string ToolType = "gen_ai.tool.type";
        
        // Conversation
        public const string ConversationId = "gen_ai.conversation.id";
        
        // Server
        public const string ServerAddress = "server.address";
        public const string ServerPort = "server.port";
        
        // Error
        public const string ErrorType = "error.type";
    }

    // Operation names
    public static class Operations
    {
        public const string Chat = "chat";
        public const string TextCompletion = "text_completion";
        public const string Embeddings = "embeddings";
        public const string ExecuteTool = "execute_tool";
        public const string InvokeAgent = "invoke_agent";
    }

    // Provider names
    public static class Providers
    {
        public const string FoundryLocal = "foundry_local";
        public const string OpenAI = "openai";
        public const string AzureOpenAI = "azure.ai.openai";
    }

    /// <summary>
    /// Start a chat completion span.
    /// </summary>
    public static Activity? StartChatSpan(
        string model,
        string provider = Providers.FoundryLocal,
        string? serverAddress = null,
        int? serverPort = null,
        string? conversationId = null)
    {
        var activity = ActivitySource.StartActivity(
            $"{Operations.Chat} {model}",
            ActivityKind.Client);

        if (activity is null) return null;

        activity.SetTag(Attributes.OperationName, Operations.Chat);
        activity.SetTag(Attributes.ProviderName, provider);
        activity.SetTag(Attributes.RequestModel, model);

        if (serverAddress is not null)
            activity.SetTag(Attributes.ServerAddress, serverAddress);
        if (serverPort is not null)
            activity.SetTag(Attributes.ServerPort, serverPort);
        if (conversationId is not null)
            activity.SetTag(Attributes.ConversationId, conversationId);

        return activity;
    }

    /// <summary>
    /// Start a tool execution span.
    /// </summary>
    public static Activity? StartToolSpan(
        string toolName,
        string? toolCallId = null,
        object? arguments = null)
    {
        var activity = ActivitySource.StartActivity(
            $"{Operations.ExecuteTool} {toolName}",
            ActivityKind.Internal);

        if (activity is null) return null;

        activity.SetTag(Attributes.OperationName, Operations.ExecuteTool);
        activity.SetTag(Attributes.ToolName, toolName);
        activity.SetTag(Attributes.ToolType, "function");

        if (toolCallId is not null)
            activity.SetTag(Attributes.ToolCallId, toolCallId);
        if (arguments is not null)
            activity.SetTag(Attributes.ToolCallArguments, JsonSerializer.Serialize(arguments));

        return activity;
    }

    /// <summary>
    /// Set response attributes on the activity.
    /// </summary>
    public static void SetResponseAttributes(
        Activity? activity,
        string? responseModel = null,
        string? responseId = null,
        string? finishReason = null,
        int? inputTokens = null,
        int? outputTokens = null)
    {
        if (activity is null) return;

        if (responseModel is not null)
            activity.SetTag(Attributes.ResponseModel, responseModel);
        if (responseId is not null)
            activity.SetTag(Attributes.ResponseId, responseId);
        if (finishReason is not null)
            activity.SetTag(Attributes.ResponseFinishReasons, new[] { finishReason });
        if (inputTokens is not null)
            activity.SetTag(Attributes.UsageInputTokens, inputTokens);
        if (outputTokens is not null)
            activity.SetTag(Attributes.UsageOutputTokens, outputTokens);
    }

    /// <summary>
    /// Set input/output messages (Opt-In - contains sensitive data).
    /// </summary>
    public static void SetMessages(
        Activity? activity,
        object? inputMessages = null,
        object? outputMessages = null,
        object? systemInstructions = null)
    {
        if (activity is null) return;

        if (inputMessages is not null)
            activity.SetTag(Attributes.InputMessages, JsonSerializer.Serialize(inputMessages));
        if (outputMessages is not null)
            activity.SetTag(Attributes.OutputMessages, JsonSerializer.Serialize(outputMessages));
        if (systemInstructions is not null)
            activity.SetTag(Attributes.SystemInstructions, JsonSerializer.Serialize(systemInstructions));
    }

    /// <summary>
    /// Set tool definitions (Opt-In).
    /// </summary>
    public static void SetToolDefinitions(Activity? activity, object? toolDefinitions)
    {
        if (activity is null || toolDefinitions is null) return;
        activity.SetTag(Attributes.ToolDefinitions, JsonSerializer.Serialize(toolDefinitions));
    }

    /// <summary>
    /// Set tool result.
    /// </summary>
    public static void SetToolResult(Activity? activity, object? result)
    {
        if (activity is null || result is null) return;
        activity.SetTag(Attributes.ToolCallResult, JsonSerializer.Serialize(result));
    }

    /// <summary>
    /// Record an error on the activity.
    /// </summary>
    public static void RecordError(Activity? activity, Exception exception)
    {
        if (activity is null) return;

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag(Attributes.ErrorType, exception.GetType().Name);
        
        // Add exception details as event
        var tagsCollection = new ActivityTagsCollection
        {
            { "exception.type", exception.GetType().FullName },
            { "exception.message", exception.Message },
            { "exception.stacktrace", exception.StackTrace }
        };
        activity.AddEvent(new ActivityEvent("exception", tags: tagsCollection));
    }
}
