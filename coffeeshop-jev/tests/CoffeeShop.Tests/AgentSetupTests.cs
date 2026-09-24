using System.Diagnostics;
using CoffeeShop.Tests.Fakes;
using CounterService.Common;
using CounterService.Domain;
using CounterService.Features.Orders.Agents;
using CounterService.Features.Orders.Workflow;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CoffeeShop.Tests;

/// <summary>tasks.md T07.</summary>
public class AgentSetupTests
{
    [Theory]
    [InlineData(AgentKeys.Counter, "CounterAgent")]
    [InlineData(AgentKeys.Barista, "BaristaAgent")]
    [InlineData(AgentKeys.Kitchen, "KitchenAgent")]
    public void AddOrderAgents_ResolvesKeyedAgent_WithExpectedName(string key, string expectedName)
    {
        using var provider = BuildProvider(isDevelopment: false);

        var agent = provider.GetRequiredKeyedService<AIAgent>(key);

        Assert.NotNull(agent);
        Assert.Equal(expectedName, agent.Name);
    }

    [Fact]
    public void AddOpenAiChatClient_MissingConfig_StillResolves_AndFailsOnlyAtCallTime()
    {
        // AppHost defaults every OPENAI_* parameter to "" - construction must never throw.
        using var provider = BuildProvider(isDevelopment: false, openAiBaseUrl: null, openAiApiKey: null, openAiModel: null);

        var chatClient = provider.GetRequiredService<Microsoft.Extensions.AI.IChatClient>();

        Assert.NotNull(chatClient);
    }

    private static ServiceProvider BuildProvider(
        bool isDevelopment,
        string? openAiBaseUrl = "https://openai.example.com/v1/",
        string? openAiApiKey = "test-key",
        string? openAiModel = "test-model")
    {
        var services = new ServiceCollection();

        var configValues = new Dictionary<string, string?>
        {
            ["OPENAI_BASE_URL"] = openAiBaseUrl,
            ["OPENAI_API_KEY"] = openAiApiKey,
            ["OPENAI_MODEL"] = openAiModel,
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(isDevelopment ? "Development" : "Production"));

        services.AddOpenAiChatClient();
        services.AddOrderAgents();

        return services.BuildServiceProvider();
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "CoffeeShop.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    // --- EnableSensitiveData follows the environment (AC2) ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CounterAgentFactory_Create_TagsSpanWithEnableSensitiveDataMatchingArgument(bool enableSensitiveData)
    {
        const string Marker = "super-secret-marker-4f8c";
        var fake = FakeChatClient.Returning("ok, got it");
        var agent = CounterAgentFactory.Create(fake, enableSensitiveData);

        var blob = new System.Text.StringBuilder();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("CoffeeShop.Agents", StringComparison.Ordinal)
                || source.Name.StartsWith("Experimental.Microsoft.Agents.AI", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                foreach (var tag in activity.TagObjects)
                {
                    blob.Append(tag.Value);
                }

                foreach (var evt in activity.Events)
                {
                    foreach (var tag in evt.Tags)
                    {
                        blob.Append(tag.Value);
                    }
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        await agent.RunAsync([new ChatMessage(ChatRole.User, Marker)]);

        var sawMarker = blob.ToString().Contains(Marker, StringComparison.Ordinal);
        Assert.Equal(enableSensitiveData, sawMarker);
    }

    // --- Prompt builders are pure functions (AC3) ---

    private static readonly IReadOnlyList<MenuItem> TestMenu =
    [
        new("LATTE", "Latte", 4.50m, Station.Barista),
        new("MUFFIN", "Muffin", 3.00m, Station.Kitchen),
    ];

    [Fact]
    public void ExtractPrompt_Build_IsPure_AndContainsMenuDisplayNames()
    {
        var a = ExtractPrompt.Build(["2 lattes"], TestMenu);
        var b = ExtractPrompt.Build(["2 lattes"], TestMenu);

        Assert.Equal(a, b);
        Assert.Contains("Latte", a, StringComparison.Ordinal);
        Assert.Contains("Muffin", a, StringComparison.Ordinal);
        Assert.Contains("2 lattes", a, StringComparison.Ordinal);
    }

    [Fact]
    public void ClarifyPrompt_Build_IsPure_AndContainsMenuDisplayNames()
    {
        var a = ClarifyPrompt.Build("item not on menu", TestMenu);
        var b = ClarifyPrompt.Build("item not on menu", TestMenu);

        Assert.Equal(a, b);
        Assert.Contains("Latte", a, StringComparison.Ordinal);
    }

    [Fact]
    public void DeliverPrompt_Build_IsPure()
    {
        var lines = new List<OrderLine> { new() { Name = "LATTE", Qty = 1, Price = 4.50m } };
        var a = DeliverPrompt.Build(lines, 4.50m, anyClamped: false);
        var b = DeliverPrompt.Build(lines, 4.50m, anyClamped: false);

        Assert.Equal(a, b);
        Assert.Contains("4.50", a, StringComparison.Ordinal);
    }
}
