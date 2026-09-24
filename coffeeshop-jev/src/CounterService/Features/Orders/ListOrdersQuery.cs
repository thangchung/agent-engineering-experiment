using CounterService.Features.Orders.Common;

namespace CounterService.Features.Orders;

public static class ListOrdersQuery
{
    public static IEndpointRouteBuilder MapListOrders(this IEndpointRouteBuilder app)
    {
        app.MapGet("/orders", (OrderStore store) => Results.Ok(store.ListNewestFirst()))
            .WithName("ListOrders")
            .WithSummary("The last orders, newest first (research.md Q7 board).");

        return app;
    }
}
