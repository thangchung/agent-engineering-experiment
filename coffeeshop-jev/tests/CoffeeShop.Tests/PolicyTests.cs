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
