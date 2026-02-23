using CoffeeShop.Counter.Domain;
using CoffeeShop.Counter.Infrastructure;

namespace CoffeeShop.Counter.Application.UseCases;

/// <summary>
/// Resolves a customer from any of three identifier forms:
///   1. Order ID  (prefix "ORD-") → looks up order → returns order.CustomerId
///   2. Customer ID (prefix "C-") → direct store lookup
///   3. Email address (case-insensitive) → email-index lookup
/// Throws <see cref="CustomerNotFoundException"/> when no match is found.
/// </summary>
public sealed class LookupCustomer(
    InMemoryCustomerStore customerStore,
    InMemoryOrderStore orderStore)
{
    /// <summary>Execute the lookup. Returns the matched Customer record.</summary>
    /// <exception cref="CustomerNotFoundException">No customer matches the identifier.</exception>
    public Task<Customer> ExecuteAsync(string identifier, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new CustomerNotFoundException(identifier);

        var trimmed = identifier.Trim();

        // 1. Order-ID prefix → resolve via order store
        if (trimmed.StartsWith("ORD-", StringComparison.OrdinalIgnoreCase))
        {
            if (orderStore.TryGetById(trimmed, out var order) && order is not null)
            {
                if (customerStore.TryGetById(order.CustomerId, out var byOrder) && byOrder is not null)
                    return Task.FromResult(byOrder);
            }
            throw new CustomerNotFoundException(identifier);
        }

        // 2. Customer-ID prefix → direct lookup
        if (trimmed.StartsWith("C-", StringComparison.OrdinalIgnoreCase))
        {
            if (customerStore.TryGetById(trimmed, out var byId) && byId is not null)
                return Task.FromResult(byId);
            throw new CustomerNotFoundException(identifier);
        }

        // 3. Treat as email (case-insensitive)
        if (customerStore.TryGetByEmail(trimmed, out var byEmail) && byEmail is not null)
            return Task.FromResult(byEmail);

        throw new CustomerNotFoundException(identifier);
    }
}
