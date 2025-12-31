using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using ContextEngineering.Core.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace ContextEngineering.IntegrationTests;

public class ApiIntegrationTests : IAsyncLifetime
{
    private DistributedApplication? _app;
    private HttpClient? _httpClient;

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.ContextEngineering_AppHost>();
        
        appHost.Services.ConfigureHttpClientDefaults(config =>
        {
            config.AddStandardResilienceHandler();
        });

        _app = await appHost.BuildAsync();
        await _app.StartAsync();
        
        _httpClient = _app.CreateHttpClient("api");
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
        _httpClient?.Dispose();
    }

    [Fact]
    public async Task GetScratchpad_ReturnsSeededData()
    {
        // Arrange
        var userId = "demo-user";

        // Act
        var response = await _httpClient!.GetAsync($"/api/scratchpad/{userId}");
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var scratchpads = await response.Content.ReadFromJsonAsync<List<Scratchpad>>();
        Assert.NotNull(scratchpads);
        Assert.Equal(2, scratchpads.Count);
        Assert.Contains(scratchpads, s => s.Category == ScratchpadCategory.Preferences);
        Assert.Contains(scratchpads, s => s.Category == ScratchpadCategory.Tasks);
    }

    [Fact]
    public async Task UpdateScratchpad_CreatesNewEntry()
    {
        // Arrange
        var userId = "new-test-user";
        var content = new { Content = "Test preferences content" };

        // Act
        var response = await _httpClient!.PutAsJsonAsync(
            $"/api/scratchpad/{userId}/preferences", content);
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var scratchpad = await response.Content.ReadFromJsonAsync<Scratchpad>();
        Assert.NotNull(scratchpad);
        Assert.Equal(userId, scratchpad.UserId);
        Assert.Equal(ScratchpadCategory.Preferences, scratchpad.Category);
        Assert.Equal("Test preferences content", scratchpad.Content);
    }

    [Fact]
    public async Task UpdateScratchpad_InvalidCategory_ReturnsBadRequest()
    {
        // Arrange
        var userId = "test-user";
        var content = new { Content = "Some content" };

        // Act
        var response = await _httpClient!.PutAsJsonAsync(
            $"/api/scratchpad/{userId}/invalid-category", content);
        
        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateConversation_ReturnsNewThread()
    {
        // Arrange
        var userId = "conversation-test-user";

        // Act
        var response = await _httpClient!.PostAsync($"/api/conversations/{userId}/new", null);
        
        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GetConversation_WithSeededData_ReturnsThread()
    {
        // Arrange
        var userId = "demo-user";

        // Act
        var response = await _httpClient!.GetAsync($"/api/conversations/{userId}");
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var conversation = await response.Content.ReadFromJsonAsync<ConversationSummary>();
        Assert.NotNull(conversation);
        Assert.Equal(2, conversation.MessageCount);
        Assert.True(conversation.TotalTokensUsed > 0);
    }

    [Fact]
    public async Task GetConversation_NonExistentUser_ReturnsNotFound()
    {
        // Arrange
        var userId = "non-existent-user-12345";

        // Act
        var response = await _httpClient!.GetAsync($"/api/conversations/{userId}");
        
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

record ConversationSummary(
    Guid ThreadId, 
    int MessageCount, 
    int TotalTokensUsed, 
    int ReductionEventCount, 
    DateTime CreatedAt);
