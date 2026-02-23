using CoffeeShop.Counter.Domain;

namespace CoffeeShop.Counter.Infrastructure;

/// <summary>
/// Populates in-memory stores with demo customers, menu items, and orders at startup.
/// All IDs use out-of-runtime ranges to avoid collision with runtime-generated IDs.
/// </summary>
public static class SeedData
{
    // -----------------------------------------------------------------
    // Customers
    // -----------------------------------------------------------------
    public static IReadOnlyList<Customer> Customers { get; } = new List<Customer>
    {
        new("C-1001", "Alice", "Johnson", "alice@example.com",   "+1-555-0101", CustomerTier.Gold,     new DateOnly(2023, 6, 15)),
        new("C-1002", "Bob",   "Martinez","bob.m@example.com",   "+1-555-0102", CustomerTier.Silver,   new DateOnly(2024, 1, 22)),
        new("C-1003", "Carol", "Wei",      "carol.wei@example.com","+1-555-0103", CustomerTier.Standard, new DateOnly(2025, 3, 10)),
    };

    // -----------------------------------------------------------------
    // Product Catalogue (also used by product-catalog service)
    // -----------------------------------------------------------------
    public static IReadOnlyList<MenuItem> MenuItems { get; } = new List<MenuItem>
    {
        new(ItemType.CAPPUCCINO,           "CAPPUCCINO",           ItemCategory.Beverages, 4.50m, true),
        new(ItemType.COFFEE_BLACK,         "COFFEE BLACK",         ItemCategory.Beverages, 3.00m, true),
        new(ItemType.COFFEE_WITH_ROOM,     "COFFEE WITH ROOM",     ItemCategory.Beverages, 3.25m, true),
        new(ItemType.ESPRESSO,             "ESPRESSO",             ItemCategory.Beverages, 3.50m, true),
        new(ItemType.ESPRESSO_DOUBLE,      "ESPRESSO DOUBLE",      ItemCategory.Beverages, 4.00m, true),
        new(ItemType.LATTE,                "LATTE",                ItemCategory.Beverages, 4.50m, true),
        new(ItemType.CAKEPOP,              "CAKEPOP",              ItemCategory.Food,      2.50m, true),
        new(ItemType.CROISSANT,            "CROISSANT",            ItemCategory.Food,      3.25m, true),
        new(ItemType.MUFFIN,               "MUFFIN",               ItemCategory.Food,      3.00m, true),
        new(ItemType.CROISSANT_CHOCOLATE,  "CROISSANT CHOCOLATE",  ItemCategory.Food,      3.75m, true),
        new(ItemType.CHICKEN_MEATBALLS,    "CHICKEN MEATBALLS",    ItemCategory.Others,    4.25m, true),
    };

    // -----------------------------------------------------------------
    // Demo Orders (ORD-5xxx — outside runtime range ORD-6000..ORD-9999)
    // -----------------------------------------------------------------
    public static IReadOnlyList<Order> Orders { get; } = BuildOrders();

    private static IReadOnlyList<Order> BuildOrders()
    {
        var now = DateTimeOffset.UtcNow;

        // ORD-5001  C-1001  Completed  LATTE×1 + CROISSANT×1  $7.75
        var items5001 = new List<OrderItem>
        {
            new(ItemType.LATTE,     "LATTE",     1, 4.50m, 4.50m),
            new(ItemType.CROISSANT, "CROISSANT", 1, 3.25m, 3.25m),
        };
        var ord5001 = new Order(
            "ORD-5001", "C-1001", items5001.AsReadOnly(),
            7.75m, OrderStatus.Completed,
            null, "Ready in about 5 minutes",
            now.AddHours(-3), now.AddHours(-2));

        // ORD-5002  C-1001  Preparing  CAPPUCCINO×1 + CAKEPOP×1  $7.00
        var items5002 = new List<OrderItem>
        {
            new(ItemType.CAPPUCCINO, "CAPPUCCINO", 1, 4.50m, 4.50m),
            new(ItemType.CAKEPOP,    "CAKEPOP",    1, 2.50m, 2.50m),
        };
        var ord5002 = new Order(
            "ORD-5002", "C-1001", items5002.AsReadOnly(),
            7.00m, OrderStatus.Preparing,
            null, "Ready in about 10 minutes",
            now.AddMinutes(-30), now.AddMinutes(-20));

        // ORD-5003  C-1002  Pending  ESPRESSO_DOUBLE×1 + MUFFIN×1 + COFFEE_BLACK×1  $10.00
        var items5003 = new List<OrderItem>
        {
            new(ItemType.ESPRESSO_DOUBLE, "ESPRESSO DOUBLE", 1, 4.00m, 4.00m),
            new(ItemType.MUFFIN,          "MUFFIN",          1, 3.00m, 3.00m),
            new(ItemType.COFFEE_BLACK,    "COFFEE BLACK",    1, 3.00m, 3.00m),
        };
        var ord5003 = new Order(
            "ORD-5003", "C-1002", items5003.AsReadOnly(),
            10.00m, OrderStatus.Pending,
            null, null,
            now.AddMinutes(-5), now.AddMinutes(-5));

        return new List<Order> { ord5001, ord5002, ord5003 }.AsReadOnly();
    }

    // -----------------------------------------------------------------
    // Load into stores
    // -----------------------------------------------------------------
    public static void Load(InMemoryCustomerStore customerStore, InMemoryOrderStore orderStore)
    {
        foreach (var c in Customers)
            customerStore.TryAdd(c);

        foreach (var o in Orders)
            orderStore.TryAdd(o);
    }
}
