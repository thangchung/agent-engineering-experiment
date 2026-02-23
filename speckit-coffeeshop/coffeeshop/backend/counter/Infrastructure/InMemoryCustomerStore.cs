using System.Collections.Concurrent;
using CoffeeShop.Counter.Domain;

namespace CoffeeShop.Counter.Infrastructure;

/// <summary>
/// Thread-safe in-memory customer store backed by ConcurrentDictionary.
/// Lookup by Id, Email (case-insensitive), or resolved via orderId.
/// </summary>
public sealed class InMemoryCustomerStore
{
    private readonly ConcurrentDictionary<string, Customer> _customers = new(StringComparer.Ordinal);
    // Email (lowercased) → CustomerId
    private readonly Dictionary<string, string> _emailIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _indexLock = new();

    /// <summary>Try to fetch a customer by their Id ("C-XXXX").</summary>
    public bool TryGetById(string id, out Customer? customer)
    {
        var found = _customers.TryGetValue(id, out var c);
        customer = c;
        return found;
    }

    /// <summary>Try to fetch a customer by email address (case-insensitive).</summary>
    public bool TryGetByEmail(string email, out Customer? customer)
    {
        customer = null;
        string? customerId;
        lock (_indexLock)
        {
            if (!_emailIndex.TryGetValue(email, out customerId))
                return false;
        }
        return _customers.TryGetValue(customerId, out customer);
    }

    /// <summary>
    /// Add a customer if no customer with the same Id already exists.
    /// Also registers the email in the secondary index.
    /// Returns false if a duplicate Id is detected (no-op).
    /// </summary>
    public bool TryAdd(Customer customer)
    {
        if (!_customers.TryAdd(customer.Id, customer))
            return false;

        lock (_indexLock)
        {
            _emailIndex.TryAdd(customer.Email.ToLowerInvariant(), customer.Id);
        }

        return true;
    }
}
