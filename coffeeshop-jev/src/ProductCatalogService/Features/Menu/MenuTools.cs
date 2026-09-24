using System.ComponentModel;
using ModelContextProtocol.Server;
using ProductCatalogService.Domain;

namespace ProductCatalogService.Features.Menu;

[McpServerToolType]
public sealed class MenuTools
{
    [McpServerTool(Name = "get_menu")]
    [Description("Returns the coffee shop menu: id, display name, USD price and station (barista or kitchen) for every item.")]
    public static IReadOnlyList<MenuItem> GetMenu() => MenuItem.All;
}
