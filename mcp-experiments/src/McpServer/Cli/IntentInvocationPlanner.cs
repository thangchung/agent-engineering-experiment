using System.Text.Json;
using System.Text.RegularExpressions;
using McpServer.Registry;
using McpServer.Tools;

namespace McpServer.Cli;

/// <summary>
/// Infers a concrete tool invocation from a natural-language CLI intent.
/// </summary>
internal static partial class IntentInvocationPlanner
{
    private static readonly string[] LocationParameterHints = ["city", "location", "place", "area", "region", "state", "country", "town"];
    private static readonly string[] SearchParameterHints = ["query", "search", "term", "text", "name", "keyword", "phrase"];

    public static PlannedInvocation Plan(string intent, string? explicitToolName, string? explicitArgsJson, IReadOnlyList<ToolDefinition> candidates)
    {
        if (!string.IsNullOrWhiteSpace(explicitToolName))
        {
            return new PlannedInvocation(explicitToolName, string.IsNullOrWhiteSpace(explicitArgsJson) ? "{}" : explicitArgsJson);
        }

        if (string.IsNullOrWhiteSpace(intent))
        {
            throw new ArgumentException("Provide either --tool <name> or an intent argument.");
        }

        if (!string.IsNullOrWhiteSpace(explicitArgsJson))
        {
            ToolDefinition bestMatch = candidates.FirstOrDefault() ?? throw new ToolNotFoundException(intent);
            return new PlannedInvocation(bestMatch.Name, explicitArgsJson);
        }

        PlannedInvocation? inferredInvocation = TryInferInvocation(intent, candidates);
        if (inferredInvocation is not null)
        {
            return inferredInvocation;
        }

        ToolDefinition fallback = candidates.FirstOrDefault() ?? throw new ToolNotFoundException(intent);
        return new PlannedInvocation(fallback.Name, "{}");
    }

    private static PlannedInvocation? TryInferInvocation(string intent, IReadOnlyList<ToolDefinition> candidates)
    {
        string? location = ExtractCity(intent);
        if (!string.IsNullOrWhiteSpace(location))
        {
            PlannedInvocation? locationInvocation = TryInferByPhrase(candidates, location, LocationParameterHints);
            if (locationInvocation is not null)
            {
                return locationInvocation;
            }
        }

        string? searchText = ExtractNameQuery(intent);
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            PlannedInvocation? searchInvocation = TryInferByPhrase(candidates, searchText, SearchParameterHints);
            if (searchInvocation is not null)
            {
                return searchInvocation;
            }
        }

        return null;
    }

    private static PlannedInvocation? TryInferByPhrase(IReadOnlyList<ToolDefinition> candidates, string phrase, IReadOnlyList<string> parameterHints)
    {
        int bestScore = 0;
        PlannedInvocation? bestInvocation = null;

        foreach (ToolDefinition candidate in candidates)
        {
            foreach (ToolParameter parameter in GetParameters(candidate.InputJsonSchema))
            {
                int score = ScoreParameter(parameter, parameterHints);
                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bestInvocation = new PlannedInvocation(
                    candidate.Name,
                    JsonSerializer.Serialize(new Dictionary<string, string> { [parameter.Name] = phrase }));
            }
        }

        return bestInvocation;
    }

    private static bool ContainsWord(string input, string word)
    {
        return Regex.IsMatch(input, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string? ExtractCity(string intent)
    {
        Match match = CityPattern().Match(intent);
        if (!match.Success)
        {
            return null;
        }

        return NormalizePhrase(match.Groups[1].Value);
    }

    private static string? ExtractNameQuery(string intent)
    {
        Match match = NamePattern().Match(intent);
        if (!match.Success)
        {
            return null;
        }

        return NormalizePhrase(match.Groups[1].Value);
    }

    private static string? NormalizePhrase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim().Trim('.', ',', ';', ':', '!', '?');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static IReadOnlyList<ToolParameter> GetParameters(string schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return [];
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(schemaJson);
            if (!document.RootElement.TryGetProperty("properties", out JsonElement properties) ||
                properties.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            return properties.EnumerateObject()
                .Select(property => new ToolParameter(
                    property.Name,
                    property.Value.TryGetProperty("description", out JsonElement descriptionElement) &&
                    descriptionElement.ValueKind == JsonValueKind.String
                        ? descriptionElement.GetString() ?? string.Empty
                        : string.Empty))
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int ScoreParameter(ToolParameter parameter, IReadOnlyList<string> hints)
    {
        int score = 0;

        foreach (string hint in hints)
        {
            if (ContainsWord(parameter.Name, hint))
            {
                score += 3;
            }

            if (!string.IsNullOrWhiteSpace(parameter.Description) && ContainsWord(parameter.Description, hint))
            {
                score += 2;
            }
        }

        return score;
    }

    [GeneratedRegex(@"\bin\s+([a-z0-9][a-z0-9\s'\-]*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CityPattern();

    [GeneratedRegex(@"\b(?:named|called)\s+([a-z0-9][a-z0-9\s'\-]*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}

internal sealed record ToolParameter(string Name, string Description);

internal sealed record PlannedInvocation(string ToolName, string ArgsJson);