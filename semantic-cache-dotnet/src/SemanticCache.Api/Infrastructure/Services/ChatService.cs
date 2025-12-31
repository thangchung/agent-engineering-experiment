using Azure.AI.OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using SemanticCache.Api.Infrastructure.Database;

namespace SemanticCache.Api.Infrastructure.Services;

public class ChatService(
    AzureOpenAIClient openAIClient,
    SemanticCacheService cacheService,
    AgentMemoryService memoryService,
    AppDbContext context,
    IConfiguration configuration)
{
    private readonly AzureOpenAIClient _openAIClient = openAIClient;
    private readonly SemanticCacheService _cacheService = cacheService;
    private readonly AgentMemoryService _memoryService = memoryService;
    private readonly AppDbContext _context = context;
    private readonly string _chatModel = configuration["AzureOpenAI:ChatModel"] ?? "gpt-4";
    private readonly string _embeddingModel = configuration["AzureOpenAI:EmbeddingModel"] ?? "text-embedding-ada-002";

    /// <summary>
    /// Generates embeddings for the given text using Azure OpenAI.
    /// </summary>
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var embeddingClient = _openAIClient.GetEmbeddingClient(_embeddingModel);
        var result = await embeddingClient.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
        return result.Value.ToFloats().ToArray();
    }

    /// <summary>
    /// Raw chat completion without caching or personalization.
    /// </summary>
    public async Task<string> GetRawResponseAsync(string query, CancellationToken cancellationToken = default)
    {
        var chatClient = _openAIClient.GetChatClient(_chatModel);
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a helpful AI assistant."),
            new UserChatMessage(query)
        };

        var response = await chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
        return response.Value.Content[0].Text;
    }

    /// <summary>
    /// Chat completion with semantic caching. Checks cache first, falls back to OpenAI if no match.
    /// </summary>
    public async Task<(string Response, bool FromCache, float Similarity)> GetCachedResponseAsync(
        string query,
        float similarityThreshold = 0.85f,
        CancellationToken cancellationToken = default)
    {
        // Generate embedding for the query
        var embedding = await GenerateEmbeddingAsync(query, cancellationToken);

        // Try to get from semantic cache
        var cacheResult = await _cacheService.GetSemanticCacheAsync(embedding, similarityThreshold, cancellationToken);

        if (cacheResult != null)
        {
            return (cacheResult.Value.Response!, true, cacheResult.Value.Similarity);
        }

        // Cache miss - get fresh response from OpenAI
        var response = await GetRawResponseAsync(query, cancellationToken);

        // Store in cache for future use
        await _cacheService.SetSemanticCacheAsync(query, response, embedding, cancellationToken: cancellationToken);

        return (response, false, 0);
    }

    /// <summary>
    /// Personalized chat with user context, conversation history, and semantic caching.
    /// </summary>
    public async Task<(string Response, bool FromCache, float Similarity)> GetPersonalizedResponseAsync(
        string userId,
        string userName,
        string query,
        string? threadId = null,
        float similarityThreshold = 0.85f,
        CancellationToken cancellationToken = default)
    {
        // Ensure user exists
        await _memoryService.GetOrCreateUserAsync(userId, userName, cancellationToken);

        // Generate thread ID if not provided
        threadId ??= Guid.NewGuid().ToString();

        // Get user preferences and context
        var preferences = await _memoryService.GetUserPreferencesAsync(userId, cancellationToken);
        var contextMemories = await _memoryService.GetAllContextMemoriesAsync(userId, cancellationToken);
        var conversationHistory = await _memoryService.GetConversationHistoryAsync(userId, threadId, maxEntries: 10, cancellationToken);

        // Try semantic cache first
        var embedding = await GenerateEmbeddingAsync(query, cancellationToken);
        var cacheResult = await _cacheService.GetSemanticCacheAsync(embedding, similarityThreshold, cancellationToken);

        string response;
        bool fromCache;
        float similarity;

        if (cacheResult != null)
        {
            response = cacheResult.Value.Response!;
            fromCache = true;
            similarity = cacheResult.Value.Similarity;
        }
        else
        {
            // Build personalized context
            var chatClient = _openAIClient.GetChatClient(_chatModel);
            var messages = new List<ChatMessage>();

            // System message with user context
            var systemPrompt = $"You are a helpful AI assistant talking to {userName}.";
            if (preferences.Count > 0)
            {
                systemPrompt += $"\n\nUser preferences: {string.Join(", ", preferences.Select(p => $"{p.Key}={p.Value}"))}";
            }
            if (contextMemories.Count > 0)
            {
                systemPrompt += $"\n\nContext: {string.Join(", ", contextMemories.Select(m => $"{m.Key}={m.Value}"))}";
            }
            messages.Add(new SystemChatMessage(systemPrompt));

            // Add conversation history
            foreach (var entry in conversationHistory)
            {
                if (entry.Role == "user")
                {
                    messages.Add(new UserChatMessage(entry.Content));
                }
                else if (entry.Role == "assistant")
                {
                    messages.Add(new AssistantChatMessage(entry.Content));
                }
            }

            // Add current query
            messages.Add(new UserChatMessage(query));

            var chatResponse = await chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
            response = chatResponse.Value.Content[0].Text;
            fromCache = false;
            similarity = 0;

            // Store in cache
            await _cacheService.SetSemanticCacheAsync(query, response, embedding, cancellationToken: cancellationToken);
        }

        // Save conversation entries
        await _memoryService.AddConversationEntryAsync(userId, threadId, "user", query, cancellationToken);
        await _memoryService.AddConversationEntryAsync(userId, threadId, "assistant", response, cancellationToken);

        return (response, fromCache, similarity);
    }
}
