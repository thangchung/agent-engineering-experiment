namespace DotNetClaw;

/// <summary>
/// Loads the agent's "Mind" — the persistent identity structure from msclaw.
///
/// A Mind is a directory with three parts:
///   SOUL.md                        — personality, mission, boundaries
///   .github/agents/*.agent.md      — behavioral instructions (Copilot CLI reads these natively)
///   .working-memory/memory.md      — facts the agent accumulates across sessions
///
/// This mirrors msclaw's IdentityLoader exactly:
///   https://github.com/ianphil/msclaw/blob/master/src/MsClaw.Core/Mind/IdentityLoader.cs
/// </summary>
public sealed class MindLoader(IConfiguration config)
{
    private readonly string _mindRoot = config["Mind:Path"] ?? "./mind";

    /// <summary>
    /// Assembles SOUL.md + agent instruction files into a single system message string.
    /// This becomes MAF's <c>ChatOptions.Instructions</c>.
    /// </summary>
    public async Task<string> LoadSystemMessageAsync(CancellationToken ct = default)
    {
        var soulPath = Path.Combine(_mindRoot, "SOUL.md");
        if (!File.Exists(soulPath))
            throw new FileNotFoundException($"SOUL.md not found at {Path.GetFullPath(soulPath)}. " +
                "Create the mind directory structure first.");

        var soul = await File.ReadAllTextAsync(soulPath, ct);

        var agentsDir = Path.Combine(_mindRoot, ".github", "agents");
        if (!Directory.Exists(agentsDir))
            return soul;

        var agentFiles = Directory.GetFiles(agentsDir, "*.agent.md", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.Ordinal);

        var parts = new List<string> { soul };
        foreach (var f in agentFiles)
        {
            var content = await File.ReadAllTextAsync(f, ct);
            parts.Add(StripFrontmatter(content));
        }

        return string.Join("\n\n---\n\n", parts);
    }

    /// <summary>
    /// Absolute path to the mind root directory.
    /// Passed as <c>Cwd</c> to <c>CopilotClientOptions</c> so the Copilot CLI
    /// discovers <c>.github/agents/</c> files natively.
    /// </summary>
    public string MindRoot => Path.GetFullPath(_mindRoot);

    // Strip YAML frontmatter (--- ... ---) from agent files — same logic as msclaw
    private static string StripFrontmatter(string content)
    {
        if (!content.StartsWith("---", StringComparison.Ordinal)) return content;
        var end = content.IndexOf("---", 3, StringComparison.Ordinal);
        return end > 0 ? content[(end + 3)..].TrimStart() : content;
    }
}
