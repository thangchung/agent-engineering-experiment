using CoffeeshopCli.Errors;
using CoffeeshopCli.Models;
using CoffeeshopCli.Services;
using Xunit;

namespace CoffeeshopCli.Tests.Services;

/// <summary>
/// Tests for OrderSubmitHandler that uses SampleDataStore (no MCP dependency).
/// </summary>
public sealed class OrderSubmitHandlerTests
{
    [Fact]
    public async Task SubmitAsync_ResolvesNamePriceAndCalculatesTotal()
    {
        var handler = new OrderSubmitHandler();

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
    public async Task SubmitAsync_WithMultipleItems_CalculatesTotalCorrectly()
    {
        var handler = new OrderSubmitHandler();

        var result = await handler.SubmitAsync(new SimplifiedOrderInput
        {
            CustomerId = "C-1001",
            Items =
            [
                new SimplifiedOrderItemInput { ItemType = ItemType.CAPPUCCINO, Qty = 1 },
                new SimplifiedOrderItemInput { ItemType = ItemType.CROISSANT, Qty = 2 }
            ]
        });

        Assert.Equal("C-1001", result.CustomerId);
        Assert.Equal(2, result.Items.Count);
        
        // Cappuccino: 1 × $4.50 = $4.50
        // Croissant: 2 × $3.25 = $6.50
        // Total: $11.00
        Assert.Equal(11.00m, result.Total);
    }

    [Fact]
    public async Task SubmitAsync_UnknownItem_ThrowsValidationError()
    {
        var handler = new OrderSubmitHandler();

        var invalidItemType = (ItemType)999; // Non-existent enum value
        var ex = await Assert.ThrowsAsync<ValidationError>(() => handler.SubmitAsync(new SimplifiedOrderInput
        {
            CustomerId = "C-1001",
            Items =
            [
                new SimplifiedOrderItemInput { ItemType = invalidItemType, Qty = 1 }
            ]
        }));

        Assert.Contains("unknown item type", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitAsync_UnknownCustomer_ThrowsValidationError()
    {
        var handler = new OrderSubmitHandler();

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

    [Fact]
    public async Task SubmitAsync_SetsOrderIdAndTimestamp()
    {
        var handler = new OrderSubmitHandler();
        var beforeSubmit = DateTime.UtcNow;

        var result = await handler.SubmitAsync(new SimplifiedOrderInput
        {
            CustomerId = "C-1001",
            Items =
            [
                new SimplifiedOrderItemInput { ItemType = ItemType.ESPRESSO, Qty = 1 }
            ]
        });

        var afterSubmit = DateTime.UtcNow;

        Assert.NotEmpty(result.OrderId);
        Assert.True(result.PlacedAt >= beforeSubmit && result.PlacedAt <= afterSubmit);
        Assert.Equal(OrderStatus.Pending, result.Status);
    }
}
