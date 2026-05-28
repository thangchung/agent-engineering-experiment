using System.Globalization;
namespace Coffeeshop.Mcp.Services;

using Coffeeshop.Models;
using Microsoft.Extensions.Logging;

public sealed class OrderAuditService(IConfiguration config, ILogger<OrderAuditService> logger) : IAuditService
{
    private readonly string _ordersRoot = config["Audit:OrdersPath"] ?? "./orders";

    public async Task WriteOrderAuditAsync(OrderResult order, CancellationToken ct = default)
    {
        var date = order.PlacedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dir = Path.Combine(_ordersRoot, date);
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, $"{order.OrderId}.md");

        var itemRows = string.Join("\n", order.Items.Select(
            i => string.Create(CultureInfo.InvariantCulture,
                $"| {i.MenuItemName} | {i.Quantity} | ${i.UnitPrice:F2} | ${i.UnitPrice * i.Quantity:F2} |")));

        var total = order.Total.ToString("F2", CultureInfo.InvariantCulture);

        var content = $"""
            # Order {order.OrderId}

            - **Customer:** {order.CustomerName} ({order.CustomerId})
            - **Placed:** {order.PlacedAt:O}
            - **Total:** ${total}

            ## Items

            | Item | Qty | Unit Price | Subtotal |
            |------|-----|-----------|----------|
            {itemRows}

            ---
            *Logged by AuditAgent*
            """;

        await File.WriteAllTextAsync(path, content, ct);
        logger.LogInformation("[Audit] Order {OrderId} logged to {Path}", order.OrderId, path);
    }
}
