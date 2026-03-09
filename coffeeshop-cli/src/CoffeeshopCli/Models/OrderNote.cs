using System.ComponentModel.DataAnnotations;

namespace CoffeeshopCli.Models;

/// <summary>
/// Note/comment attached to an order.
/// Source: orders.py — update_order tool's note structure
/// [OPTIONAL] Only used if notes are displayed/edited in commands.
/// </summary>
public record OrderNote
{
    [Required]
    public required string Text { get; init; }

    [Required]
    public required string Author { get; init; }

    [Required]
    public required DateTime Timestamp { get; init; }
}
