using System.Text.Json;
using CoffeeshopCli.Mcp;
using CoffeeshopCli.Services;
using Xunit;

namespace CoffeeshopCli.Tests.Mcp;

public sealed class CoffeeshopMcpBridgeToolsTests
{
    [Fact]
    public void SkillList_IncludesCounterService()
    {
        var tools = CreateTools();

        var json = ToJsonElement(tools.SkillList());
        var names = json.GetProperty("skills").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString())
            .ToList();

        Assert.Contains("coffeeshop-counter-service", names);
    }

    [Fact]
    public async Task SkillInvoke_ReturnsConfirmationRequired_WhenNotConfirmed()
    {
        var tools = CreateTools();

        var result = await tools.SkillInvoke(
            "coffeeshop-counter-service",
            new CoffeeshopMcpBridgeTools.SkillInvokeArgs(
                customer_id: "C-1001",
                intent: "process-order",
                items:
                [
                    new CoffeeshopMcpBridgeTools.OrderLineInput("LATTE", 1)
                ],
                confirm: false));

        var json = ToJsonElement(result);

        Assert.True(json.GetProperty("ok").GetBoolean());
        Assert.Equal("confirmation_required", json.GetProperty("message").GetString());
        Assert.False(json.GetProperty("state").GetProperty("confirmed").GetBoolean());
    }

    [Fact]
    public async Task SkillInvoke_ReturnsIntentNotSupported_ForNonProcessOrderIntent()
    {
        var tools = CreateTools();

        var result = await tools.SkillInvoke(
            "coffeeshop-counter-service",
            new CoffeeshopMcpBridgeTools.SkillInvokeArgs(
                customer_id: "C-1001",
                intent: "account",
                items: null,
                confirm: false));

        var json = ToJsonElement(result);

        Assert.False(json.GetProperty("ok").GetBoolean());
        Assert.Equal("intent_not_supported_for_submit", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task SkillInvoke_SubmitsOrder_WhenConfirmed()
    {
        var tools = CreateTools();

        var result = await tools.SkillInvoke(
            "coffeeshop-counter-service",
            new CoffeeshopMcpBridgeTools.SkillInvokeArgs(
                customer_id: "C-1001",
                intent: "process-order",
                items:
                [
                    new CoffeeshopMcpBridgeTools.OrderLineInput("LATTE", 1)
                ],
                confirm: true));

        var json = ToJsonElement(result);

        Assert.True(json.GetProperty("ok").GetBoolean());
        Assert.Equal("coffeeshop-counter-service", json.GetProperty("skill").GetString());
        Assert.True(json.GetProperty("state").GetProperty("confirmed").GetBoolean());
        Assert.Equal("Pending", json.GetProperty("result").GetProperty("status").GetString());
    }

    private static CoffeeshopMcpBridgeTools CreateTools()
    {
        var discovery = new FileSystemDiscoveryService(new ModelRegistry(), "./skills");
        var parser = new SkillParser();
        var submit = new OrderSubmitHandler();
        return new CoffeeshopMcpBridgeTools(discovery, parser, submit);
    }

    private static JsonElement ToJsonElement(object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }
}
