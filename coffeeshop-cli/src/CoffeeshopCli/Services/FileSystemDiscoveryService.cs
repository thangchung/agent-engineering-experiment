namespace CoffeeshopCli.Services;

/// <summary>
/// Discovers models from assembly reflection and skills from filesystem.
/// </summary>
public sealed class FileSystemDiscoveryService : IDiscoveryService
{
    private readonly ModelRegistry _modelRegistry;
    private readonly string _skillsDirectory;

    public FileSystemDiscoveryService(ModelRegistry modelRegistry, string skillsDirectory = "./skills")
    {
        _modelRegistry = modelRegistry;
        _skillsDirectory = skillsDirectory;
    }

    public IReadOnlyList<ModelInfo> DiscoverModels()
    {
        var models = new List<ModelInfo>();

        foreach (var modelName in _modelRegistry.GetModelNames())
        {
            var type = _modelRegistry.GetModelType(modelName);
            if (type == null) continue;

            var propertyCount = type.GetProperties().Length;

            models.Add(new ModelInfo
            {
                Name = modelName,
                Type = type,
                PropertyCount = propertyCount
            });
        }

        return models;
    }

    public IReadOnlyList<SkillInfo> DiscoverSkills()
    {
        var parser = new SkillParser();
        var filesystemSkills = DiscoverSkillsFromFileSystem(parser);

        if (filesystemSkills.Count > 0)
        {
            return filesystemSkills;
        }

        return DiscoverSkillsFromEmbeddedResources(parser);
    }

    private List<SkillInfo> DiscoverSkillsFromFileSystem(SkillParser parser)
    {
        var skills = new List<SkillInfo>();

        if (!Directory.Exists(_skillsDirectory))
        {
            return skills;
        }

        // Scan for */SKILL.md files
        foreach (var skillDir in Directory.GetDirectories(_skillsDirectory))
        {
            var skillFile = Path.Combine(skillDir, "SKILL.md");
            if (!File.Exists(skillFile)) continue;

            try
            {
                var content = File.ReadAllText(skillFile);
                var manifest = parser.Parse(content);

                skills.Add(new SkillInfo
                {
                    Name = manifest.Frontmatter.Name,
                    Description = manifest.Frontmatter.Description,
                    Version = manifest.Frontmatter.Metadata.Version,
                    Category = manifest.Frontmatter.Metadata.Category,
                    LoopType = manifest.Frontmatter.Metadata.LoopType,
                    Path = skillFile,
                    Content = null
                });
            }
            catch
            {
                // Skip malformed SKILL.md files
                continue;
            }
        }

        return skills;
    }

    private static List<SkillInfo> DiscoverSkillsFromEmbeddedResources(SkillParser parser)
    {
        var skills = new List<SkillInfo>();
        var assembly = typeof(FileSystemDiscoveryService).Assembly;

        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".SKILL.md", StringComparison.OrdinalIgnoreCase));

        foreach (var resourceName in resourceNames)
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is null)
                {
                    continue;
                }

                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();
                var manifest = parser.Parse(content);

                skills.Add(new SkillInfo
                {
                    Name = manifest.Frontmatter.Name,
                    Description = manifest.Frontmatter.Description,
                    Version = manifest.Frontmatter.Metadata.Version,
                    Category = manifest.Frontmatter.Metadata.Category,
                    LoopType = manifest.Frontmatter.Metadata.LoopType,
                    Path = $"embedded://{resourceName}",
                    Content = content
                });
            }
            catch
            {
                // Skip malformed embedded SKILL.md resources.
                continue;
            }
        }

        return skills;
    }
}
