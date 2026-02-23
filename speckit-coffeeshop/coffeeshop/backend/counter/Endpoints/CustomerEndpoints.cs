using CoffeeShop.Counter.Application;
using CoffeeShop.Counter.Application.UseCases;
using CoffeeShop.Counter.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CoffeeShop.Counter.Endpoints;

/// <summary>
/// HTTP endpoints for customer operations.
///
/// T024: GET /api/v1/customers/lookup?identifier=
/// T049: GET /api/v1/customers/{customerId}/orders  (wired in T049)
/// </summary>
public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/customers").WithTags("Customers");

        // GET /api/v1/customers/lookup?identifier=
        group.MapGet("/lookup", LookupAsync)
            .WithName("LookupCustomer")
            .WithSummary("Look up a customer by email, customer ID, or order ID.")
            .Produces<CustomerLookupResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(404);

        return app;
    }

    private static async Task<IResult> LookupAsync(
        string? identifier,
        LookupCustomer lookupCustomer)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return Results.Problem(
                title: "Identifier required",
                detail: "Please provide your email address, customer ID (C-XXXX), or an order number (ORD-XXXX).",
                type: "https://coffeeshop.local/errors/validation",
                statusCode: 400);
        }

        try
        {
            var customer = await lookupCustomer.ExecuteAsync(identifier);
            return Results.Ok(ToResponse(customer));
        }
        catch (CustomerNotFoundException ex)
        {
            return Results.Problem(
                title: "Customer not found",
                detail: ex.Message,
                type: "https://coffeeshop.local/errors/not-found",
                statusCode: 404);
        }
    }

    internal static CustomerLookupResponse ToResponse(Customer customer) =>
        new(
            customer.Id,
            customer.FirstName,
            customer.LastName,
            customer.Email,
            customer.Phone,
            customer.Tier.ToString().ToLowerInvariant(),
            BuildGreeting(customer));

    private static string BuildGreeting(Customer c)
    {
        var tierLabel = c.Tier switch
        {
            CustomerTier.Gold     => "Gold Member",
            CustomerTier.Silver   => "Silver Member",
            CustomerTier.Standard => "Standard Member",
            _                     => c.Tier.ToString()
        };
        return $"Welcome back, {c.FirstName} ✨ {tierLabel}";
    }
}

/// <summary>
/// Response record for customer lookup — matches contracts/customers.md.
/// </summary>
public record CustomerLookupResponse(
    string CustomerId,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    string Tier,
    string Greeting);
