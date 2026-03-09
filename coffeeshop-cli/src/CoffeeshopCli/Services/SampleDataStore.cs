using CoffeeshopCli.Models;

namespace CoffeeshopCli.Services;

/// <summary>
/// In-memory sample data store with hardcoded menu items and customers.
/// Mirrors _DEFAULT_CATALOG from product_catalogs.py and _DEFAULT_CUSTOMERS from orders.py.
/// No MCP client dependency — pure static data for development/testing.
/// </summary>
public static class SampleDataStore
{
    public static readonly IReadOnlyList<MenuItem> Menu = new List<MenuItem>
    {
        // Beverages
        new MenuItem { ItemType = ItemType.CAPPUCCINO, Name = "Cappuccino", Category = "Beverages", Price = 4.50m },
        new MenuItem { ItemType = ItemType.COFFEE_BLACK, Name = "Coffee Black", Category = "Beverages", Price = 3.00m },
        new MenuItem { ItemType = ItemType.COFFEE_WITH_ROOM, Name = "Coffee With Room", Category = "Beverages", Price = 3.25m },
        new MenuItem { ItemType = ItemType.ESPRESSO, Name = "Espresso", Category = "Beverages", Price = 3.50m },
        new MenuItem { ItemType = ItemType.ESPRESSO_DOUBLE, Name = "Espresso Double", Category = "Beverages", Price = 4.00m },
        new MenuItem { ItemType = ItemType.LATTE, Name = "Latte", Category = "Beverages", Price = 4.50m },
        
        // Food
        new MenuItem { ItemType = ItemType.CAKEPOP, Name = "Cakepop", Category = "Food", Price = 2.50m },
        new MenuItem { ItemType = ItemType.CROISSANT, Name = "Croissant", Category = "Food", Price = 3.25m },
        new MenuItem { ItemType = ItemType.MUFFIN, Name = "Muffin", Category = "Food", Price = 3.00m },
        new MenuItem { ItemType = ItemType.CROISSANT_CHOCOLATE, Name = "Croissant Chocolate", Category = "Food", Price = 3.75m },
        
        // Others
        new MenuItem { ItemType = ItemType.CHICKEN_MEATBALLS, Name = "Chicken Meatballs", Category = "Others", Price = 4.25m }
    }.AsReadOnly();

    public static readonly IReadOnlyList<Customer> Customers = new List<Customer>
    {
        new Customer
        {
            CustomerId = "C-1001",
            Name = "Alice Smith",
            Email = "alice@example.com",
            Phone = "+1-555-0100",
            Tier = CustomerTier.Gold,
            AccountCreated = new DateOnly(2023, 1, 10)
        }
    }.AsReadOnly();

    /// <summary>Get a customer by email (case-insensitive).</summary>
    public static Customer? GetCustomerByEmail(string email)
    {
        return Customers.FirstOrDefault(c =>
            c.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Get a customer by ID (case-insensitive).</summary>
    public static Customer? GetCustomerById(string customerId)
    {
        return Customers.FirstOrDefault(c =>
            c.CustomerId.Equals(customerId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Get a menu item by type.</summary>
    public static MenuItem? GetMenuItemByType(ItemType itemType)
    {
        return Menu.FirstOrDefault(m => m.ItemType == itemType);
    }
}
