namespace Jev.Client;

/// <summary>
/// One flat answer shape covering all 3 primitives (research.md §2). Only the fields for the
/// answer's own <see cref="Type"/> are populated; the rest are null.
/// </summary>
public sealed record JevAnswer
{
    /// <summary>"choice" | "score" | "noul".</summary>
    public required string Type { get; init; }

    /// <summary>Choice only: the highest-probability option.</summary>
    public string? Choice { get; init; }

    /// <summary>Noul only: P(true), 0..1. Noul has no <see cref="Confidence"/> (research.md §2).</summary>
    public double? Noul { get; init; }

    /// <summary>Score only: the probability-weighted level, Σ i·pᵢ (0-indexed), can be fractional.</summary>
    public double? Score { get; init; }

    /// <summary>Choice/score only: probability per option/level.</summary>
    public IReadOnlyDictionary<string, double>? Probabilities { get; init; }

    /// <summary>Score only: level index (as string) to its label.</summary>
    public IReadOnlyDictionary<string, string>? Legend { get; init; }

    /// <summary>Choice/score only: how peaked the probability distribution is.</summary>
    public double? Confidence { get; init; }
}

/// <summary>Token usage reported by Jev/OpenJev for one request.</summary>
public sealed record JevUsage
{
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
}

/// <summary>The full <c>POST /v1/systemone</c> response.</summary>
public sealed record JevResponse
{
    /// <summary>The versioned model id that actually answered, e.g. "jev-1.13.0" or "openjev-0.1".</summary>
    public required string Model { get; init; }

    /// <summary>Keyed by the same ids the request's <c>questions</c> map used.</summary>
    public required IReadOnlyDictionary<string, JevAnswer> Answers { get; init; }

    public JevUsage? Usage { get; init; }
}
