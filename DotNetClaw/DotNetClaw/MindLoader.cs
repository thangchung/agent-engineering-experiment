namespace DotNetClaw;

/// <summary>
/// Loads the agent's "Mind" — the persistent identity structure from msclaw.
///
/// A Mind is a directory with four parts:
///   SOUL.md                             — personality, mission, boundaries
///   .github/agents/*.agent.md           — behavioral instructions (Copilot CLI reads these natively)
///   .working-memory/memory.md           — curated facts (read every session, rarely written)
///   .working-memory/rules.md            — one-liner lessons from mistakes (append-only)
///   .working-memory/log.md              — raw session log (last 50 lines injected)
///
/// This mirrors msclaw's IdentityLoader:
///   https://github.com/ianphil/msclaw/blob/master/src/MsClaw.Core/Mind/IdentityLoader.cs
/// </summary>
public sealed class MindLoader(IConfiguration config)
{
    private readonly string _mindRoot = config["Mind:Path"] ?? "./mind";

    /// <summary>
    /// Assembles SOUL.md + agent instruction files + working-memory into a single system message.
    /// This becomes MAF's <c>ChatOptions.Instructions</c>.
    /// Working memory is injected so the agent's memory is real — not just referenced by path.
    /// </summary>
    public async Task<string> LoadSystemMessageAsync(CancellationToken ct = default)
    {
        var soulPath = Path.Combine(_mindRoot, "SOUL.md");
        if (!File.Exists(soulPath))
            throw new FileNotFoundException($"SOUL.md not found at {Path.GetFullPath(soulPath)}. " +
                "Create the mind directory structure first.");

        var soul = await File.ReadAllTextAsync(soulPath, ct);
        var parts = new List<string> { soul };

        // Agent instruction files (.github/agents/*.agent.md)
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

        // Working memory — injected so the agent actually sees its memory each session.
        // Token cost is trivial (<2KB typically). Log is trimmed to last 50 lines.
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
