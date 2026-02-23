using System.ComponentModel;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------
// Aspire service defaults (OTel, health checks, service discovery)
// -----------------------------------------------------------------------
builder.AddServiceDefaults();

// -----------------------------------------------------------------------
// OpenAPI
// -----------------------------------------------------------------------
builder.Services.AddOpenApi();

// -----------------------------------------------------------------------
// MCP Server — exposes product-catalog tools over HTTP/SSE
// WithToolsFromAssembly() discovers all [McpServerToolType] classes.
// -----------------------------------------------------------------------
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();  // /health, /alive, /healthz

// MCP transport endpoints (/sse and /messages)
app.MapMcp();

// -----------------------------------------------------------------------
// REST convenience endpoint consumed by counter's McpProductCatalogClient
// via typed HttpClient (no MCP client package needed in counter).
// GET /api/menu → { "items": [...] }
// -----------------------------------------------------------------------
app.MapGet("/api/menu", () => Results.Ok(new MenuResponse(CatalogTools.SeedMenu)))
   .WithName("GetMenu")
   .WithOpenApi();

app.Run();

// =======================================================================
// MCP tool provider — static class pattern (no DI required for seed data)
// =======================================================================

/// <summary>
/// Menu item data-transfer type for the product-catalog service.
/// Matches the shape expected by the counter's McpProductCatalogClient.
/// </summary>
public record MenuItemDto(
    string Type,
    string DisplayName,
    string Category,
    decimal Price,
    bool IsAvailable
);

/// <summary>
/// REST response envelope for GET /api/menu.
/// </summary>
public record MenuResponse(IReadOnlyList<MenuItemDto> Items);

/// <summary>
/// MCP tool provider exposing the CoffeeShop product catalog.
/// Tools are called by MCP clients (and indirectly via REST by the counter).
/// </summary>
[McpServerToolType]
public static class CatalogTools
{
    // -------------------------------------------------------------------
    // Seed data — single source of truth for product-catalog service.
    // Matches SeedData.MenuItems values in counter (prices, availability).
    // -------------------------------------------------------------------
    public static readonly IReadOnlyList<MenuItemDto> SeedMenu = new List<MenuItemDto>
    {
        new("CAPPUCCINO",          "CAPPUCCINO",          "Beverages", 4.50m, true),
        new("COFFEE_BLACK",        "COFFEE BLACK",        "Beverages", 3.00m, true),
        new("COFFEE_WITH_ROOM",    "COFFEE WITH ROOM",    "Beverages", 3.25m, true),
        new("ESPRESSO",            "ESPRESSO",            "Beverages", 3.50m, true),
        new("ESPRESSO_DOUBLE",     "ESPRESSO DOUBLE",     "Beverages", 4.00m, true),
        new("LATTE",               "LATTE",               "Beverages", 4.50m, true),
        new("CAKEPOP",             "CAKEPOP",             "Food",      2.50m, true),
        new("CROISSANT",           "CROISSANT",           "Food",      3.25m, true),
        new("MUFFIN",              "MUFFIN",              "Food",      3.00m, true),
        new("CROISSANT_CHOCOLATE", "CROISSANT CHOCOLATE", "Food",      3.75m, true),
        new("CHICKEN_MEATBALLS",   "CHICKEN MEATBALLS",   "Others",    4.25m, true),
    };

    /// <summary>
    /// Returns the full menu (all 11 items, including unavailable ones).
    /// </summary>
    [McpServerTool(Name = "get_menu_items")]
    [Description("Returns the complete product catalog with all menu items, prices, categories, and availability.")]
    public static IReadOnlyList<MenuItemDto> GetMenuItems() => SeedMenu;

    /// <summary>
    /// Returns a single menu item by its type key (e.g. "LATTE"), or null if not found.
    /// </summary>
    [McpServerTool(Name = "get_item_by_type")]
    [Description("Returns a specific menu item by its type key (e.g. 'LATTE'). Returns null if not found.")]
    public static MenuItemDto? GetItemByType(
        [Description("The ItemType enum name, e.g. 'LATTE', 'CROISSANT', 'CHICKEN_MEATBALLS'.")] string type)
        => SeedMenu.FirstOrDefault(m =>
            m.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
}
