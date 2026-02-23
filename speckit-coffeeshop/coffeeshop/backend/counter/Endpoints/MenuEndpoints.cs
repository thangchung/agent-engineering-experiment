using CoffeeShop.Counter.Application;
using CoffeeShop.Counter.Application.UseCases;
using CoffeeShop.Counter.Domain;

namespace CoffeeShop.Counter.Endpoints;

/// <summary>
/// <c>GET /api/v1/menu</c> — returns the full product catalog.
/// On MCP failure returns RFC 9457 503.
/// </summary>
public static class MenuEndpoints
{
    public static IEndpointRouteBuilder MapMenuEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/menu", GetMenuHandler)
           .WithName("GetMenu")
           .WithOpenApi();

        return app;
    }

    private static async Task<IResult> GetMenuHandler(
        GetMenu getMenu,
        CancellationToken ct)
    {
        try
        {
            var items = await getMenu.ExecuteAsync(ct);
            var dtos = items.Select(ToDto).ToList();
            return Results.Ok(new MenuResponse(dtos.AsReadOnly()));
        }
        catch (ProductCatalogUnavailableException ex)
        {
            return Results.Problem(
                type: "https://coffeeshop.local/errors/service-unavailable",
                title: "Menu unavailable",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                detail: ex.Message);
        }
    }

    private static MenuItemDto ToDto(MenuItem m) =>
        new(m.Type.ToString(), m.DisplayName, m.Category.ToString(), m.Price, m.IsAvailable);

    // -----------------------------------------------------------------------
    // Response types (match contracts/menu.md)
    // -----------------------------------------------------------------------

    public record MenuItemDto(
        string  Type,
        string  DisplayName,
        string  Category,
        decimal Price,
        bool    IsAvailable);

    public record MenuResponse(IReadOnlyList<MenuItemDto> Items);
}
