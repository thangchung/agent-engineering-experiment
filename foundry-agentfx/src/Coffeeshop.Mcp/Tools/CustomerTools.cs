namespace Coffeeshop.Mcp.Tools;

using System.ComponentModel;
using Coffeeshop.Mcp.Services;
using Coffeeshop.Models;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol.Server;

[McpServerToolType]
public static class CustomerTools
{
    [McpServerTool(Name = "customer_lookup"),
     Description("Look up a customer by name, phone number, or email address.")]
    public static async Task<Customer?> LookupCustomer(
        [Description("Customer name, phone (e.g. '555-0101'), or email")] string query,
        [FromServices] ICustomerService customerService,
        CancellationToken ct)
        => await customerService.LookupAsync(query, ct);
}
