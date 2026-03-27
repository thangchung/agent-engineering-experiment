using McpServer.Registry;

namespace McpServer.Search;

/// <summary>
/// Applies in-house weighted token scoring across tool metadata fields.
/// Token sets and the lowercased schema string are pre-computed at construction time
/// so that each <see cref="Search"/> call pays only O(1) per token per candidate,
/// rather than re-tokenizing and re-lowercasing on every query.
/// </summary>
public sealed class WeightedToolSearcher : IToolSearcher
{
    private const int ExactNameWeight = 100;
    private const int NameTokenWeight = 30;
    private const int DescriptionTokenWeight = 10;
    private const int ParameterNameTokenWeight = 8;
    private const int ParameterDescriptionTokenWeight = 4;
    private const int TagTokenWeight = 3;

    private readonly IToolRegistry _registry;

    /// <summary>
    /// Keyed by tool name (case-insensitive). Admin context is used during index construction
    /// so that admin-only tools are indexed; per-caller visibility is still enforced at query
    /// time via <see cref="IToolRegistry.GetVisibleTools"/>.
    /// </summary>
    private readonly Dictionary<string, ToolSearchEntry> _searchIndex;

    /// <summary>
    /// Initializes the searcher and builds the pre-computed search index for all tools.
    /// </summary>
    public WeightedToolSearcher(IToolRegistry registry)
    {
        _registry = registry;

        IReadOnlyList<ToolDescriptor> allTools = registry.GetVisibleTools(new UserContext(IsAdmin: true));
        _searchIndex = new Dictionary<string, ToolSearchEntry>(allTools.Count, StringComparer.OrdinalIgnoreCase);

        foreach (ToolDescriptor tool in allTools)
        {
            _searchIndex[tool.Name] = new ToolSearchEntry(
                tool,
                tool.Name.ToLowerInvariant(),
                new HashSet<string>(TextNormalizer.Tokenize(tool.Name), StringComparer.Ordinal),
                new HashSet<string>(TextNormalizer.Tokenize(tool.Description), StringComparer.Ordinal),
                new HashSet<string>(tool.Tags.SelectMany(TextNormalizer.Tokenize), StringComparer.Ordinal),
                tool.InputJsonSchema.ToLowerInvariant());
        }
    }

    /// <summary>
    /// Searches visible tools for a given query and returns top-ranked results.
    /// </summary>
    public IReadOnlyList<ToolDescriptor> Search(string query, int limit, UserContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(context);

        if (limit <= 0)
        {
            return [];
        }

        string normalizedQuery = query.Trim().ToLowerInvariant();
        string[] queryTokens = TextNormalizer.Tokenize(query);
        IReadOnlyList<ToolDescriptor> candidates = _registry.GetVisibleTools(context);

        return candidates
            .Select((tool, index) =>
            {
                // Fall back to zero score for tools registered after startup without an index entry.
                int score = _searchIndex.TryGetValue(tool.Name, out ToolSearchEntry? entry)
                    ? Score(entry, normalizedQuery, queryTokens)
                    : 0;
                return new RankedTool(tool, score, index);
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Index)
            .Take(limit)
            .Select(item => item.Tool)
            .ToArray();
    }

    /// <summary>
    /// Computes a deterministic score for one pre-built search entry using O(1) HashSet lookups.
    /// </summary>
    private static int Score(ToolSearchEntry entry, string normalizedQuery, string[] queryTokens)
    {
        int score = 0;

        if (entry.LowerName.Equals(normalizedQuery, StringComparison.Ordinal))
        {
            score += ExactNameWeight;
        }

        foreach (string token in queryTokens)
        {
            if (entry.NameTokens.Contains(token))
            {
                score += NameTokenWeight;
            }

            if (entry.DescriptionTokens.Contains(token))
            {
                score += DescriptionTokenWeight;
            }

            if (entry.TagTokens.Contains(token))
            {
                score += TagTokenWeight;
            }

            score += ScoreSchemaFields(entry.LowerSchema, token);
        }

        return score;
    }

    /// <summary>
    /// Heuristically scores a pre-lowercased schema string for parameter name and description matches.
    /// </summary>
    private static int ScoreSchemaFields(string lowerSchema, string token)
    {
        if (string.IsNullOrWhiteSpace(lowerSchema))
        {
            return 0;
        }

        int score = 0;

        if (lowerSchema.Contains($"\"{token}\"", StringComparison.Ordinal))
        {
            score += ParameterNameTokenWeight;
        }

        if (lowerSchema.Contains($":\"{token}", StringComparison.Ordinal) ||
            lowerSchema.Contains($" {token}", StringComparison.Ordinal))
        {
            score += ParameterDescriptionTokenWeight;
        }

        return score;
    }

    private sealed record RankedTool(ToolDescriptor Tool, int Score, int Index);

    /// <summary>
    /// Per-tool search data pre-computed at construction time to avoid redundant work per query.
    /// </summary>
    private sealed record ToolSearchEntry(
        ToolDescriptor Tool,
        string LowerName,
        HashSet<string> NameTokens,
        HashSet<string> DescriptionTokens,
        HashSet<string> TagTokens,
        string LowerSchema);
}
