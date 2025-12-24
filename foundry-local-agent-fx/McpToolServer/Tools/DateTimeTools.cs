using System.ComponentModel;
using ModelContextProtocol.Server;

namespace McpToolServer.Tools;

/// <summary>
/// Date/time related MCP tools.
/// </summary>
[McpServerToolType]
public static class DateTimeTools
{
    private static readonly Dictionary<string, string> TimezoneAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sydney"] = "AUS Eastern Standard Time",
        ["Melbourne"] = "AUS Eastern Standard Time",
        ["London"] = "GMT Standard Time",
        ["New York"] = "Eastern Standard Time",
        ["Los Angeles"] = "Pacific Standard Time",
        ["Tokyo"] = "Tokyo Standard Time",
        ["Paris"] = "Romance Standard Time",
        ["Berlin"] = "W. Europe Standard Time",
        ["Singapore"] = "Singapore Standard Time",
        ["Hong Kong"] = "China Standard Time",
        ["Dubai"] = "Arabian Standard Time",
        ["Mumbai"] = "India Standard Time",
    };

    /// <summary>
    /// Gets current date/time for a timezone.
    /// </summary>
    [McpServerTool(Name = "get_datetime"), Description("Get the current date and time in a specified timezone.")]
    public static string GetDateTime(
        [Description("The timezone string (e.g., 'AUS Eastern Standard Time', 'Pacific Standard Time', 'UTC') or city name (e.g., 'Sydney', 'New York')")] string timezone)
    {
        try
        {
            if (string.IsNullOrEmpty(timezone))
            {
                return $"Current UTC time: {DateTime.UtcNow:F}";
            }
            
            // Try to resolve timezone alias (city name)
            if (TimezoneAliases.TryGetValue(timezone, out var resolvedTz))
            {
                timezone = resolvedTz;
            }
            
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            return $"Current time in {timezone}: {localTime:F}";
        }
        catch (TimeZoneNotFoundException)
        {
            return $"Unknown timezone: '{timezone}'. Try city names like 'Sydney', 'New York', or standard timezone IDs like 'UTC', 'Pacific Standard Time'.";
        }
    }
}
