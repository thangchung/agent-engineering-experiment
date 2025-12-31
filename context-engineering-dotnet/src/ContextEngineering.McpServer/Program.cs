using ContextEngineering.Core.Entities;
using ContextEngineering.Core.Interfaces;
using ContextEngineering.Infrastructure;
using ContextEngineering.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults
builder.AddServiceDefaults();

// Add PostgreSQL with EF Core (Aspire integration)
builder.AddNpgsqlDbContext<AppDbContext>("contextdb", configureDbContextOptions: options =>
{
    options.UseNpgsql(npgsql => npgsql.MigrationsAssembly("ContextEngineering.Infrastructure"))
           .UseSnakeCaseNamingConvention();
});

// Add infrastructure services
builder.Services.AddInfrastructure();

// Add MCP Server with HTTP transport for Aspire integration
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "ContextEngineering.McpServer", Version = "1.0.0" };
    })
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

// Apply migrations on startup (development only)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// Map Aspire default endpoints
app.MapDefaultEndpoints();

// Map MCP endpoints
app.MapMcp();

app.Run();

// === MCP Tools ===

[McpServerToolType]
public static class ScratchpadTools
{
    [McpServerTool(Name = "read_scratchpad")]
    [Description("Read the user's scratchpad content including preferences and completed tasks.")]
    public static async Task<string> ReadScratchpad(
        [Description("The user ID to read scratchpad for")] string userId,
        IScratchpadRepository scratchpadRepository,
        CancellationToken ct)
    {
        var scratchpads = await scratchpadRepository.GetAllByUserIdAsync(userId, ct);
        
        if (scratchpads.Count == 0)
        {
            return "No scratchpad data found for this user.";
        }

        var result = scratchpads
            .Select(s => $"[{s.Category.ToUpperInvariant()}]\n{s.Content}")
            .Aggregate((a, b) => $"{a}\n\n{b}");

        return result;
    }

    [McpServerTool(Name = "update_scratchpad")]
    [Description("Update the user's scratchpad with new preferences or completed tasks.")]
    public static async Task<string> UpdateScratchpad(
        [Description("The user ID to update scratchpad for")] string userId,
        [Description("Category: 'preferences' for user preferences, 'tasks' for completed tasks")] string category,
        [Description("The content to store in the scratchpad")] string content,
        IScratchpadRepository scratchpadRepository,
        CancellationToken ct)
    {
        if (category != ScratchpadCategory.Preferences && category != ScratchpadCategory.Tasks)
        {
            return $"Invalid category '{category}'. Use 'preferences' or 'tasks'.";
        }

        var scratchpad = await scratchpadRepository.UpsertAsync(userId, category, content, ct);
        return $"Scratchpad [{category}] updated successfully at {scratchpad.UpdatedAt:u}.";
    }
}

[McpServerToolType]
public static class ConversationTools
{
    [McpServerTool(Name = "get_or_create_conversation")]
    [Description("Get or create a conversation thread for a user. Returns the thread ID and metadata.")]
    public static async Task<string> GetOrCreateConversation(
        [Description("The user ID to get or create conversation for")] string userId,
        IConversationRepository conversationRepository,
        CancellationToken ct)
    {
        var thread = await conversationRepository.GetByUserIdAsync(userId, ct)
                     ?? await conversationRepository.CreateAsync(userId, ct);

        return JsonSerializer.Serialize(new
        {
            threadId = thread.Id,
            userId = thread.UserId,
            messageCount = thread.MessageCount,
            totalTokensUsed = thread.TotalTokensUsed,
            reductionEventCount = thread.ReductionEventCount,
            createdAt = thread.CreatedAt
        });
    }

