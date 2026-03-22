using System.Text.Json;

namespace McpServer.UnitTests;

internal static class TestJson
{
    public static JsonElement Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
