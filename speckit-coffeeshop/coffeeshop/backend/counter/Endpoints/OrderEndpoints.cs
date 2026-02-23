using CoffeeShop.Counter.Application;
using CoffeeShop.Counter.Application.UseCases;
using CoffeeShop.Counter.Domain;

namespace CoffeeShop.Counter.Endpoints;

/// <summary>
/// Order endpoints:
/// - POST /api/v1/orders      — place order (T036)
/// - GET  /api/v1/orders/{id} — get order   (T044, stub added here)
/// </summary>
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/orders", PlaceOrderHandler)
           .WithName("PlaceOrder")
           .WithOpenApi();

        return app;
    }

    private static async Task<IResult> PlaceOrderHandler(
        PlaceOrderHttpRequest body,
        PlaceOrder placeOrder,
        CancellationToken ct)
    {
        // Map HTTP request DTO → use-case request
        var useCaseItems = (body.Items ?? Array.Empty<OrderItemHttpRequest>())
            .Select(i => new OrderItemRequest(i.Type, i.Quantity))
            .ToList()
            .AsReadOnly();

        var request = new PlaceOrderRequest(body.CustomerId, useCaseItems, body.Notes);

        try
        {
            var order = await placeOrder.ExecuteAsync(request, ct);
            return Results.Created($"/api/v1/orders/{order.Id}", ToOrderDto(order));
        }
        catch (OrderValidationException ex)
        {
            return Results.Problem(
                type: "https://coffeeshop.local/errors/validation",
                title: ex.Message.Contains("least one") ? "Order requires at least one item" : "Invalid quantity",
                statusCode: StatusCodes.Status400BadRequest,
                detail: ex.Message);
        }
        catch (ItemUnavailableException ex)
        {
            return Results.Problem(
                type: "https://coffeeshop.local/errors/conflict",
                title: "Item unavailable",
                statusCode: StatusCodes.Status409Conflict,
                detail: ex.Message);
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

    // -----------------------------------------------------------------------
    // DTO mappings — matches contracts/orders.md
    // -----------------------------------------------------------------------

    public static OrderDto ToOrderDto(Order order) =>
        new(
            order.Id,
            order.CustomerId,
            order.Items.Select(i => new OrderItemDto(
                i.Type.ToString(),
                i.DisplayName,
                i.Quantity,
                i.UnitPrice,
                i.LineTotal)).ToList().AsReadOnly(),
            order.TotalPrice,
            order.Status.ToString().ToLowerInvariant(),
            order.Notes,
            order.EstimatedPickup,
            order.CreatedAt,
            order.UpdatedAt);

    // -----------------------------------------------------------------------
    // HTTP request/response types
    // -----------------------------------------------------------------------

    public record PlaceOrderHttpRequest(
        string CustomerId,
        IReadOnlyList<OrderItemHttpRequest>? Items,
        string? Notes);

    public record OrderItemHttpRequest(string Type, int Quantity);

    public record OrderDto(
        string                     Id,
        string                     CustomerId,
        IReadOnlyList<OrderItemDto> Items,
        decimal                    TotalPrice,
        string                     Status,
        string?                    Notes,
        string?                    EstimatedPickup,
        DateTimeOffset             CreatedAt,
        DateTimeOffset             UpdatedAt);

    public record OrderItemDto(
        string  Type,
        string  DisplayName,
        int     Quantity,
        decimal UnitPrice,
        decimal LineTotal);
}
