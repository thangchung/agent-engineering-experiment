using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentService;

/// <summary>
/// Parses tool calls from Foundry Local response content.
/// Foundry Local/Qwen returns function calls as JSON in content, not in tool_calls field.
/// </summary>
public static partial class ToolCallParser
{
    /// <summary>
    /// Extracts all tool calls from the model's response content.
    /// Supports multiple tool calls in a single response.
    /// </summary>
    public static List<ToolCall> Parse(string? content)
    {
        var results = new List<ToolCall>();
        
        if (string.IsNullOrWhiteSpace(content))
            return results;

        // Try multiple patterns for flexibility
        
        // Pattern 1: Find all standard format {"name": "...", "arguments": {...}}
        var matches = ToolCallRegex().Matches(content);
        foreach (Match match in matches)
        {
            var toolCall = ParseMatch(match);
            if (toolCall is not null)
                results.Add(toolCall);
        }
        
        if (results.Count > 0)
            return results;

        // Pattern 2: Try to find all JSON objects with "name" field
        var jsonMatches = JsonObjectRegex().Matches(content);
        foreach (Match jsonMatch in jsonMatches)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<ToolCall>(jsonMatch.Value, JsonOptions);
                if (parsed?.Name is not null)
                    results.Add(parsed);
            }
            catch { }
        }
        
        if (results.Count > 0)
            return results;

        // Pattern 3: Try parsing entire content as JSON (single tool call)
        try
        {
            var trimmed = content.Trim();
            if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
            {
                var parsed = JsonSerializer.Deserialize<ToolCall>(trimmed, JsonOptions);
                if (parsed?.Name is not null)
                    results.Add(parsed);
            }
        }
        catch { }

        return results;
    }

    private static ToolCall? ParseMatch(Match match)
    {
        try
        {
            var name = match.Groups["name"].Value;
            var argsJson = match.Groups["args"].Value;
            
            var args = string.IsNullOrEmpty(argsJson) 
                ? new Dictionary<string, JsonElement>() 
                : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsJson, JsonOptions);

            return new ToolCall(name, args ?? []);
        }
        catch
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    // Match: {"name": "tool_name", "arguments": {...}}
    [GeneratedRegex("""
        \{\s*"name"\s*:\s*"(?<name>\w+)"\s*,\s*"arguments"\s*:\s*(?<args>\{[^}]*\})\s*\}
        """, RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline)]
    private static partial Regex ToolCallRegex();

    // Match any JSON object
    [GeneratedRegex(@"\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}", RegexOptions.Singleline)]
    private static partial Regex JsonObjectRegex();
}

/// <summary>
/// Represents a parsed tool call from the model.
/// </summary>
public record ToolCall(
    string Name,
    Dictionary<string, JsonElement> Arguments)
{
    public string? GetString(string key) =>
        Arguments.TryGetValue(key, out var value) ? value.GetString() : null;

    public int? GetInt(string key) =>
        Arguments.TryGetValue(key, out var value) && value.TryGetInt32(out var i) ? i : null;

    public double? GetDouble(string key) =>
        Arguments.TryGetValue(key, out var value) && value.TryGetDouble(out var d) ? d : null;
}
