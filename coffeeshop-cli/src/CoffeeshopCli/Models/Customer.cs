using System.ComponentModel.DataAnnotations;

namespace CoffeeshopCli.Models;

/// <summary>
/// Customer account record.
/// Source: orders.py — _DEFAULT_CUSTOMERS
/// </summary>
public record Customer
{
    [Required]
    [RegularExpression(@"^C-\d{4}$", ErrorMessage = "Customer ID must match pattern C-####")]
    public required string CustomerId { get; init; }

    [Required]
    public required string Name { get; init; }

    [Required]
    [EmailAddress]
    public required string Email { get; init; }

    [Required]
    [Phone]
    public required string Phone { get; init; }

    [Required]
    public required CustomerTier Tier { get; init; }

    [Required]
    public required DateOnly AccountCreated { get; init; }
}
