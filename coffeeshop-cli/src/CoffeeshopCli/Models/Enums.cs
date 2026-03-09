namespace CoffeeshopCli.Models;

/// <summary>
/// Item types available in the coffeeshop menu.
/// Source: product_catalogs.py — _DEFAULT_CATALOG
/// </summary>
public enum ItemType
{
    CAPPUCCINO,
    COFFEE_BLACK,
    COFFEE_WITH_ROOM,
    ESPRESSO,
    ESPRESSO_DOUBLE,
    LATTE,
    CAKEPOP,
    CROISSANT,
    MUFFIN,
    CROISSANT_CHOCOLATE,
    CHICKEN_MEATBALLS
}

/// <summary>
/// Order status lifecycle.
/// Source: orders.py — _DEFAULT_ORDERS
/// </summary>
public enum OrderStatus
{
    Pending,
    Confirmed,
    Preparing,
    Ready,
    Completed,
    Cancelled
}

/// <summary>
/// Customer tier/loyalty level.
/// Source: orders.py — _DEFAULT_CUSTOMERS
/// </summary>
public enum CustomerTier
{
    Standard,
    Silver,
    Gold
}
