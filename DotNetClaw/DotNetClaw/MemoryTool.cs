using System.ComponentModel;

namespace DotNetClaw;

/// <summary>
/// Agent tools for persistent working memory.
///
/// Separate tools per file — constrains the agent to the 3-file protocol
/// and prevents random file creation in .working-memory/.
///
/// Reading is automatic (MindLoader injects all 3 files at session start).
/// Writing requires these tools because the agent decides WHEN to write.
/// </summary>
public sealed class MemoryTool(string mindRoot, ILogger<MemoryTool> logger)
{
    private readonly string _wmDir = Path.Combine(Path.GetFullPath(mindRoot), ".working-memory");

    // ── Log (append-only, raw session observations) ──────────────────

    [Description("Append an observation to the session log. Use for: decisions made, " +
                 "things learned, session handover notes. Raw stream of consciousness — " +
                 "write freely, consolidate to memory.md later.")]
    public async Task<string> AppendLogAsync(
        [Description("The log entry to append")] string entry,
        CancellationToken ct = default)
    {
        logger.LogInformation("[MemoryTool] AppendLog called: {Entry}", entry);
        var path = Path.Combine(_wmDir, "log.md");
        Directory.CreateDirectory(_wmDir);
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
        await File.AppendAllTextAsync(path, $"- [{timestamp}] {entry}\n", ct);
        return "Logged.";
    }

    // ── Rules (append-only, lessons from mistakes) ───────────────────

    [Description("Add an operational rule learned from a mistake or discovery. " +
                 "Format: one-liner that prevents the mistake recurring. " +
                 "Example: 'Always check if file exists before reading.'")]
    public async Task<string> AddRuleAsync(
        [Description("The rule to add (one concise line)")] string rule,
        CancellationToken ct = default)
    {
        logger.LogInformation("[MemoryTool] AddRule called: {Rule}", rule);
        var path = Path.Combine(_wmDir, "rules.md");
        Directory.CreateDirectory(_wmDir);

        if (!File.Exists(path))
            await File.WriteAllTextAsync(path, "# Rules\n\n", ct);

        await File.AppendAllTextAsync(path, $"- {rule}\n", ct);
        return $"Rule added: {rule}";
    }

    // ── Memory (curated facts, rewritten during consolidation) ───────

    [Description("Save a durable fact to long-term memory. Use sparingly — only for " +
                 "important facts that should survive across sessions (user preferences, " +
                 "project context, key dates). This file is read at every session start.")]
    public async Task<string> SaveFactAsync(
        [Description("The fact to remember")] string fact,
        CancellationToken ct = default)
    {
        logger.LogInformation("[MemoryTool] SaveFact called: {Fact}", fact);
        var path = Path.Combine(_wmDir, "memory.md");
        Directory.CreateDirectory(_wmDir);

        if (!File.Exists(path))
            await File.WriteAllTextAsync(path, "# Working Memory\n\n", ct);

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd");
        await File.AppendAllTextAsync(path, $"- [{timestamp}] {fact}\n", ct);
        return $"Remembered: {fact}";
    }
}
