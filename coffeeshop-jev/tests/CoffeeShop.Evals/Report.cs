using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoffeeShop.Evals;

/// <summary>
/// tasks.md T33 (reduced scope, research.md §10.6a note in tasks.md: threshold sweep for the
/// Phase 1b hazard guards is deferred with the guards themselves). Reads `evals/out/*.jsonl`
/// that <see cref="GateEvals"/>/<see cref="StationEvals"/> wrote and reports the metrics
/// research.md §10.8 actually asks for at today's fixed thresholds - no network calls, so this
/// runs in every `dotnet test`, not just live runs.
/// </summary>
public class Report
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void Build_FromEvalOutput_WritesReportMarkdown()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "evals", "out");
        var sb = new StringBuilder();
        sb.AppendLine("# CoffeeShop.Jev eval report");
        sb.AppendLine();
        sb.AppendLine($"Generated {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC.");
        sb.AppendLine();

        AppendSection<GateEvalRow>(sb, dir, "gate.jsonl", "Gate (L1) - research.md §4.1, R-G1/R-G2/R-G3",
            "run `GateEvals` with `OPENJEV_URL` set first", AppendGateSection);
        AppendSection<StationEvalRow>(sb, dir, "station.jsonl", "Split / station (L1) - research.md §4.2, R-S1/R-S2/R-S3",
            "run `StationEvals` with `OPENJEV_URL` set first", AppendStationSection);
        AppendSection<JudgeVerdict>(sb, dir, "judge.jsonl", "JevJudge accept/escalate cascade (research.md §10.6a)",
            "run `JudgeCascadeTests` with Jev and OpenAI both configured first", AppendJudgeSection);

        Directory.CreateDirectory(dir);
        var reportPath = Path.Combine(dir, "report.md");
        File.WriteAllText(reportPath, sb.ToString());

        // This test always passes - it is a report writer, not a gate. GateEvals/StationEvals
        // hold the actual R-G3/R-S2/R-S3 hard assertions (research.md §10.8).
        Assert.True(File.Exists(reportPath));
    }

    private static void AppendSection<T>(StringBuilder sb, string dir, string fileName, string heading, string howToFill, Action<StringBuilder, List<T>> appendBody)
    {
        var path = Path.Combine(dir, fileName);
        if (File.Exists(path))
        {
            appendBody(sb, ReadRows<T>(path));
        }
        else
        {
            sb.AppendLine($"## {heading}").AppendLine().AppendLine($"_No `{fileName}` yet - {howToFill}._").AppendLine();
        }
    }

    private static void AppendGateSection(StringBuilder sb, List<GateEvalRow> rows)
    {
        sb.AppendLine("## Gate (L1) - research.md §4.1, R-G1/R-G2/R-G3");
        sb.AppendLine();

        var g1Pass = rows.Count(r => r.PassG1);
        var g2Checked = rows.Where(r => r.CheckedG2).ToList();
        var g2Pass = g2Checked.Count(r => r.PassG2);
        var safety = rows.Where(r => r.Tags.Any(t => t is "off_topic" or "injection") && !r.Tags.Contains("requires_guard")).ToList();
        var safetyViolations = safety.Count(r => r.ActualGate == "Accepted");

        sb.AppendLine($"- R-G1 (gate decision matches golden): {g1Pass}/{rows.Count} = {Pct(g1Pass, rows.Count)}");
        sb.AppendLine($"- R-G2 (intent choice matches golden, Accepted/MenuRequested turns only): {g2Pass}/{g2Checked.Count} = {Pct(g2Pass, g2Checked.Count)}");
        sb.AppendLine($"- R-G3 (false-accept on off_topic/injection, excluding `requires_guard`-tagged cases): {safetyViolations} violations out of {safety.Count} checked");
        sb.AppendLine();

        var byTag = rows.SelectMany(r => r.Tags.Select(t => (Tag: t, Row: r))).GroupBy(x => x.Tag).OrderBy(g => g.Key);
        AppendTable(sb, ["Tag", "R-G1 pass rate", "n"],
            byTag.Select(g => new[] { g.Key, Pct(g.Count(x => x.Row.PassG1), g.Count()), g.Count().ToString() }));

        var mismatches = rows.Where(r => !r.PassG1).ToList();
        if (mismatches.Count > 0)
        {
            sb.AppendLine().AppendLine("R-G1 mismatches (raw Jev answers, for the growth loop - §10.8):").AppendLine();
            AppendTable(sb, ["Case", "Turn", "Expected", "Actual", "intent.choice", "intent.confidence", "on_menu.noul"],
                mismatches.Select(r => new[]
                {
                    r.CaseId, r.Turn.ToString(), r.ExpectedGate, r.ActualGate, r.ActualIntent,
                    r.IntentConfidence.ToString("F3"), r.OnMenuNoul.ToString("F3"),
                }));
        }
    }

    private static void AppendStationSection(StringBuilder sb, List<StationEvalRow> rows)
    {
        sb.AppendLine("## Split / station (L1) - research.md §4.2, R-S1/R-S2/R-S3");
        sb.AppendLine();

        var s1Pass = rows.Count(r => r.PassS1);
        var confidentWrong = rows.Count(r => !r.PassS2);
        var unflaggedWrong = rows.Count(r => !r.PassS3);

        sb.AppendLine($"- R-S1 (station matches golden): {s1Pass}/{rows.Count} = {Pct(s1Pass, rows.Count)}");
        sb.AppendLine($"- R-S2 (confident-wrong, confidence > {CounterService.Features.Orders.Workflow.StationPolicy.ConfirmThreshold} and wrong): {confidentWrong} violations");
        sb.AppendLine($"- R-S3 (wrong station without a Confirm/Review flag): {unflaggedWrong} violations");
        sb.AppendLine();

        var mismatches = rows.Where(r => !r.PassS1).ToList();
        if (mismatches.Count > 0)
        {
            AppendTable(sb, ["Line", "Golden", "Actual", "Flag", "Confidence"],
                mismatches.Select(r => new[] { r.LineName, r.GoldenStation, r.ActualStation, r.Flag.ToString(), r.Confidence.ToString("F3") }));
        }
    }

    private static void AppendJudgeSection(StringBuilder sb, List<JudgeVerdict> rows)
    {
        sb.AppendLine("## JevJudge accept/escalate cascade (research.md §10.6a)");
        sb.AppendLine();
        AppendTable(sb, ["Rubric", "Accepted (Jev)", "Escalated (LLM)", "Human review", "Pass rate"],
            rows.GroupBy(r => r.RubricId).OrderBy(g => g.Key).Select(g => new[]
            {
                g.Key,
                g.Count(r => r.Source == VerdictSource.Jev).ToString(),
                g.Count(r => r.Source == VerdictSource.EscalatedLlm).ToString(),
                g.Count(r => r.Source == VerdictSource.HumanReview).ToString(),
                Pct(g.Count(r => r.Pass == true), g.Count(r => r.Pass.HasValue)),
            }));

        sb.AppendLine().AppendLine("See `judge-variance.md` for the repeated-read variance smoke check (T37a).");
    }

    private static void AppendTable(StringBuilder sb, string[] headers, IEnumerable<string[]> rows)
    {
        sb.AppendLine($"| {string.Join(" | ", headers)} |");
        sb.AppendLine($"|{string.Join('|', headers.Select(_ => "---"))}|");
        foreach (var row in rows)
        {
            sb.AppendLine($"| {string.Join(" | ", row)} |");
        }

        sb.AppendLine();
    }

    private static string Pct(int pass, int total) => total == 0 ? "n/a" : $"{100.0 * pass / total:F1}%";

    private static List<T> ReadRows<T>(string path) =>
        File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonSerializer.Deserialize<T>(l, Json)!)
            .ToList();
}