    [McpServerTool(Name = "get_conversation_messages")]
    [Description("Get all messages in a conversation thread.")]
    public static async Task<string> GetConversationMessages(
        [Description("The thread ID (GUID) to get messages for")] string threadId,
        IConversationRepository conversationRepository,
        CancellationToken ct)
    {
        if (!Guid.TryParse(threadId, out var guid))
        {
            return "Invalid thread ID format. Must be a valid GUID.";
        }

        var messages = await conversationRepository.GetMessagesAsync(guid, ct);
        
        if (messages.Count == 0)
        {
            return "[]";
        }

        return JsonSerializer.Serialize(messages.Select(m => new
        {
            role = m.Role,
            content = m.Content,
            tokenCount = m.TokenCount,
            createdAt = m.CreatedAt
        }));
    }

    [McpServerTool(Name = "add_message")]
    [Description("Add a message to a conversation thread.")]
    public static async Task<string> AddMessage(
        [Description("The thread ID (GUID) to add message to")] string threadId,
        [Description("The role of the message sender: 'user' or 'assistant'")] string role,
        [Description("The content of the message")] string content,
        [Description("The token count for this message")] int tokenCount,
        IConversationRepository conversationRepository,
        CancellationToken ct)
    {
        if (!Guid.TryParse(threadId, out var guid))
        {
            return "Invalid thread ID format. Must be a valid GUID.";
        }

        if (role != "user" && role != "assistant")
        {
            return "Invalid role. Must be 'user' or 'assistant'.";
        }

        var message = new ChatMessage
        {
            Role = role,
            Content = content,
            TokenCount = tokenCount
        };

        await conversationRepository.AddMessageAsync(guid, message, ct);
        return $"Message added successfully. Role: {role}, Tokens: {tokenCount}";
    }

    [McpServerTool(Name = "record_token_usage")]
    [Description("Record token usage for a conversation and optionally increment reduction count.")]
    public static async Task<string> RecordTokenUsage(
        [Description("The thread ID (GUID) to record usage for")] string threadId,
        [Description("Number of tokens in the prompt")] int promptTokens,
        [Description("Number of tokens in the completion")] int completionTokens,
        [Description("Whether the conversation was reduced/summarized")] bool wasReduced,
        IConversationRepository conversationRepository,
        CancellationToken ct)
    {
        if (!Guid.TryParse(threadId, out var guid))
        {
            return "Invalid thread ID format. Must be a valid GUID.";
        }

        await conversationRepository.RecordTokenUsageAsync(guid, promptTokens, completionTokens, wasReduced, ct);
        
        if (wasReduced)
        {
            await conversationRepository.IncrementReductionCountAsync(guid, ct);
        }

        return $"Token usage recorded. Prompt: {promptTokens}, Completion: {completionTokens}, Reduced: {wasReduced}";
    }

    [McpServerTool(Name = "create_new_conversation")]
    [Description("Create a new conversation thread for a user, ending the current one.")]
    public static async Task<string> CreateNewConversation(
        [Description("The user ID to create a new conversation for")] string userId,
        IConversationRepository conversationRepository,
        CancellationToken ct)
    {
        var thread = await conversationRepository.CreateAsync(userId, ct);
        
        return JsonSerializer.Serialize(new
        {
            threadId = thread.Id,
            userId = thread.UserId,
            createdAt = thread.CreatedAt
        });
    }

    [McpServerTool(Name = "get_conversation_summary")]
    [Description("Get conversation summary/statistics for a user.")]
    public static async Task<string> GetConversationSummary(
        [Description("The user ID to get conversation summary for")] string userId,
        IConversationRepository conversationRepository,
        CancellationToken ct)
    {
        var thread = await conversationRepository.GetByUserIdAsync(userId, ct);
        
        if (thread is null)
        {
            return "No conversation found for this user.";
        }

        return JsonSerializer.Serialize(new
        {
            threadId = thread.Id,
            userId = thread.UserId,
            messageCount = thread.MessageCount,
            totalTokensUsed = thread.TotalTokensUsed,
            reductionEventCount = thread.ReductionEventCount,
            createdAt = thread.CreatedAt
        });
    }
}
