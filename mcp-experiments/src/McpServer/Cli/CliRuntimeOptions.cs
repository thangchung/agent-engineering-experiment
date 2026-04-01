namespace McpServer.Cli;

/// <summary>
/// Runtime CLI flags parsed from raw process arguments.
/// </summary>
/// <param name="Verbose">Whether verbose output was requested for this run.</param>
internal sealed record CliRuntimeOptions(bool Verbose);