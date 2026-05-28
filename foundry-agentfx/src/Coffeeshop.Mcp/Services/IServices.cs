namespace Coffeeshop.Mcp.Services;

using Coffeeshop.Models;

public interface IMenuService
{
    Task<IEnumerable<MenuItem>> GetAllItemsAsync(CancellationToken ct = default);
    Task<MenuItem?> GetByIdAsync(string id, CancellationToken ct = default);
}

public interface ICustomerService
{
    Task<Customer?> LookupAsync(string nameOrPhone, CancellationToken ct = default);
}

public interface IOrderService
{
    Task<OrderResult> SubmitAsync(string customerId, List<OrderItem> items, CancellationToken ct = default);
}
