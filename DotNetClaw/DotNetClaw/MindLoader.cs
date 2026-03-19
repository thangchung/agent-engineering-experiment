namespace DotNetClaw;

public sealed class MindLoader(IConfiguration config)
{
    private readonly string _mindRoot = config["Mind:Path"] ?? "./mind";

    public async Task<string> LoadSystemMessageAsync(CancellationToken ct = default)
    {
        var soulPath = Path.Combine(_mindRoot, "SOUL.md");
        if (!File.Exists(soulPath))
            throw new FileNotFoundException($"SOUL.md not found at {Path.GetFullPath(soulPath)}. " +
                "Create the mind directory structure first.");

        var soul = await File.ReadAllTextAsync(soulPath, ct);
        var parts = new List<string> { soul };

        var agentsDir = Path.Combine(_mindRoot, ".github", "agents");
        if (Directory.Exists(agentsDir))
        {
            var agentFiles = Directory.GetFiles(agentsDir, "*.agent.md", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.Ordinal);

            foreach (var f in agentFiles)
            {
                var content = await File.ReadAllTextAsync(f, ct);
                parts.Add(StripFrontmatter(content));
            }
        }

        // Log trimmed to last 50 lines to limit token cost.
        var wmDir = Path.Combine(_mindRoot, ".working-memory");
        if (Directory.Exists(wmDir))
        {
            var memoryParts = new List<string>();
            foreach (var fileName in new[] { "memory.md", "rules.md", "log.md" })
            {
                var path = Path.Combine(wmDir, fileName);
                if (!File.Exists(path)) continue;

                var content = await File.ReadAllTextAsync(path, ct);
                if (string.IsNullOrWhiteSpace(content)) continue;

                if (fileName == "log.md")
                {
                    var lines = content.Split('\n');
                    if (lines.Length > 50)
                        content = string.Join('\n', lines[^50..]);
                }

                memoryParts.Add(content);
            }

            if (memoryParts.Count > 0)
            {
                parts.Add("## Working Memory\n\n" +
                          "These are your persistent files. They survive across sessions.\n\n" +
                          string.Join("\n\n---\n\n", memoryParts));
            }
        }

        return string.Join("\n\n---\n\n", parts);
    }

    public string MindRoot => Path.GetFullPath(_mindRoot);

    private static string StripFrontmatter(string content)
    {
        if (!content.StartsWith("---", StringComparison.Ordinal)) return content;
        var end = content.IndexOf("---", 3, StringComparison.Ordinal);
        return end > 0 ? content[(end + 3)..].TrimStart() : content;
    }
}
