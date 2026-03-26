using System.Text.Json.Serialization;

namespace McpServer.CodeMode;

/// <summary>
/// Controls how much schema detail is returned by discovery APIs.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SchemaDetailLevel>))]
public enum SchemaDetailLevel
{
    Brief = 0,
    Detailed = 1,
    Full = 2,
}
