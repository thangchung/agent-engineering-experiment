using CoffeeshopCli.Errors;
using CoffeeshopCli.Mcp;
using CoffeeshopCli.Models;
using CoffeeshopCli.Services;
using Xunit;

namespace CoffeeshopCli.Tests.Services;

public sealed class OrderSubmitHandlerTests
{
    [Fact]
    public async Task SubmitAsync_ResolvesNamePriceAndCalculatesTotal()
    {
        var mcp = new InMemoryMcpClient();
        var handler = new OrderSubmitHandler(mcp);

        var result = await handler.SubmitAsync(new SimplifiedOrderInput
        {
            CustomerId = "C-1001",
            Items =
            [
                new SimplifiedOrderItemInput { ItemType = ItemType.LATTE, Qty = 2 }
            ]
        });

        Assert.Equal("C-1001", result.CustomerId);
        Assert.Single(result.Items);
        Assert.Equal("Latte", result.Items[0].Name);
        Assert.Equal(4.50m, result.Items[0].Price);
        Assert.Equal(9.00m, result.Total);
    }

    [Fact]
    public async Task SubmitAsync_UnknownItem_ThrowsValidationError()
    {
        var mcp = new InMemoryMcpClient();
        var handler = new OrderSubmitHandler(mcp);

        var ex = await Assert.ThrowsAsync<ValidationError>(() => handler.SubmitAsync(new SimplifiedOrderInput
        {
            CustomerId = "C-1001",
            Items =
            [
                new SimplifiedOrderItemInput { ItemType = ItemType.COFFEE_BLACK, Qty = 1 }
            ]
        }));

        Assert.Contains("unknown item type", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitAsync_UnknownCustomer_ThrowsValidationError()
    {
        var mcp = new InMemoryMcpClient();
        var handler = new OrderSubmitHandler(mcp);

        var ex = await Assert.ThrowsAsync<ValidationError>(() => handler.SubmitAsync(new SimplifiedOrderInput
        {
            CustomerId = "C-9999",
            Items =
            [
                new SimplifiedOrderItemInput { ItemType = ItemType.LATTE, Qty = 1 }
            ]
        }));

        Assert.Contains("unknown customer id", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
