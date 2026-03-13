using CoffeeshopCli.Configuration;
using Xunit;

namespace CoffeeshopCli.Tests.Configuration;

public sealed class ConfigLoaderTests
{
    [Fact]
    public void Load_DefaultsWhenNoAppsettings()
    {
        using var dir = new TestFixtures.TempDirectoryFixture();

        var cfg = ConfigLoader.Load(basePath: dir.Path);

        Assert.Equal("./skills", cfg.Discovery.SkillsDirectory);
        Assert.False(cfg.Hosting.EnableHttpMcpBridge);
    }

    [Fact]
    public void Load_ReadsSkillsDirectoryFromAppsettings()
    {
        using var dir = new TestFixtures.TempDirectoryFixture();
        File.WriteAllText(Path.Combine(dir.Path, "appsettings.json"), """
{
  "Discovery": {
    "SkillsDirectory": "/tmp/appsettings-skills"
  }
}
""");

        var cfg = ConfigLoader.Load(basePath: dir.Path);

        Assert.Equal("/tmp/appsettings-skills", cfg.Discovery.SkillsDirectory);
    }

    [Fact]
    public void Load_EnvVarsOverrideAppsettings()
    {
        using var dir = new TestFixtures.TempDirectoryFixture();
        File.WriteAllText(Path.Combine(dir.Path, "appsettings.json"), """
{
  "Discovery": {
    "SkillsDirectory": "/tmp/appsettings-skills"
  }
}
""");

        var key = "Discovery__SkillsDirectory";
        var old = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "/tmp/env-skills");

            var cfg = ConfigLoader.Load(basePath: dir.Path);
            Assert.Equal("/tmp/env-skills", cfg.Discovery.SkillsDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, old);
        }
    }

    [Fact]
    public void Load_ReadsHostingFlagFromEnv()
    {
        using var dir = new TestFixtures.TempDirectoryFixture();
        var key = "Hosting__EnableHttpMcpBridge";
        var old = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "true");

            var cfg = ConfigLoader.Load(basePath: dir.Path);

            Assert.True(cfg.Hosting.EnableHttpMcpBridge);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, old);
        }
    }
}
