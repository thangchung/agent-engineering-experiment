using System.Text.RegularExpressions;

namespace McpServer.Search;

/// <summary>
/// Provides deterministic text normalization and tokenization for search scoring.
/// </summary>
public static partial class TextNormalizer
{
    /// <summary>
    /// Splits text into unique lowercase tokens using separators and punctuation boundaries.
    /// </summary>
    public static string[] Tokenize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return SplitRegex()
            .Split(value.Trim().ToLowerInvariant())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex SplitRegex();
}
