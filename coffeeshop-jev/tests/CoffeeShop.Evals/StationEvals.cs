using System.Text.Json;
using CounterService.Domain;
using CounterService.Features.Orders.Workflow;

namespace CoffeeShop.Evals;

/// <summary>One line's real Jev split call and the station it resolved to (research.md T32, L1).</summary>
public sealed record StationEvalRow(
    string LineName,
    string GoldenStation,
    string ActualStation,
    Flag Flag,
    double Confidence,
    bool PassS1,
    bool PassS2,
    bool PassS3);

/// <summary>
/// tasks.md T32: the 11 ids + 11 display names (22 lines total, research.md §10.3 "station
/// set"), all in the one Jev call <see cref="StationExecutor"/> would make, checked against
/// R-S1/R-S2/R-S3 (research.md §10.5). R-S2 is the one that matters most for this whole
/// project's confidence-routing premise (§4.2): a "confident but wrong" split would mean the
/// Confirm/Review bands are lying to the staff who trust them.
/// </summary>
public class StationEvals
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RunGoldenStationLines_WritesReportAndEnforcesCalibrationGate()
    {
        EvalConfig.RequireOpenJev();
        var jev = EvalClients.Jev();

        var lines = EvalMenu.Items
            .Select(m => (Name: m.Id, Station: m.Station))
            .Concat(EvalMenu.Items.Select(m => (Name: m.DisplayName, Station: m.Station)))
            .Select(x => (OrderLine: new OrderLine { Name = x.Name, Qty = 1, Price = 0m }, Golden: x.Station))
            .ToList();

        var orderLines = lines.Select(l => l.OrderLine).ToList();
        var response = await jev.AskAsync(orderLines, StationQuestions.Build(orderLines));

        var rows = new List<StationEvalRow>();
        for (var i = 0; i < lines.Count; i++)
        {
            var answer = response.Answers[StationQuestions.QuestionId(i)];
            var (station, flag) = StationPolicy.Assign(answer.Choice!, answer.Confidence!.Value);
            var golden = lines[i].Golden.ToString();
            var actual = station.ToString();
            var wrong = actual != golden;

            rows.Add(new StationEvalRow(
                lines[i].OrderLine.Name, golden, actual, flag, answer.Confidence!.Value,
                PassS1: !wrong,
                PassS2: !(wrong && answer.Confidence!.Value > StationPolicy.ConfirmThreshold),
                PassS3: !wrong || flag != Flag.None));
        }

        EvalOutput.WriteJsonl("station.jsonl", rows, Json);

        var confidentWrong = rows.Where(r => !r.PassS2).ToList();
        Assert.True(confidentWrong.Count == 0,
            $"R-S2 violated (confident-wrong): {string.Join(", ", confidentWrong.Select(r => $"{r.LineName} -> {r.ActualStation} @ {r.Confidence:F2}"))}");

        var unflaggedWrong = rows.Where(r => !r.PassS3).ToList();
        Assert.True(unflaggedWrong.Count == 0,
            $"R-S3 violated (wrong station with no Confirm/Review flag): {string.Join(", ", unflaggedWrong.Select(r => r.LineName))}");
    }
}
