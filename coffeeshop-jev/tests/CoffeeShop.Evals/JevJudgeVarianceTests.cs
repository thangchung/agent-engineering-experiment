using System.Text.Json;

namespace CoffeeShop.Evals;

/// <summary>
/// tasks.md T37a: mirrors danielgshea/jev-as-a-judge's own method (research.md §10.6a) - freeze
/// the thing being judged, vary only the judge's repeated reads of it, and report the observed
/// variance. Their benchmark re-read 5 frozen agent runs 100x each; this re-reads 3 frozen,
/// hand-written tickets (not live-generated - the point is to isolate the *judge's* variance
/// from the *generator's*, exactly like the source benchmark did) 30x each - a smaller N than
/// their 100x (kept the live run under a couple of minutes), documented as a reduction, not a
/// different method.
/// </summary>
public class JevJudgeVarianceTests
{
    private const int Repetitions = 30;

    private static readonly (string CaseId, string Query, string Response)[] FrozenTickets =
    [
        ("G01-barista", """[{"name":"LATTE","qty":1}]""",
            """{"items":[{"name":"LATTE","qty":1,"steps":["Pull an espresso shot","Steam milk","Pour milk over the espresso and top with foam"],"minutes":4}]}"""),
        ("G03-kitchen-croissant", """[{"name":"CROISSANT","qty":1}]""",
            """{"items":[{"name":"CROISSANT","qty":1,"steps":["Warm the croissant in the oven for 3 minutes","Plate it"],"minutes":3}]}"""),
        ("G04-kitchen-meatballs", """[{"name":"CHICKEN_MEATBALLS","qty":1}]""",
            """{"items":[{"name":"CHICKEN_MEATBALLS","qty":1,"steps":["Reheat the meatballs in the oven for 8 minutes","Plate with sauce"],"minutes":8}]}"""),
    ];

    [Fact]
    public async Task RepeatedReads_OfFrozenTickets_ReportsPerCaseVariance()
    {
        EvalConfig.RequireOpenJev();
        var jev = EvalClients.Jev();
        var judge = new JevJudge(jev, escalationJudge: null);
        var rows = new List<(string CaseId, double Score, double Confidence)>();

        foreach (var (caseId, query, response) in FrozenTickets)
        {
            for (var i = 0; i < Repetitions; i++)
            {
                var verdicts = await judge.EvaluateAsync(new JudgeItem(caseId, query, response), [RubricSet.TicketRealism]);
                var verdict = Assert.Single(verdicts);
                rows.Add((caseId, verdict.Value, verdict.Confidence));
            }
        }

        var byCaseVariance = rows
            .GroupBy(r => r.CaseId)
            .Select(g => (CaseId: g.Key, Mean: g.Average(r => r.Score), Variance: Variance(g.Select(r => r.Score))))
            .ToList();

        WriteReport(byCaseVariance);

        // Flag, don't gate (research.md tasks.md T37a): the source benchmark's whole finding
        // was Jev's variance staying essentially flat across cases, so one case behaving very
        // differently from the others is this golden set's own outlier to look at, not proof
        // our deployment is worse than the benchmark's.
        var maxVariance = byCaseVariance.Max(c => c.Variance);
        var minVariance = byCaseVariance.Min(c => c.Variance);
        if (minVariance > 0 && maxVariance / minVariance > 10)
        {
            Console.WriteLine(
                $"NOTE (not a failure): variance spread is {maxVariance / minVariance:F1}x across cases - " +
                "see evals/out/judge-variance.md for the per-case breakdown.");
        }

        Assert.True(byCaseVariance.Count == FrozenTickets.Length);
    }

    private static double Variance(IEnumerable<double> values)
    {
        var list = values.ToList();
        var mean = list.Average();
        return list.Select(v => (v - mean) * (v - mean)).Average();
    }

    private static void WriteReport(IReadOnlyList<(string CaseId, double Mean, double Variance)> byCaseVariance)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "evals", "out");
        Directory.CreateDirectory(dir);

        var lines = new List<string>
        {
            "# JevJudge variance smoke check (T37a)",
            "",
            $"{Repetitions} repeated reads per case, {FrozenTickets.Length} frozen R-T4 tickets, generated {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC.",
            "",
            "| Case | Mean score | Variance |",
            "|---|---:|---:|",
        };
        lines.AddRange(byCaseVariance.Select(c => $"| {c.CaseId} | {c.Mean:F4} | {c.Variance:E3} |"));

        File.WriteAllLines(Path.Combine(dir, "judge-variance.md"), lines);
        File.WriteAllLines(
            Path.Combine(dir, "judge-variance.jsonl"),
            byCaseVariance.Select(c => JsonSerializer.Serialize(new { c.CaseId, c.Mean, c.Variance })));
    }
}
