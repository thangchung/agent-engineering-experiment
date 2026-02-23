namespace CoffeeShop.Counter.Domain;

public enum CustomerTier
{
    Gold,
    Silver,
    Standard
}

public enum ItemCategory
{
    Beverages,
    Food,
    Others
}

public enum ItemType
{
    // Beverages
    CAPPUCCINO,
    COFFEE_BLACK,
    COFFEE_WITH_ROOM,
    ESPRESSO,
    ESPRESSO_DOUBLE,
    LATTE,

    // Food
    CAKEPOP,
    CROISSANT,
    MUFFIN,
    CROISSANT_CHOCOLATE,

    // Others
    CHICKEN_MEATBALLS
}

public enum OrderStatus
{
    Pending,
    Confirmed,
    Preparing,
    Ready,
    Completed,
    Cancelled
}
