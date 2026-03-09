using System.ComponentModel.DataAnnotations;

namespace CoffeeshopCli.Models;

/// <summary>
/// Menu item with pricing.
/// Source: product_catalogs.py — _DEFAULT_CATALOG
/// </summary>
public record MenuItem
{
    [Required]
    public required ItemType ItemType { get; init; }

    [Required]
    public required string Name { get; init; }

    [Required]
    public required string Category { get; init; } // "Beverages", "Food", "Others"

    [Required]
    [Range(0.01, 1000.00)]
    public required decimal Price { get; init; }
}
