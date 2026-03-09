namespace CoffeeshopCli.Errors;

/// <summary>
/// Base class for all CLI errors with structured information.
/// Supports both TUI (Spectre Panel) and JSON output modes.
/// </summary>
public abstract class CliError : Exception
{
    public string Type { get; }
    public Dictionary<string, object>? Details { get; init; }

    protected CliError(string type, string message, Dictionary<string, object>? details = null)
        : base(message)
    {
        Type = type;
        Details = details;
    }

    /// <summary>
    /// Exit code for this error type.
    /// </summary>
    public abstract int ExitCode { get; }
}
