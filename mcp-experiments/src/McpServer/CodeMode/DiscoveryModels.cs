namespace McpServer.CodeMode;

/// <summary>
/// Search projection used in code-mode discovery.
/// </summary>
public sealed record DiscoveryResult(
	string Name,
	string Description,
	IReadOnlyList<string> Tags,
	string? Parameters);

/// <summary>
/// Search response with optional truncation annotation.
/// </summary>
public sealed record DiscoverySearchResponse(
	IReadOnlyList<DiscoveryResult> Results,
	int TotalMatched,
	string? Annotation);

/// <summary>
/// Schema projection used in code-mode schema retrieval.
/// </summary>
public sealed record SchemaResult(string Name, string Schema);

/// <summary>
/// Schema lookup response that preserves matched schemas and missing names.
/// </summary>
public sealed record SchemaLookupResponse(
	IReadOnlyList<SchemaResult> Results,
	IReadOnlyList<string> Missing);
