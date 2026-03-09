using CoffeeshopCli.Configuration;
using Xunit;

namespace CoffeeshopCli.Tests.Configuration;

public sealed class ConfigLoaderTests
{
    [Fact]
    public void Load_UsesFileSkillsDirectory()
    {
        using var fixture = new TestFixtures.TempDirectoryFixture();
        var configDir = Path.Combine(fixture.Path, ".config", "coffeeshop-cli");
        Directory.CreateDirectory(configDir);

        var configPath = Path.Combine(configDir, "config.json");
        File.WriteAllText(configPath, """
{
  "discovery": {
    "skills_directory": "/tmp/file-skills"
  }
}
""");

        var cfg = ConfigLoader.Load(explicitConfigPath: configPath);

        Assert.Equal("/tmp/file-skills", cfg.Discovery.SkillsDirectory);
    }

    [Fact]
    public void Load_UsesEnvOverFile()
    {
        using var fixture = new TestFixtures.TempDirectoryFixture();
        var configPath = Path.Combine(fixture.Path, "config.json");
        File.WriteAllText(configPath, """
{
  "discovery": {
    "skills_directory": "/tmp/file-skills"
  }
}
""");

        var key = "COFFEESHOP_SKILLS_DIR";
        var old = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "/tmp/env-skills");

            var cfg = ConfigLoader.Load(explicitConfigPath: configPath);
            Assert.Equal("/tmp/env-skills", cfg.Discovery.SkillsDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, old);
        }
    }

    [Fact]
    public void Load_UsesCliOverEnvAndFile()
    {
        using var fixture = new TestFixtures.TempDirectoryFixture();
        var configPath = Path.Combine(fixture.Path, "config.json");
        File.WriteAllText(configPath, """
{
  "discovery": {
    "skills_directory": "/tmp/file-skills"
  }
}
""");

        var key = "COFFEESHOP_SKILLS_DIR";
        var old = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "/tmp/env-skills");

            var cfg = ConfigLoader.Load(explicitConfigPath: configPath, cliSkillsDirectory: "/tmp/cli-skills");
            Assert.Equal("/tmp/cli-skills", cfg.Discovery.SkillsDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, old);
        }
    }
}
