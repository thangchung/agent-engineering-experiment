using GitHub.Copilot.SDK;

namespace McpServer.Services;

public static class CopilotProviderFactory
{
    public static ProviderConfig Create(CopilotProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string? baseUrl = ResolveValue(options.BaseUrl);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Copilot:Provider:BaseUrl is required when Copilot:Auth:Type=byok.");
        }

        ProviderConfig provider = new()
        {
            Type = NormalizeValue(options.Type) ?? "openai",
            BaseUrl = baseUrl,
            ApiKey = ResolveValue(options.ApiKey),
            BearerToken = ResolveValue(options.BearerToken),
            WireApi = NormalizeValue(options.WireApi) ?? "completions"
        };

        string? apiVersion = ResolveValue(options.Azure.ApiVersion);
        if (!string.IsNullOrWhiteSpace(apiVersion))
        {
            provider.Azure = new AzureOptions
            {
                ApiVersion = apiVersion
            };
        }

        return provider;
    }

    private static string? ResolveValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        if (trimmed.Length > 3 && trimmed[0] == '$' && trimmed[1] == '{' && trimmed[^1] == '}')
        {
            string environmentName = trimmed[2..^1].Trim();
            if (!string.IsNullOrWhiteSpace(environmentName))
            {
                return Environment.GetEnvironmentVariable(environmentName);
            }
        }

        return trimmed;
    }

    private static string? NormalizeValue(string? value)
    {
        string? resolved = ResolveValue(value);
        return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
    }
}