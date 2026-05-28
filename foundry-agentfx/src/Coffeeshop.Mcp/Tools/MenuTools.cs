namespace Coffeeshop.Mcp.Tools;

using System.ComponentModel;
using Coffeeshop.Mcp.Services;
using Coffeeshop.Models;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol.Server;

[McpServerToolType]
public static class MenuTools
{
    [McpServerTool(Name = "menu_list_items"),
     Description("List all available menu items including name, category, and price.")]
    public static async Task<IEnumerable<MenuItem>> ListMenuItems(
        [FromServices] IMenuService menuService,
        CancellationToken ct)
        => await menuService.GetAllItemsAsync(ct);

    [McpServerTool(Name = "menu_get_item"),
     Description("Get details for a specific menu item by its ID.")]
    public static async Task<MenuItem?> GetMenuItem(
        [Description("The menu item ID, e.g. 'latte'")] string id,
        [FromServices] IMenuService menuService,
        CancellationToken ct)
        => await menuService.GetByIdAsync(id, ct);
}
