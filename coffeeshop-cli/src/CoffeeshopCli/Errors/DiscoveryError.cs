namespace CoffeeshopCli.Errors;

/// <summary>
/// Discovery error - model/skill not found or discovery failed.
/// Exit code: 2
/// </summary>
public sealed class DiscoveryError : CliError
{
    public DiscoveryError(string message, Dictionary<string, object>? details = null)
        : base("discovery", message, details)
    {
    }

    public override int ExitCode => 2;
}
