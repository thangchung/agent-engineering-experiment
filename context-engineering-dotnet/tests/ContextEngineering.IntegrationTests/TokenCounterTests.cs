using ContextEngineering.Infrastructure.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;

namespace ContextEngineering.IntegrationTests;

public class TokenCounterTests
{
    [Fact]
    public void CountTokens_EmptyString_ReturnsZero()
    {
        // Arrange
        var counter = new TiktokenCounter();

        // Act
        var result = counter.CountTokens(string.Empty);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void CountTokens_SimpleText_ReturnsPositiveCount()
    {
        // Arrange
        var counter = new TiktokenCounter();
        var text = "Hello, world! This is a test message.";

        // Act
        var result = counter.CountTokens(text);

        // Assert
        Assert.True(result > 0);
        Assert.True(result < 20); // Should be around 10 tokens
    }

    [Fact]
    public void CountTokens_MultipleTexts_ReturnsSumOfCounts()
    {
        // Arrange
        var counter = new TiktokenCounter();
        var texts = new[] { "Hello", "World", "Test" };

        // Act
        var result = counter.CountTokens(texts);
        var individualSum = texts.Sum(t => counter.CountTokens(t));

        // Assert
        Assert.Equal(individualSum, result);
    }
}

public class TokenCountingChatReducerTests
{
    [Fact]
    public async Task ReduceAsync_BelowThreshold_ReturnsNull()
    {
        // Arrange
        var tokenCounter = new TiktokenCounter();
        var logger = Mock.Of<ILogger<TokenCountingChatReducer>>();
        var reducer = new TokenCountingChatReducer(tokenCounter, logger, maxTokens: 4000);
        
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helpful assistant."),
            new(ChatRole.User, "Hello!")
        };

        // Act
        var result = await reducer.ReduceAsync(messages);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ReduceAsync_AboveThreshold_ReducesMessages()
    {
        // Arrange
        var tokenCounter = new TiktokenCounter();
        var logger = Mock.Of<ILogger<TokenCountingChatReducer>>();
        var reducer = new TokenCountingChatReducer(tokenCounter, logger, maxTokens: 50, targetTokens: 30);
        
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helpful assistant."),
            new(ChatRole.User, "This is message one with quite a lot of text to make it longer."),
            new(ChatRole.User, "This is message two with even more text to add to the token count."),
            new(ChatRole.User, "This is message three with additional content."),
            new(ChatRole.User, "Short msg")
        };

        // Act
        var result = await reducer.ReduceAsync(messages);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count < messages.Count);
        // System message should be preserved
        Assert.Contains(result, m => m.Role == ChatRole.System);
    }

    [Fact]
    public async Task ReduceAsync_PreservesSystemMessages()
    {
        // Arrange
        var tokenCounter = new TiktokenCounter();
        var logger = Mock.Of<ILogger<TokenCountingChatReducer>>();
        var reducer = new TokenCountingChatReducer(tokenCounter, logger, maxTokens: 20, targetTokens: 15);
        
        var systemMessage = new ChatMessage(ChatRole.System, "System prompt");
        var messages = new List<ChatMessage>
        {
            systemMessage,
            new(ChatRole.User, "User message with lots of content that will exceed the token limit for sure."),
            new(ChatRole.Assistant, "Assistant response with additional content.")
        };

        // Act
        var result = await reducer.ReduceAsync(messages);

        // Assert
        Assert.NotNull(result);
        Assert.Contains(result, m => m.Role == ChatRole.System);
    }
}
