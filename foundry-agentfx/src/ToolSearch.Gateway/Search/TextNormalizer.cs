using System.Text.RegularExpressions;

namespace ToolSearch.Gateway.Search;

public static partial class TextNormalizer
{
    public static string[] Tokenize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return SplitRegex()
            .Split(value.Trim().ToLowerInvariant())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex SplitRegex();
}
