namespace CounterService.Common;

/// <summary>Thrown by <see cref="CatalogClient"/> when the catalog can't be reached and there is
/// no cached ("last-good") menu to fall back to (research.md §12.3 U1).</summary>
public sealed class CatalogUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
