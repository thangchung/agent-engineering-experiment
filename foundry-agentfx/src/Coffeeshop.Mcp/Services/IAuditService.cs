namespace Coffeeshop.Mcp.Services;

using Coffeeshop.Models;

public interface IAuditService
{
    Task WriteOrderAuditAsync(OrderResult order, CancellationToken ct = default);
}
