using CounterService.Domain;

namespace CounterService.Common;

/// <summary>The one seam tests fake instead of running a real MCP server (CatalogClientTests in
/// T06 already proves the real implementation against a real in-process MCP server).</summary>
public interface ICatalogClient
{
    Task<IReadOnlyList<MenuItem>> GetMenuAsync(CancellationToken cancellationToken = default);
}
