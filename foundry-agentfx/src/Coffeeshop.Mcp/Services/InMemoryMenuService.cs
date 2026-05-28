namespace Coffeeshop.Mcp.Services;

using Coffeeshop.Models;

public sealed class InMemoryMenuService : IMenuService
{
    private static readonly List<MenuItem> _items =
    [
        new("latte", "Caffè Latte", "Coffee", 4.50m, "Espresso with steamed milk"),
        new("cappuccino", "Cappuccino", "Coffee", 4.00m, "Espresso with foamed milk"),
        new("espresso", "Espresso", "Coffee", 3.00m, "Strong black coffee"),
        new("americano", "Americano", "Coffee", 3.50m, "Espresso with hot water"),
        new("mocha", "Mocha", "Coffee", 5.00m, "Espresso with chocolate and steamed milk"),
        new("green-tea", "Green Tea", "Tea", 3.00m, "Japanese sencha green tea"),
        new("croissant", "Butter Croissant", "Food", 3.50m, "Freshly baked butter croissant"),
        new("muffin", "Blueberry Muffin", "Food", 3.00m, "Baked with fresh blueberries"),
    ];

    public Task<IEnumerable<MenuItem>> GetAllItemsAsync(CancellationToken ct = default)
        => Task.FromResult<IEnumerable<MenuItem>>(_items);

    public Task<MenuItem?> GetByIdAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_items.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)));
}
