using CoffeeShop.Tests.Fakes;
using CounterService.Domain;
using CounterService.Features.Orders.Workflow;
using DecisionModel.Client;
using Microsoft.Extensions.Configuration;

namespace CoffeeShop.Tests;

public class DecisionProfileTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => KeyValuePair.Create<string, string?>(v.Key, v.Value))).Build();

    [Fact]
    public void FromConfiguration_NothingSet_IsTypesafe() =>
        Assert.Equal(DecisionProfile.Typesafe, DecisionProfile.FromConfiguration(Config()));

    [Fact]
    public void FromConfiguration_EmptyProviderFromAppHost_IsTypesafe() =>
        Assert.Equal(DecisionProfile.Typesafe, DecisionProfile.FromConfiguration(Config(("DecisionModel:Provider", ""))));

    [Fact]
    public void FromConfiguration_Laya_UsesLayaProfile() =>
        Assert.Equal(DecisionProfile.Laya, DecisionProfile.FromConfiguration(Config(("DecisionModel:Provider", "Laya"))));

    [Fact]
    public void FromConfiguration_Laya_AsksYesNoQuestions_OthersAskOneChoice()
    {
        Assert.Equal(GateMode.Noul, DecisionProfile.Laya.GateMode);
        Assert.Equal(GateMode.Choice, DecisionProfile.Microsoft.GateMode);
        Assert.Equal(GateMode.Choice, DecisionProfile.FromConfiguration(Config(("DecisionModel:Provider", "laya"), ("DecisionModel:GateMode", "Choice"))).GateMode);
    }

    [Fact]
    public void FromConfiguration_OpenJev_UsesTypesafeCutOffsUnderItsOwnName()
    {
        var p = DecisionProfile.FromConfiguration(Config(("DecisionModel:Provider", "openjev")));

        Assert.Equal("openjev", p.Name);
        Assert.Equal(DecisionProfile.Typesafe with { Name = "openjev" }, p);
    }

    [Fact]
    public void FromConfiguration_Microsoft_UsesFoundryPathHeaderAndDeploymentName()
    {
        var p = DecisionProfile.FromConfiguration(Config(("DecisionModel:Provider", "microsoft"), ("DecisionModel:Model", "pi-decision-1")));

        Assert.Equal("microsoft", p.Name);
        Assert.Equal("pi-decision-1", p.Model);
        Assert.Equal("providers/microsoft/v1/systemone", p.Path);
        Assert.Equal("api-key", p.ApiKeyHeader);
        Assert.True(p.EntraWhenNoKey);
        Assert.Equal(0.7, p.IntentConfidence);
    }

    [Fact]
    public void FromConfiguration_Microsoft_NoDeploymentName_DefaultsToTheModelName() =>
        Assert.Equal("Microsoft-Decision-1", DecisionProfile.FromConfiguration(Config(("DecisionModel:Provider", "microsoft"), ("DecisionModel:Model", ""))).Model);

    [Theory]
    [InlineData("https://res.services.ai.azure.com", "https://res.services.ai.azure.com")]
    [InlineData("https://res.services.ai.azure.com/", "https://res.services.ai.azure.com")]
    [InlineData("https://res.services.ai.azure.com/providers/microsoft", "https://res.services.ai.azure.com")]
    [InlineData("https://res.services.ai.azure.com/providers/microsoft/", "https://res.services.ai.azure.com")]
    [InlineData("https://res.services.ai.azure.com/providers/microsoft/v1/systemone", "https://res.services.ai.azure.com")]
    public void Microsoft_NormalizeBaseUrl_DropsTheProviderSuffixTheRequestPathAlreadyHas(string given, string expected) =>
        Assert.Equal(expected, DecisionProfile.Microsoft.NormalizeBaseUrl(given));

    [Fact]
    public void NormalizeBaseUrl_OtherProviders_DropOnlyTheirOwnRequestPath()
    {
        Assert.Equal("http://host:8686/providers/microsoft", DecisionProfile.OpenJev.NormalizeBaseUrl("http://host:8686/providers/microsoft/"));
        Assert.Equal("http://host:8686", DecisionProfile.OpenJev.NormalizeBaseUrl("http://host:8686/v1/systemone"));
    }

    [Fact]
    public void FromConfiguration_CustomWithOverrides_AppliesThem()
    {
        var p = DecisionProfile.FromConfiguration(Config(
            ("DecisionModel:Provider", "custom"),
            ("DecisionModel:Model", "our-model"),
            ("DecisionModel:Thresholds:IntentConfidence", "0.7"),
            ("DecisionModel:SplitWithModel", "false")));

        Assert.Equal("custom", p.Name);
        Assert.Equal("our-model", p.Model);
        Assert.Equal(0.7, p.IntentConfidence);
        Assert.Equal(DecisionProfile.Typesafe.OnMenu, p.OnMenu);
        Assert.False(p.SplitWithModel);
    }

    [Fact]
    public void FromConfiguration_UnknownProvider_FailsAtStartup() =>
        Assert.Throws<InvalidOperationException>(() => DecisionProfile.FromConfiguration(Config(("DecisionModel:Provider", "nope"))));

    [Fact]
    public void IntentPolicy_LowConfidence_AcceptedUnderLayaButAskedUnderTypesafe()
    {
        Assert.IsType<Unclear>(IntentPolicy.Decide("place_order", 0.2, 0.9, asks: 0));
        Assert.IsType<Accepted>(IntentPolicy.Decide("place_order", 0.2, 0.9, asks: 0, DecisionProfile.Laya));
    }

    [Fact]
    public void StationPolicy_FlagBandsFollowTheProfile()
    {
        var strict = DecisionProfile.Typesafe with { StationReview = 0.9, StationConfirm = 0.95 };
        Assert.Equal(Flag.Confirm, StationPolicy.Assign("barista", 0.7).Flag);
        Assert.Equal(Flag.Review, StationPolicy.Assign("barista", 0.7, strict).Flag);
    }

    [Fact]
    public async Task Split_ProviderWithoutStationRouting_UsesCatalogStationAndNeverCallsTheModel()
    {
        var client = new DecisionModelClient(
            new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://decision-model.local/") }, new DecisionModelOptions());
        MenuItem[] menu = [new("LATTE", "Latte", 4.5m, Station.Barista), new("CROISSANT", "Croissant", 3.25m, Station.Kitchen)];
        var draft = new OrderDraft([
            new OrderLine { Name = "LATTE", Qty = 1, Price = 4.5m },
            new OrderLine { Name = "CROISSANT", Qty = 1, Price = 3.25m },
        ]);

        var split = await new SplitExecutor(client, DecisionProfile.Laya, menu).HandleAsync(draft, new NoopWorkflowContext());

        Assert.Equal([Station.Barista, Station.Kitchen], split.Lines.Select(l => l.Station!.Value));
        Assert.All(split.Lines, l => Assert.Equal(Flag.None, l.Flag));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the decision model must not be called");
    }
}
