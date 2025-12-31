using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SemanticCache.IntegrationTests.Tests;

public class SemanticCacheIntegrationTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task GetAllUsers_ReturnsSeededUsers()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SemanticCache_AppHost>(cancellationToken);
        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
        });

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        // Act
        using var httpClient = app.CreateHttpClient("api");
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        
        using var response = await httpClient.GetAsync("/api/users", cancellationToken);
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var users = await response.Content.ReadFromJsonAsync<List<UserDto>>(cancellationToken);
        Assert.NotNull(users);
        Assert.Equal(3, users.Count); // 3 seeded users
        Assert.Contains(users, u => u.Name == "Alice Johnson");
        Assert.Contains(users, u => u.Name == "Bob Smith");
        Assert.Contains(users, u => u.Name == "Carol Williams");
    }

    [Fact]
    public async Task GetCacheEntries_ReturnsSeededCache()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SemanticCache_AppHost>(cancellationToken);
        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        // Act
        using var httpClient = app.CreateHttpClient("api");
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        
        using var response = await httpClient.GetAsync("/api/cache", cancellationToken);
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var cacheEntries = await response.Content.ReadFromJsonAsync<List<CacheEntryDto>>(cancellationToken);
        Assert.NotNull(cacheEntries);
        Assert.Equal(5, cacheEntries.Count); // 5 seeded cache entries
    }

    [Fact]
    public async Task GetMetrics_ReturnsSeededMetrics()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SemanticCache_AppHost>(cancellationToken);
        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        // Act
        using var httpClient = app.CreateHttpClient("api");
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        
        using var response = await httpClient.GetAsync("/api/metrics", cancellationToken);
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var metrics = await response.Content.ReadFromJsonAsync<List<MetricsDto>>(cancellationToken);
        Assert.NotNull(metrics);
        Assert.Equal(50, metrics.Count); // 50 seeded metrics logs
    }

    // DTOs for test assertions
    private record UserDto(string Id, string Name, DateTime CreatedAt, DateTime UpdatedAt);
    private record CacheEntryDto(string Id, string Query, string Response, int HitCount, DateTime CreatedAt, DateTime LastAccessedAt);
    private record MetricsDto(string Id, string Endpoint, string Method, int StatusCode, long ResponseTimeMs, bool CacheHit, string? UserId, DateTime Timestamp);
}
