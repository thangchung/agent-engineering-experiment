using System.Text.Json;

namespace CoffeeshopCli.Infrastructure;

/// <summary>
/// Centralized JSON serialization helper.
/// DRY principle: avoid repeating JsonSerializerOptions across commands.
/// </summary>
public static class JsonHelper
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static string ToJson(object data)
    {
        return JsonSerializer.Serialize(data, Options);
    }

    public static T? FromJson<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options);
    }
}
