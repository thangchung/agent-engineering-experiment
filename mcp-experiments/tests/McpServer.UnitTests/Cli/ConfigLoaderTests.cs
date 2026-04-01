using McpServer.Cli;
using Microsoft.Extensions.Configuration;

namespace McpServer.UnitTests.Cli;

public sealed class ConfigLoaderTests
{
    [Fact]
    public void Load_DefaultsToFalse_WhenHostingSectionMissing()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();

        CliConfig result = ConfigLoader.Load(config);

        Assert.False(result.EnableCliMode);
        Assert.False(result.CliServeMode);
        Assert.False(result.EnableStatistic);
    }

    [Fact]
    public void Load_UsesHostingValues_WhenProvided()
    {
        Dictionary<string, string?> values = new()
        {
            ["Hosting:EnableCliMode"] = "true",
            ["Hosting:CliServeMode"] = "true",
            ["Hosting:EnableStatistic"] = "true",
        };

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        CliConfig result = ConfigLoader.Load(config);

        Assert.True(result.EnableCliMode);
        Assert.True(result.CliServeMode);
        Assert.True(result.EnableStatistic);
    }
}
