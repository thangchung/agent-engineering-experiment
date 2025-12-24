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
    /// Extracts a tool call from the model's response content.
    /// </summary>
    public static ToolCall? Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        // Try multiple patterns for flexibility
        
        // Pattern 1: Standard format {"name": "...", "arguments": {...}}
        var match = ToolCallRegex().Match(content);
        if (match.Success)
        {
            return ParseMatch(match);
        }

        // Pattern 2: Try to find any JSON object with "name" field
        var jsonMatch = JsonObjectRegex().Match(content);
        if (jsonMatch.Success)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<ToolCall>(jsonMatch.Value, JsonOptions);
                if (parsed?.Name is not null)
                    return parsed;
            }
            catch { }
        }

        // Pattern 3: Try parsing entire content as JSON
        try
        {
            var trimmed = content.Trim();
            if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
            {
                var parsed = JsonSerializer.Deserialize<ToolCall>(trimmed, JsonOptions);
                if (parsed?.Name is not null)
                    return parsed;
            }
        }
        catch { }

        return null;
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
