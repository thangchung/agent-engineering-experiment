using CoffeeshopCli.Errors;
using CoffeeshopCli.Models;

namespace CoffeeshopCli.Validation;

/// <summary>
/// Validates OrderItem model instances.
/// </summary>
public sealed class OrderItemValidator : IValidator<OrderItem>
{
    public ValidationError? Validate(OrderItem model)
    {
        var errors = new List<string>();

        // Name: required
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            errors.Add("Name is required");
        }

        // Qty: must be 1-100
        if (!ValidationHelpers.IsInRange(model.Qty, 1, 100))
        {
            errors.Add("Qty must be between 1 and 100");
        }

        // Price: must be positive
        if (!ValidationHelpers.IsPositive(model.Price))
        {
            errors.Add("Price must be greater than 0");
        }

        if (errors.Count > 0)
        {
            var details = new Dictionary<string, object>
            {
                ["errors"] = errors
            };
            return new ValidationError("OrderItem validation failed", details);
        }

        return null;
    }
}
