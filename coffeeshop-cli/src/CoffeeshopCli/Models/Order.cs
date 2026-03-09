using System.ComponentModel.DataAnnotations;

namespace CoffeeshopCli.Models;

/// <summary>
/// Complete order with line items and notes.
/// Source: orders.py — _DEFAULT_ORDERS
/// </summary>
public record Order
{
    [Required]
    [RegularExpression(@"^ORD-\d{4}$", ErrorMessage = "Order ID must match pattern ORD-####")]
    public required string OrderId { get; init; }

    [Required]
    [RegularExpression(@"^C-\d{4}$", ErrorMessage = "Customer ID must match pattern C-####")]
    public required string CustomerId { get; init; }

    [Required]
    public required OrderStatus Status { get; init; }

    [Required]
    public required DateTime PlacedAt { get; init; }

    [Required]
    [Range(0.01, 100000.00)]
    public required decimal Total { get; init; }

    [Required]
    public required List<OrderItem> Items { get; init; }

    public List<OrderNote> Notes { get; init; } = new();
}
