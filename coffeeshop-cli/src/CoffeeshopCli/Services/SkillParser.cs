using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CoffeeshopCli.Services;

/// <summary>
/// Parses SKILL.md files: extracts YAML frontmatter + markdown body.
/// </summary>
public sealed class SkillParser
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Parse a SKILL.md file: extract YAML frontmatter between --- delimiters,
    /// deserialize it, and return the remaining markdown body.
    /// </summary>
    public SkillManifest Parse(string content)
    {
        var lines = content.Split('\n');
        if (lines.Length < 2 || lines[0].Trim() != "---")
        {
            return new SkillManifest { Body = content };
        }

        var endIndex = -1;
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                endIndex = i;
                break;
            }
        }

        if (endIndex < 0)
        {
            return new SkillManifest { Body = content };
        }

        var yamlBlock = string.Join('\n', lines[1..endIndex]);
        var body = string.Join('\n', lines[(endIndex + 1)..]).TrimStart();

        var frontmatter = YamlDeserializer.Deserialize<SkillFrontmatter>(yamlBlock);
        return new SkillManifest { Frontmatter = frontmatter, Body = body };
    }
}

public record SkillFrontmatter
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string License { get; init; } = "";
    public string Compatibility { get; init; } = "";
    public SkillMetadata Metadata { get; init; } = new();
}

public record SkillMetadata
{
    public string Author { get; init; } = "";
    public string Version { get; init; } = "";
    public string Category { get; init; } = "";
    public string LoopType { get; init; } = "";
}

public record SkillManifest
{
    public SkillFrontmatter Frontmatter { get; init; } = new();
    public string Body { get; init; } = "";
}
