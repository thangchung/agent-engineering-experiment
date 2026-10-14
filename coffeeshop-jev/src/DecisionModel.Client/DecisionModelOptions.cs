namespace DecisionModel.Client;

/// <summary>
/// Options for <see cref="DecisionModelClient"/>. research.md §5.5.
/// </summary>
public sealed class DecisionModelOptions
{
    /// <summary>The DecisionModel model id sent on every request. "jev-latest" is DecisionModel's own alias for the
    /// current model; OpenJev also accepts "openjev-latest".</summary>
    public string Model { get; set; } = "jev-latest";

    /// <summary>Bearer token. Left null/empty when the server has no <c>OPENJEV_API_KEY</c> (config key <c>DecisionModel:ApiKey</c>) set.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Request path under the base URL. Microsoft Foundry serves <c>providers/microsoft/v1/systemone</c>.</summary>
    public string Path { get; set; } = "v1/systemone";

    /// <summary>When set, the key goes in this header (Foundry: <c>api-key</c>) instead of <c>Authorization: Bearer</c>.</summary>
    public string? ApiKeyHeader { get; set; }

    /// <summary>Supplies a bearer token per request (Microsoft Entra ID). Wins over <see cref="ApiKey"/>.</summary>
    public Func<CancellationToken, ValueTask<string>>? TokenProvider { get; set; }

    /// <summary>Shown as <c>gen_ai.provider.name</c> on the client span.</summary>
    public string ProviderName { get; set; } = "decision-model";

    /// <summary>When true, the DecisionModel question/answer bodies are attached to the trace span
    /// (gen_ai.input.messages / gen_ai.output.messages, opt-in per the GenAI semconv - same
    /// dev-only policy as the 3 agents' EnableSensitiveData).</summary>
    public bool CaptureContent { get; set; }
}
