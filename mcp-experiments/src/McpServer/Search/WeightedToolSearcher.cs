using McpServer.Registry;

namespace McpServer.Search;

/// <summary>
/// Applies in-house weighted token scoring across tool metadata fields.
/// </summary>
public sealed class WeightedToolSearcher(IToolRegistry registry) : IToolSearcher
{
    private const int ExactNameWeight = 100;
    private const int NameTokenWeight = 30;
    private const int DescriptionTokenWeight = 10;
    private const int ParameterNameTokenWeight = 8;
    private const int ParameterDescriptionTokenWeight = 4;
    private const int TagTokenWeight = 3;

    /// <summary>
    /// Searches tools for a given query and returns top ranked results.
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
        IReadOnlyList<ToolDescriptor> candidates = registry.GetVisibleTools(context);

        return candidates
            .Select((tool, index) => new RankedTool(tool, Score(tool, normalizedQuery, queryTokens), index))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Index)
            .Take(limit)
            .Select(item => item.Tool)
            .ToArray();
    }

    /// <summary>
    /// Computes deterministic score for one tool descriptor.
    /// </summary>
    private static int Score(ToolDescriptor tool, string normalizedQuery, string[] queryTokens)
    {
        int score = 0;
        string lowerName = tool.Name.ToLowerInvariant();
        string lowerDescription = tool.Description.ToLowerInvariant();
        string[] nameTokens = TextNormalizer.Tokenize(tool.Name);
        string[] descriptionTokens = TextNormalizer.Tokenize(tool.Description);
        string[] tagTokens = tool.Tags.SelectMany(TextNormalizer.Tokenize).ToArray();

        if (lowerName.Equals(normalizedQuery, StringComparison.Ordinal))
        {
            score += ExactNameWeight;
        }

        foreach (string token in queryTokens)
        {
            if (nameTokens.Contains(token, StringComparer.Ordinal))
            {
                score += NameTokenWeight;
            }

            if (descriptionTokens.Contains(token, StringComparer.Ordinal))
            {
                score += DescriptionTokenWeight;
            }

            if (tagTokens.Contains(token, StringComparer.Ordinal))
            {
                score += TagTokenWeight;
            }

            score += ScoreSchemaFields(tool.InputJsonSchema, token);
        }

        return score;
    }

    /// <summary>
    /// Heuristically scores schema text by checking parameter-like keys and descriptions.
    /// </summary>
    private static int ScoreSchemaFields(string schema, string token)
    {
        if (string.IsNullOrWhiteSpace(schema))
        {
            return 0;
        }

        string lowerSchema = schema.ToLowerInvariant();
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
}
