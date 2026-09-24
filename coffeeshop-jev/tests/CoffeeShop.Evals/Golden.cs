using System.Text.Json;

namespace CoffeeShop.Evals;

/// <summary>One expected line in a golden order (research.md §10.3).</summary>
public sealed record GoldenLine(string Name, int Qty, string Station);

/// <summary>The golden truth for one case, per turn where it varies (research.md §10.3).</summary>
public sealed record GoldenExpect
{
    /// <summary>The Gate decision's type name per turn: "Accepted" | "Unclear" | "Rejected" | "MenuRequested".</summary>
    public required List<string> Gate { get; init; }

    /// <summary>The `intent` question's expected `choice`. Null when the case doesn't pin one
    /// down (e.g. G16, where every turn is vague and any low-confidence choice is fine).</summary>
    public string? Intent { get; init; }

    public int Asks { get; init; }

    public List<GoldenLine>? Lines { get; init; }

    /// <summary>Not checked by L1 (Gate/Split) evals - reserved for L3 workflow evals.</summary>
    public string? Status { get; init; }
}

/// <summary>One golden case: a scripted conversation plus its expected outcome at every layer
/// (research.md §10.3). `Turns[0]` is the order text; `Turns[1..]` are scripted human answers.</summary>
public sealed record GoldenCase
{
    public required string Id { get; init; }

    public required List<string> Tags { get; init; }

    public required List<string> Turns { get; init; }

    public required GoldenExpect Expect { get; init; }
}

/// <summary>Loads `golden/orders.jsonl` (research.md T30). One JSON object per line.</summary>
public static class Golden
{
    private const string RelativePath = "golden/orders.jsonl";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<GoldenCase> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, RelativePath);
        var cases = File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<GoldenCase>(line, Json)
                ?? throw new InvalidDataException($"Golden case line failed to parse: {line}"))
            .ToList();

        var duplicateIds = cases.GroupBy(c => c.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateIds.Count > 0)
        {
            throw new InvalidDataException($"Duplicate golden case ids: {string.Join(", ", duplicateIds)}");
        }

        return cases;
    }
}
