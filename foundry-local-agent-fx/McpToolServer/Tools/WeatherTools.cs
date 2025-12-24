using System.ComponentModel;
using ModelContextProtocol.Server;

namespace McpToolServer.Tools;

/// <summary>
/// Weather-related MCP tools.
/// </summary>
[McpServerToolType]
public static class WeatherTools
{
    /// <summary>
    /// Gets current weather for a city (mock implementation).
    /// </summary>
    [McpServerTool(Name = "get_weather"), Description("Get the current weather for a specified city.")]
    public static string GetWeather(
        [Description("The city name to get weather for")] string city)
    {
        // Mock weather data - in production, call real weather API
        var weathers = new[]
        {
            ("sunny", 25, 60),
            ("cloudy", 18, 75),
            ("rainy", 15, 90),
            ("windy", 20, 55),
            ("partly cloudy", 22, 65)
        };
        
        var (condition, temp, humidity) = weathers[Math.Abs(city.GetHashCode()) % weathers.Length];
        
        return $"""
            Weather in {city}:
            - Condition: {condition}
            - Temperature: {temp}°C
            - Humidity: {humidity}%
            - Wind: {10 + (Math.Abs(city.GetHashCode()) % 20)} km/h
            """;
    }

    /// <summary>
    /// Gets typical/historical weather for a city and date.
    /// </summary>
    [McpServerTool(Name = "get_typical_weather"), Description("Get the typical/historical weather for a specified city and date/time.")]
    public static string GetTypicalWeather(
        [Description("The city name")] string city,
        [Description("The date and time in ISO format (e.g., '2025-01-15T14:00:00')")] string datetime)
    {
        DateTime.TryParse(datetime, out var parsedDate);
        if (parsedDate == default)
        {
            parsedDate = DateTime.Now;
        }
        
        // Mock typical weather based on month
        var month = parsedDate.Month;
        var (condition, tempRange) = month switch
        {
            12 or 1 or 2 => ("cold and possibly snowy", "0-10°C"),
            3 or 4 or 5 => ("mild with occasional rain", "10-20°C"),
            6 or 7 or 8 => ("warm and sunny", "20-30°C"),
            _ => ("cool with changing weather", "10-18°C")
        };
        
        return $"""
            Typical weather in {city} around {parsedDate:MMMM dd}:
            - Conditions: {condition}
            - Temperature range: {tempRange}
            - This is historical average data
            """;
    }
}
