using CounterService.Domain;

namespace CoffeeShop.Evals;

/// <summary>
/// The same fixed 11-item catalog the app serves (research.md §15.2, `ProductCatalogService
/// .Domain.MenuItem.All`), as `CounterService.Domain.MenuItem` (the shape the Gate/Split/Extract
/// code actually takes). Evals run standalone (`dotnet test tests/CoffeeShop.Evals`, no MCP
/// server), so this is a literal copy of the catalog's values rather than a live `GET /menu` -
/// if the catalog's menu ever changes, this needs updating too (there's no single source shared
/// across process boundaries; T06's `CatalogClient` is the production path).
/// </summary>
public static class EvalMenu
{
    public static readonly IReadOnlyList<MenuItem> Items =
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
