using CoffeeshopCli.Models;
using CoffeeshopCli.Validation;
using Xunit;

namespace CoffeeshopCli.Tests.Validation;

public class OrderValidatorTests
{
    private readonly OrderValidator _validator = new();

    [Fact]
    public void Validate_ValidOrder_ReturnsNull()
    {
        var order = new Order
        {
            OrderId = "ORD-1001",
            CustomerId = "C-1001",
            Status = OrderStatus.Pending,
            PlacedAt = DateTime.Now,
            Total = 9.00m,
            Items = new List<OrderItem>
            {
                new OrderItem
                {
                    ItemType = ItemType.LATTE,
                    Name = "Latte",
                    Qty = 2,
                    Price = 4.50m
                }
            }
        };

        var result = _validator.Validate(order);

        Assert.Null(result);
    }

    [Fact]
    public void Validate_InvalidOrderId_ReturnsError()
    {
        var order = new Order
        {
            OrderId = "INVALID",
            CustomerId = "C-1001",
            Status = OrderStatus.Pending,
            PlacedAt = DateTime.Now,
            Total = 9.00m,
            Items = new List<OrderItem>
            {
                new OrderItem { ItemType = ItemType.LATTE, Name = "Latte", Qty = 2, Price = 4.50m }
            }
        };

        var result = _validator.Validate(order);

        Assert.NotNull(result);
        Assert.Contains("OrderId must match pattern", GetErrorList(result!).First());
    }

    [Fact]
    public void Validate_EmptyItems_ReturnsError()
    {
        var order = new Order
        {
            OrderId = "ORD-1001",
            CustomerId = "C-1001",
            Status = OrderStatus.Pending,
            PlacedAt = DateTime.Now,
            Total = 0m,
            Items = new List<OrderItem>()
        };

        var result = _validator.Validate(order);

        Assert.NotNull(result);
        Assert.Contains("Items list must contain at least one item", GetErrorList(result!));
    }

    [Fact]
    public void Validate_InvalidNestedOrderItem_ReturnsError()
    {
        var order = new Order
        {
            OrderId = "ORD-1001",
            CustomerId = "C-1001",
            Status = OrderStatus.Pending,
            PlacedAt = DateTime.Now,
            Total = 9.00m,
            Items = new List<OrderItem>
            {
                new OrderItem
                {
                    ItemType = ItemType.LATTE,
                    Name = "", // Invalid: empty name
                    Qty = 200, // Invalid: > 100
                    Price = -1.00m // Invalid: negative
                }
            }
        };

        var result = _validator.Validate(order);

        Assert.NotNull(result);
        var errors = GetErrorList(result!);
        Assert.Contains(errors, e => e.Contains("OrderItem validation failed"));
        Assert.Contains(errors, e => e.Contains("Name is required"));
        Assert.Contains(errors, e => e.Contains("Qty must be between 1 and 100"));
        Assert.Contains(errors, e => e.Contains("Price must be greater than 0"));
    }

    [Fact]
    public void Validate_OrderWithNotes_ValidatesNoteContent()
    {
        var order = new Order
        {
            OrderId = "ORD-1001",
            CustomerId = "C-1001",
            Status = OrderStatus.Pending,
            PlacedAt = DateTime.Now,
            Total = 9.00m,
            Items = new List<OrderItem>
            {
                new OrderItem { ItemType = ItemType.LATTE, Name = "Latte", Qty = 2, Price = 4.50m }
            },
            Notes = new List<OrderNote>
            {
                new OrderNote { Text = "", Author = "Staff", Timestamp = DateTime.Now } // Invalid: empty text
            }
        };

        var result = _validator.Validate(order);

        Assert.NotNull(result);
        Assert.Contains("Text is required", GetErrorList(result!).First(e => e.Contains("Note")));
    }

    private static List<string> GetErrorList(Errors.ValidationError error)
    {
        if (error.Details != null && error.Details.TryGetValue("errors", out var errors))
        {
            return errors as List<string> ?? new List<string>();
        }
        return new List<string>();
    }
}
