using System.Reflection;
using System.Text.Json;
using CounterService.Domain;
using CounterService.Features.Orders.Workflow;

namespace CoffeeShop.Tests;

/// <summary>tasks.md T08: IntentPolicy + StationPolicy, all pure functions, no network.</summary>
public class PolicyTests
{
    // --- IntentPolicy (research.md §4.1, AC1) ---

    [Fact]
    public void Decide_IntentConfidenceJustBelowThreshold_ReturnsUnclear()
    {
        var result = IntentPolicy.Decide("place_order", 0.49, 0.9, asks: 0);
        Assert.IsType<Unclear>(result);
    }

    [Fact]
    public void Decide_IntentConfidenceAtThreshold_UsesTheChoice()
    {
        var result = IntentPolicy.Decide("place_order", 0.50, 0.9, asks: 0);
        Assert.IsType<Accepted>(result);
    }

    [Fact]
    public void Decide_PlaceOrderOnMenuAtThreshold_ReturnsAccepted()
    {
        var result = IntentPolicy.Decide("place_order", 0.9, 0.50, asks: 0);
        Assert.IsType<Accepted>(result);
    }

    [Fact]
    public void Decide_PlaceOrderOnMenuJustBelowThreshold_ReturnsUnclear()
    {
        var result = IntentPolicy.Decide("place_order", 0.9, 0.49, asks: 0);
        Assert.IsType<Unclear>(result);
    }

    [Fact]
    public void Decide_AskMenu_ReturnsMenuRequested()
    {
        var result = IntentPolicy.Decide("ask_menu", 0.9, 0.0, asks: 0);
        Assert.IsType<MenuRequested>(result);
    }

    [Theory]
    [InlineData(0.72, 0.08, 0.33, 0.00, "place_order")]    // "2 lattes and a croissant"
    [InlineData(0.23, 0.72, 0.52, 0.66, "ask_menu")]       // "what do you have?"
    [InlineData(0.27, 0.69, 0.15, 0.72, "ask_menu")]       // "show me the menu"
    [InlineData(0.09, 0.13, 0.85, 0.05, "order_history")]  // "what did I order before?"
    [InlineData(0.70, 0.07, 0.71, 0.91, "place_order")]    // cart text: past 0.71 stays under 0.8, off-topic is only a fallback
    [InlineData(0.60, 0.00, 0.24, 0.01, "place_order")]    // cart text "2 × Latte, 1 × Muffin"
    [InlineData(0.53, 0.00, 0.19, 0.00, "place_order")]    // "2 Latte, 1 Muffin"
    [InlineData(0.81, 0.54, 0.12, 0.20, "place_order")]    // menu 0.54 stays under 0.6
    [InlineData(0.00, 0.00, 0.07, 0.85, "off_topic")]      // "what's the weather today?"
    [InlineData(0.14, 0.10, 0.09, 0.12, "other")]          // "hmm"
    public void FromNouls_MeasuredLayaScores_PickTheRightIntent(double orders, double menu, double past, double offTopic, string expected)
    {
        var (choice, confidence) = IntentPolicy.FromNouls(orders, menu, past, offTopic);

        Assert.Equal(expected, choice);
        Assert.Equal(expected == "other", confidence == 0);
    }

    [Fact]
    public void FromNouls_OtherHasZeroConfidence_SoDecideAsksInsteadOfRefusing()
    {
        var (choice, confidence) = IntentPolicy.FromNouls(0.14, 0.10, 0.09, 0.12);

        Assert.IsType<Unclear>(IntentPolicy.Decide(choice, confidence, 0.4, asks: 0, DecisionProfile.Laya));
    }

    [Fact]
    public void Decide_OrderHistory_ReturnsHistoryRequested()
    {
        Assert.IsType<HistoryRequested>(IntentPolicy.Decide("order_history", 0.9, 0.5, asks: 0));
    }

    [Fact]
    public void Decide_OffTopic_ReturnsRejectedOffTopic()
    {
        var result = IntentPolicy.Decide("off_topic", 0.9, 0.0, asks: 0);
        var rejected = Assert.IsType<Rejected>(result);
        Assert.Equal(RejectReason.OffTopic, rejected.Reason);
    }

    [Fact]
    public void Decide_UnclearAtMaxAsks_ReturnsRejectedAskCap()
    {
        // asks == MaxAsks (2): the next unclear intent must give up instead of asking again.
        var result = IntentPolicy.Decide("ask_menu", 0.1, 0.0, asks: 2);
        var rejected = Assert.IsType<Rejected>(result);
        Assert.Equal(RejectReason.AskCap, rejected.Reason);
    }

    [Fact]
    public void Decide_UnclearBelowMaxAsks_StillAsks()
    {
        var result = IntentPolicy.Decide("ask_menu", 0.1, 0.0, asks: 1);
        Assert.IsType<Unclear>(result);
    }

    // --- StationPolicy (research.md §4.2, AC2) ---

    [Theory]
    [InlineData(0.59, Flag.Review)]
    [InlineData(0.60, Flag.Confirm)]
    [InlineData(0.85, Flag.Confirm)]
    [InlineData(0.86, Flag.None)]
    public void Assign_ConfidenceBands_MapToExpectedFlag(double confidence, Flag expected)
    {
        var (_, flag) = StationPolicy.Assign("barista", confidence);
        Assert.Equal(expected, flag);
    }

    [Fact]
    public void Assign_Barista_MapsToBaristaStation()
    {
        var (station, _) = StationPolicy.Assign("barista", 0.9);
        Assert.Equal(Station.Barista, station);
    }

    [Theory]
    [InlineData("kitchen")]
    [InlineData("other")]
    [InlineData("anything-else")]
    public void Assign_NonBaristaChoice_MapsToKitchenStation(string choice)
    {
        var (station, _) = StationPolicy.Assign(choice, 0.9);
        Assert.Equal(Station.Kitchen, station);
    }

    // --- Message ToString round-trip (AC3) ---

    [Fact]
    public void Messages_ToString_RoundTripsThroughJson()
    {
        AssertRoundTrips(new Accepted());
        AssertRoundTrips(new Unclear("why?"));
        AssertRoundTrips(new Rejected(RejectReason.OffTopic, "no"));
        AssertRoundTrips(new ClarifyRequest("Which one?", ["Latte", "Muffin"]));
        AssertRoundTrips(new OrderDraft([new OrderLine { Name = "LATTE", Qty = 1, Price = 4.50m }]));
        AssertRoundTrips(new SplitOrder([new OrderLine { Name = "LATTE", Qty = 1, Price = 4.50m, Station = Station.Barista }]));
    }

    private static void AssertRoundTrips<T>(T message)
    {
        var json = message!.ToString();
        Assert.False(string.IsNullOrWhiteSpace(json));
        using var doc = JsonDocument.Parse(json!);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    // --- Money is decimal everywhere (AC4) ---

    [Fact]
    public void Domain_NoMoneyPropertyUsesFloatOrDouble()
    {
        var assembly = typeof(OrderLine).Assembly;
        var offenders = assembly.GetTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(p =>
                (p.Name.Contains("Price", StringComparison.OrdinalIgnoreCase) ||
                 p.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)) &&
                (p.PropertyType == typeof(float) || p.PropertyType == typeof(double)))
            .Select(p => $"{p.DeclaringType!.FullName}.{p.Name}")
            .ToList();

        Assert.Empty(offenders);
    }
}
