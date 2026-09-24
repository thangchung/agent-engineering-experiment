namespace CounterService.Domain;

/// <summary>
/// The Counter's own view of a menu item, fetched from ProductCatalogService over MCP
/// (Common/CatalogClient.cs) - never a project reference (research.md §6: slices/services stay
/// decoupled, only talk over the wire).
/// </summary>
public sealed record MenuItem(string Id, string DisplayName, decimal PriceUsd, Station Station);
