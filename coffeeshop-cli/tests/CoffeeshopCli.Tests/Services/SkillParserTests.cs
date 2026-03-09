using CoffeeshopCli.Services;
using Xunit;

namespace CoffeeshopCli.Tests.Services;

public class SkillParserTests
{
    private readonly SkillParser _parser = new();

    [Fact]
    public void Parse_ValidSkillMd_ExtractsFrontmatterAndBody()
    {
        var content = @"---
name: test-skill
description: A test skill
license: MIT
compatibility: Test environment
metadata:
  author: test-author
  version: ""1.0""
  category: test
  loop-type: agentic
---

# Test Skill

This is the body content.
";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.Equal("test-skill", result.Frontmatter.Name);
        Assert.Equal("A test skill", result.Frontmatter.Description);
        Assert.Equal("MIT", result.Frontmatter.License);
        Assert.Equal("Test environment", result.Frontmatter.Compatibility);
        Assert.Equal("test-author", result.Frontmatter.Metadata.Author);
        Assert.Equal("1.0", result.Frontmatter.Metadata.Version);
        Assert.Equal("test", result.Frontmatter.Metadata.Category);
        Assert.Equal("agentic", result.Frontmatter.Metadata.LoopType);
        Assert.Contains("# Test Skill", result.Body);
        Assert.Contains("This is the body content.", result.Body);
    }

    [Fact]
    public void Parse_NoFrontmatter_ReturnsBodyOnly()
    {
        var content = "# Test Skill\n\nNo frontmatter here.";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.Equal("", result.Frontmatter.Name);
        Assert.Equal(content, result.Body);
    }

    [Fact]
    public void Parse_IncompleteYaml_ReturnsBodyOnly()
    {
        var content = @"---
name: test-skill
# Missing closing ---

Body content";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        // Should return the whole content as body when YAML is malformed
        Assert.Equal(content, result.Body);
    }

    [Fact]
    public void Parse_MultilineDescription_PreservesFormatting()
    {
        var content = @"---
name: test-skill
description: >
  This is a multiline
  description that should
  be preserved.
metadata:
  version: ""1.0""
  category: test
  loop-type: simple
---

Body";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.Contains("multiline", result.Frontmatter.Description);
        Assert.Contains("description", result.Frontmatter.Description);
    }

    [Fact]
    public void Parse_HyphenatedFieldName_ConvertsToProperty()
    {
        var content = @"---
name: test-skill
description: Test
metadata:
  loop-type: agentic
  version: ""1.0""
  category: test
---

Body";

        var result = _parser.Parse(content);

        Assert.Equal("agentic", result.Frontmatter.Metadata.LoopType);
    }
}
