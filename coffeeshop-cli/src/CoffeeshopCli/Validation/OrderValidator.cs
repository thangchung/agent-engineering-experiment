using CoffeeshopCli.Errors;
using CoffeeshopCli.Models;

namespace CoffeeshopCli.Validation;

/// <summary>
/// Validates Order model instances (including nested OrderItems and OrderNotes).
/// </summary>
public sealed class OrderValidator : IValidator<Order>
{
    private readonly OrderItemValidator _itemValidator = new();

    public ValidationError? Validate(Order model)
    {
        var errors = new List<string>();

        // OrderId: required, pattern ^ORD-\d{4}$
        if (string.IsNullOrWhiteSpace(model.OrderId))
        {
            errors.Add("OrderId is required");
        }
        else if (!ValidationHelpers.MatchesPattern(model.OrderId, @"^ORD-\d{4}$"))
        {
            errors.Add("OrderId must match pattern ^ORD-\\d{4}$ (e.g., ORD-1001)");
        }

        // CustomerId: required, pattern ^C-\d{4}$
        if (string.IsNullOrWhiteSpace(model.CustomerId))
        {
            errors.Add("CustomerId is required");
        }
        else if (!ValidationHelpers.MatchesPattern(model.CustomerId, @"^C-\d{4}$"))
        {
            errors.Add("CustomerId must match pattern ^C-\\d{4}$ (e.g., C-1001)");
        }

        // Items: must have at least one
        if (model.Items == null || model.Items.Count == 0)
        {
            errors.Add("Items list must contain at least one item");
        }
        else
        {
            // Validate each item
            for (int i = 0; i < model.Items.Count; i++)
            {
                var itemError = _itemValidator.Validate(model.Items[i]);
                if (itemError != null)
                {
                    errors.Add($"Item[{i}]: {itemError.Message}");
                    if (itemError.Details != null && itemError.Details.TryGetValue("errors", out var itemErrors))
                    {
                        if (itemErrors is List<string> errorList)
                        {
                            errors.AddRange(errorList.Select(e => $"  - {e}"));
                        }
                    }
                }
            }
        }

        // Notes: optional, but validate text if present
        if (model.Notes != null)
        {
            for (int i = 0; i < model.Notes.Count; i++)
            {
                var note = model.Notes[i];
                if (string.IsNullOrWhiteSpace(note.Text))
                {
                    errors.Add($"Note[{i}]: Text is required");
                }
                if (string.IsNullOrWhiteSpace(note.Author))
                {
                    errors.Add($"Note[{i}]: Author is required");
                }
            }
        }

        if (errors.Count > 0)
        {
            var details = new Dictionary<string, object>
            {
                ["errors"] = errors
            };
            return new ValidationError("Order validation failed", details);
        }

        return null;
    }
}
