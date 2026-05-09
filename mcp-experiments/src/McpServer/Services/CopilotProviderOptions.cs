namespace McpServer.Services;

public sealed class CopilotProviderOptions
{
    public string? Type { get; set; }

    public string? WireApi { get; set; }

    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }

    public string? BearerToken { get; set; }

    public AzureProviderOptions Azure { get; set; } = new();

    public Dictionary<string, string>? Headers { get; set; }

    public string? ModelId { get; set; }

    public string? WireModel { get; set; }

    public sealed class AzureProviderOptions
    {
        public string? ApiVersion { get; set; }
    }
}