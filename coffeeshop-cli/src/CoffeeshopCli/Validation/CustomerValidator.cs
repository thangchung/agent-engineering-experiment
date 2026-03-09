using CoffeeshopCli.Errors;
using CoffeeshopCli.Models;

namespace CoffeeshopCli.Validation;

/// <summary>
/// Validates Customer model instances.
/// </summary>
public sealed class CustomerValidator : IValidator<Customer>
{
    public ValidationError? Validate(Customer model)
    {
        var errors = new List<string>();

        // CustomerId: required, pattern ^C-\d{4}$
        if (string.IsNullOrWhiteSpace(model.CustomerId))
        {
            errors.Add("CustomerId is required");
        }
        else if (!ValidationHelpers.MatchesPattern(model.CustomerId, @"^C-\d{4}$"))
        {
            errors.Add("CustomerId must match pattern ^C-\\d{4}$ (e.g., C-1001)");
        }

        // Email: required, valid format
        if (string.IsNullOrWhiteSpace(model.Email))
        {
            errors.Add("Email is required");
        }
        else if (!ValidationHelpers.IsValidEmail(model.Email))
        {
            errors.Add("Email must be a valid email address");
        }

        // Name: required
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            errors.Add("Name is required");
        }

        // Phone: optional but must be valid if provided
        if (!string.IsNullOrWhiteSpace(model.Phone) && !ValidationHelpers.IsValidPhone(model.Phone))
        {
            errors.Add("Phone must be a valid phone number");
        }

        if (errors.Count > 0)
        {
            var details = new Dictionary<string, object>
            {
                ["errors"] = errors
            };
            return new ValidationError("Customer validation failed", details);
        }

        return null;
    }
}
