extern alias ProductCatalogServiceAsm;

using ProductCatalogServiceAsm::ProductCatalogService.Domain;

namespace CoffeeShop.Tests;

/// <summary>tasks.md T05 AC3.</summary>
public class MenuItemTests
{
    [Fact]
    public void All_HasElevenUniqueItems()
    {
        Assert.Equal(11, MenuItem.All.Count);
        Assert.Equal(11, MenuItem.All.Select(m => m.Id).Distinct().Count());
    }

    [Fact]
    public void All_EveryItem_HasPositivePriceWithAtMostTwoDecimals()
    {
        foreach (var item in MenuItem.All)
        {
            Assert.True(item.PriceUsd > 0, $"{item.Id} price must be positive");
            Assert.Equal(item.PriceUsd, decimal.Round(item.PriceUsd, 2));
        }
    }

    [Fact]
    public void All_EveryItem_HasNonEmptyDisplayName()
    {
        foreach (var item in MenuItem.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.DisplayName));
        }
    }

    [Fact]
    public void All_Latte_PricedAtFourFifty()
    {
        var latte = MenuItem.All.Single(m => m.Id == "LATTE");
        Assert.Equal(4.50m, latte.PriceUsd);
        Assert.Equal(Station.Barista, latte.Station);
    }
}
