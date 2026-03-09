using System.ComponentModel.DataAnnotations;

namespace CoffeeshopCli.Models;

/// <summary>
/// Individual line item within an order.
/// Source: orders.py — order item dicts within _DEFAULT_ORDERS
/// </summary>
public record OrderItem
{
    [Required]
    public required ItemType ItemType { get; init; }

    [Required]
    public required string Name { get; init; }

    [Required]
    [Range(1, 100)]
    public required int Qty { get; init; }

    [Required]
    [Range(0.01, 1000.00)]
    public required decimal Price { get; init; } // Unit price
}
