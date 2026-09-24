using CounterService.Common;

namespace CounterService.Features.Menu;

public static class GetMenuQuery
{
    public static IEndpointRouteBuilder MapGetMenu(this IEndpointRouteBuilder app)
    {
        app.MapGet("/menu", HandleAsync)
            .WithName("GetMenu")
            .WithSummary("The coffee shop menu, from ProductCatalogService's MCP get_menu tool.");

        return app;
    }

    private static async Task<IResult> HandleAsync(ICatalogClient catalog, CancellationToken cancellationToken)
    {
        try
        {
            var menu = await catalog.GetMenuAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(menu);
        }
        catch (CatalogUnavailableException ex)
        {
            return Results.Problem(
                title: "Catalog unavailable",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
