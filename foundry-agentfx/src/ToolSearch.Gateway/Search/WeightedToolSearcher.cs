using ToolSearch.Gateway.Registry;

namespace ToolSearch.Gateway.Search;

public sealed class WeightedToolSearcher : IToolSearcher
{
    private const int ExactNameWeight = 100;
    private const int NameTokenWeight = 30;
    private const int DescriptionTokenWeight = 10;
    private const int ParameterNameTokenWeight = 8;
    private const int ParameterDescriptionTokenWeight = 4;
    private const int TagTokenWeight = 3;

    private readonly IToolRegistry _registry;
    private readonly Dictionary<string, ToolSearchEntry> _searchIndex;

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

    public IReadOnlyList<ToolDescriptor> Search(string query, int limit, UserContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(context);

        if (limit == 0)
            return [];
        if (limit < 0)
            limit = 10;

        string normalizedQuery = query.Trim().ToLowerInvariant();
        string[] queryTokens = TextNormalizer.Tokenize(query);
        IReadOnlyList<ToolDescriptor> candidates = _registry.GetVisibleTools(context);

        return candidates
            .Select((tool, index) =>
            {
                int score = _searchIndex.TryGetValue(tool.Name, out ToolSearchEntry? entry)
                    ? Score(entry, normalizedQuery, queryTokens)
                    : 0;
                return (tool, score, index);
            })
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.index)
            .Take(limit)
            .Select(x => x.tool)
            .ToArray();
    }

    private static int Score(ToolSearchEntry entry, string normalizedQuery, string[] queryTokens)
    {
        int score = 0;

        if (entry.LowerName.Equals(normalizedQuery, StringComparison.Ordinal))
            score += ExactNameWeight;

        foreach (string token in queryTokens)
        {
            if (entry.NameTokens.Contains(token)) score += NameTokenWeight;
            if (entry.DescriptionTokens.Contains(token)) score += DescriptionTokenWeight;
            if (entry.TagTokens.Contains(token)) score += TagTokenWeight;
            score += ScoreSchemaFields(entry.LowerSchema, token);
        }

        return score;
    }

    private static int ScoreSchemaFields(string lowerSchema, string token)
    {
        if (string.IsNullOrWhiteSpace(lowerSchema))
            return 0;

        int score = 0;
        if (lowerSchema.Contains($"\"{token}\"", StringComparison.Ordinal))
            score += ParameterNameTokenWeight;
        if (lowerSchema.Contains($":\"{token}", StringComparison.Ordinal) ||
            lowerSchema.Contains($" {token}", StringComparison.Ordinal))
            score += ParameterDescriptionTokenWeight;
        return score;
    }

    private sealed record ToolSearchEntry(
        ToolDescriptor Tool,
        string LowerName,
        HashSet<string> NameTokens,
        HashSet<string> DescriptionTokens,
        HashSet<string> TagTokens,
        string LowerSchema);
}
