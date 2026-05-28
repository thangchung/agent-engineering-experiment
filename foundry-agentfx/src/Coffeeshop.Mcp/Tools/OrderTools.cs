namespace Coffeeshop.Mcp.Tools;

using System.ComponentModel;
using Coffeeshop.Mcp.Services;
using Coffeeshop.Models;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol.Server;

[McpServerToolType]
public static class OrderTools
{
    [McpServerTool(Name = "order_submit"),
     Description("Submit a coffee order for a customer. Returns order confirmation with total price.")]
    public static async Task<OrderResult> SubmitOrder(
        [Description("The customer ID obtained from customer_lookup")] string customerId,
        [Description("List of items to order, each with menuItemId and quantity")] List<OrderItem> items,
        [FromServices] IOrderService orderService,
        CancellationToken ct)
        => await orderService.SubmitAsync(customerId, items, ct);
}
