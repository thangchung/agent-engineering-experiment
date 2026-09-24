using Xunit;

namespace CoffeeShop.Evals;

/// <summary>
/// A fact that only runs when the live Jev environment variable is set.
/// Without it, the test is reported as Skipped, not Failed (research.md T01 AC3).
/// </summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(EvalConfig.OpenJevUrl))
        {
            Skip = "OPENJEV_URL is not set (env var or AppHost Parameters) - skipping live Jev test.";
        }
    }
}

/// <summary>
/// A fact that only runs when a real OpenAI-compatible chat endpoint is configured.
/// </summary>
public sealed class LiveOpenAiFactAttribute : FactAttribute
{
    public LiveOpenAiFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(EvalConfig.OpenAiBaseUrl) ||
            string.IsNullOrWhiteSpace(EvalConfig.OpenAiApiKey) ||
            string.IsNullOrWhiteSpace(EvalConfig.OpenAiModel))
        {
            Skip = "OpenAI base url/api key/model are not all available (env vars or AppHost Parameters/user-secrets) - skipping live OpenAI test.";
        }
    }
}
