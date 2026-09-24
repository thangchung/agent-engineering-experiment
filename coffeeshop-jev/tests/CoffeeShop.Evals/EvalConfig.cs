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

    public static string? JevApiKey => Get("JEV_API_KEY", "jev-api-key");

    public static string? OpenAiBaseUrl => Get("OPENAI_BASE_URL", "Parameters:openai-base-url");

    public static string? OpenAiApiKey => Get("OPENAI_API_KEY", "openai-api-key");

    public static string? OpenAiModel => Get("OPENAI_MODEL", "Parameters:openai-model");

    /// <summary>Fails fast, by design (2026-09-24 decision): a live eval test run with no
    /// OpenJev configured is a setup mistake, not a normal offline run - throwing here, instead
    /// of skipping, tells the human exactly what to set instead of leaving a silent "Skipped"
    /// they might not notice.</summary>
    public static void RequireOpenJev() => Require(
        OpenJevUrl,
        "OpenJev is not configured (OpenJevUrl is null/empty). Set env var OPENJEV_URL, or run:\n" +
        "  cd src/AppHost && dotnet user-secrets set \"openjev-url\" \"http://<host>:<port>\"\n" +
        "See README.md.");

    /// <summary>Same fail-fast policy as <see cref="RequireOpenJev"/>, for the OpenAI-compatible
    /// chat endpoint the 3 agents and the JevJudge escalation tier use.</summary>
    public static void RequireOpenAi()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(OpenAiBaseUrl))
        {
            missing.Add("openai-base-url / OPENAI_BASE_URL");
        }

        if (string.IsNullOrWhiteSpace(OpenAiApiKey))
        {
            missing.Add("openai-api-key / OPENAI_API_KEY");
        }

        if (string.IsNullOrWhiteSpace(OpenAiModel))
        {
            missing.Add("openai-model / OPENAI_MODEL");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"OpenAI chat endpoint is not configured. Missing: {string.Join(", ", missing)}. Set the env vars, or run:\n" +
                "  cd src/AppHost && dotnet user-secrets set \"openai-base-url\" \"...\"\n" +
                "  cd src/AppHost && dotnet user-secrets set \"openai-api-key\" \"...\"\n" +
                "  cd src/AppHost && dotnet user-secrets set \"openai-model\" \"...\"\n" +
                "See README.md.");
        }
    }

    /// <summary>The fail-fast primitive both Require* methods use - pure and testable on its
    /// own (<see cref="SmokeTests"/>), unlike testing RequireOpenJev/RequireOpenAi directly,
    /// which would depend on whatever happens to be configured on the machine running the test.</summary>
    public static void Require(string? value, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(errorMessage);
        }
    }

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
