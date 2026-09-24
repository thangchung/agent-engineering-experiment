using Microsoft.Extensions.Configuration;

namespace CoffeeShop.Evals;

/// <summary>
/// Live-eval config: reads the same `Parameters` the AppHost uses (research.md T30+), plus its
/// user-secrets store (`UserSecretsId` below matches `src/AppHost/AppHost.csproj` exactly), so
/// these tests hit the endpoints `aspire run` is already configured for without a developer
/// re-typing them as env vars or a secret ever being pasted into a file. Env vars still win, for
/// CI (T40) where there is no user-secrets store on disk.
/// </summary>
public static class EvalConfig
{
    private const string AppHostUserSecretsId = "351623b9-4467-4592-82cf-46a33cd69d60";

    private static readonly IConfiguration Root = Build();

    public static string? OpenJevUrl => Get("OPENJEV_URL", "Parameters:openjev-url");

    public static string? OpenAiBaseUrl => Get("OPENAI_BASE_URL", "Parameters:openai-base-url");

    public static string? OpenAiApiKey => Get("OPENAI_API_KEY", "openai-api-key");

    public static string? OpenAiModel => Get("OPENAI_MODEL", "Parameters:openai-model");

    private static string? Get(string envVar, string parameterKey) =>
        Environment.GetEnvironmentVariable(envVar) is { Length: > 0 } fromEnv ? fromEnv : Root[parameterKey];

    private static IConfiguration Build()
    {
        var builder = new ConfigurationBuilder();
        if (FindAppHostDirectory() is { } appHostDir)
        {
            builder.SetBasePath(appHostDir)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true);
        }

        return builder.AddUserSecrets(AppHostUserSecretsId).Build();
    }

    private static string? FindAppHostDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CoffeeShop.slnx")))
        {
            dir = dir.Parent;
        }

        return dir is null ? null : Path.Combine(dir.FullName, "src", "AppHost");
    }
}
