namespace Jev.Client;

/// <summary>
/// Options for <see cref="JevClient"/>. research.md §5.5.
/// </summary>
public sealed class JevOptions
{
    /// <summary>The Jev model id sent on every request. "jev-latest" is Jev's own alias for the
    /// current model; OpenJev also accepts "openjev-latest".</summary>
    public string Model { get; set; } = "jev-latest";

    /// <summary>Bearer token. Left null/empty when the server has no <c>OPENJEV_API_KEY</c> set.</summary>
    public string? ApiKey { get; set; }

    /// <summary>When true, the Jev question/answer bodies are attached to the trace span
    /// (gen_ai.input.messages / gen_ai.output.messages, opt-in per the GenAI semconv - same
    /// dev-only policy as the 3 agents' EnableSensitiveData).</summary>
    public bool CaptureContent { get; set; }
}
