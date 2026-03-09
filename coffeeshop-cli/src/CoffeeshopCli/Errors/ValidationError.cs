namespace CoffeeshopCli.Errors;

/// <summary>
/// Validation error - invalid input data.
/// Exit code: 1
/// </summary>
public sealed class ValidationError : CliError
{
    public ValidationError(string message, Dictionary<string, object>? details = null)
        : base("validation", message, details)
    {
    }

    public override int ExitCode => 1;
}
