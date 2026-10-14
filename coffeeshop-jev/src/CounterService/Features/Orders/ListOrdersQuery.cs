using CounterService.Features.Orders.Common;

namespace CounterService.Features.Orders;

public static class ListOrdersQuery
{
    public static IEndpointRouteBuilder MapListOrders(this IEndpointRouteBuilder app)
    {
        app.MapGet("/orders", (string? customerId, OrderStore store) =>
                Results.Ok(string.IsNullOrEmpty(customerId) ? store.ListNewestFirst() : store.ListByCustomer(customerId)))
            .WithName("ListOrders")
            .WithSummary("The last orders, newest first; ?customerId= keeps one customer's (research.md §17.4).");

        app.MapGet("/orders/{number:int}", (int number, OrderStore store) =>
                store.Get(number) is { } record ? Results.Ok(record) : Results.NotFound())
            .WithName("GetOrder")
            .WithSummary("One order with its audit trail and AG-UI event log.");

        return app;
    }
}
