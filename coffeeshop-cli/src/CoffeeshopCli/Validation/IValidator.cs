using CoffeeshopCli.Errors;

namespace CoffeeshopCli.Validation;

/// <summary>
/// Validator interface for domain models.
/// </summary>
public interface IValidator<T>
{
    /// <summary>
    /// Validate a model instance. Returns null if valid, or a ValidationError otherwise.
    /// </summary>
    ValidationError? Validate(T model);
}
