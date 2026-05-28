namespace Coffeeshop.Mcp.Services;

using Coffeeshop.Models;

public sealed class InMemoryCustomerService : ICustomerService
{
    private static readonly List<Customer> _customers =
    [
        new("cust-001", "Alice Johnson", "alice@example.com", "555-0101"),
        new("cust-002", "Bob Smith", "bob@example.com", "555-0102"),
        new("cust-003", "Charlie Brown", "charlie@example.com", "555-0103"),
        new("cust-004", "Diana Prince", "diana@example.com", "555-0104"),
    ];

    public Task<Customer?> LookupAsync(string nameOrPhone, CancellationToken ct = default)
    {
        var normalized = nameOrPhone.Trim();
        var match = _customers.FirstOrDefault(c =>
            c.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            c.Phone.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            c.Email.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match);
    }
}
