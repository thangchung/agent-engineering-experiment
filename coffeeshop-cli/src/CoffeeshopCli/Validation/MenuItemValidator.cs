using CoffeeshopCli.Errors;
using CoffeeshopCli.Models;

namespace CoffeeshopCli.Validation;

/// <summary>
/// Validates MenuItem model instances.
/// </summary>
public sealed class MenuItemValidator : IValidator<MenuItem>
{
    public ValidationError? Validate(MenuItem model)
    {
        var errors = new List<string>();

        // Name: required
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            errors.Add("Name is required");
        }

        // Category: required
        if (string.IsNullOrWhiteSpace(model.Category))
        {
            errors.Add("Category is required");
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
            return new ValidationError("MenuItem validation failed", details);
        }

        return null;
    }
}
