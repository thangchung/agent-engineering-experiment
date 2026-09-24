namespace ProductCatalogService.Domain;

/// <summary>
/// The coffee shop's fixed 11-item catalog (research.md §15.2). Station is the golden-truth
/// classification used by the evals (research.md §10.3), derived from the original
/// coffeeshop-agent's <c>(int)ItemType &lt;= 5</c> rule: the first 6 enum values are beverages.
/// </summary>
public enum Station
{
    Barista,
    Kitchen,
}

public sealed record MenuItem(string Id, string DisplayName, decimal PriceUsd, Station Station)
{
    public static readonly IReadOnlyList<MenuItem> All =
    [
        new("CAPPUCCINO", "Cappuccino", 4.50m, Station.Barista),
        new("COFFEE_BLACK", "Black coffee", 3.00m, Station.Barista),
        new("COFFEE_WITH_ROOM", "Coffee with room", 3.25m, Station.Barista),
        new("ESPRESSO", "Espresso", 3.00m, Station.Barista),
        new("ESPRESSO_DOUBLE", "Double espresso", 3.75m, Station.Barista),
        new("LATTE", "Latte", 4.50m, Station.Barista),
        new("CAKEPOP", "Cake pop", 2.50m, Station.Kitchen),
        new("CROISSANT", "Croissant", 3.25m, Station.Kitchen),
        new("MUFFIN", "Muffin", 3.00m, Station.Kitchen),
        new("CROISSANT_CHOCOLATE", "Chocolate croissant", 3.75m, Station.Kitchen),
        new("CHICKEN_MEATBALLS", "Chicken meatballs", 7.50m, Station.Kitchen),
    ];
}
