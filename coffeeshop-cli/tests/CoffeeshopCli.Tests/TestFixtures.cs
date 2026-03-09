namespace CoffeeshopCli.Tests;

/// <summary>
/// Shared test fixtures for DRY test setup.
/// </summary>
public static class TestFixtures
{
    /// <summary>
    /// Creates a temporary directory that is automatically cleaned up.
    /// </summary>
    public sealed class TempDirectoryFixture : IDisposable
    {
        public string Path { get; }

        public TempDirectoryFixture()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"coffeeshop-test-{Guid.NewGuid()}");
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    /// <summary>
    /// Provides sample SKILL.md content for parser tests.
    /// </summary>
    public static string GetSampleSkillContent()
    {
        return @"---
name: test-skill
description: Test skill for unit tests
license: MIT
compatibility: Test only
metadata:
  author: test
  version: ""1.0""
  category: test
  loop-type: agentic
---

# Test Skill

This is a test skill body.
";
    }

    /// <summary>
    /// Generates a test config.json file.
    /// </summary>
    public static string GetSampleConfigJson()
    {
        return @"{
  ""discovery"": {
    ""skills_directory"": ""./skills""
  }
}";
    }
}
