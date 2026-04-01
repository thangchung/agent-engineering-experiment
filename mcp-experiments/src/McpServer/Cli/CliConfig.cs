namespace McpServer.Cli;

/// <summary>
/// Runtime flags that control whether the server runs in CLI mode.
/// </summary>
public sealed record CliConfig(
	bool EnableCliMode = false,
	bool CliServeMode = false,
	bool EnableStatistic = false);
