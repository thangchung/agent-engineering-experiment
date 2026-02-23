using CoffeeShop.Counter.Application;
using CoffeeShop.Counter.Application.Ports;
using CoffeeShop.Counter.Application.UseCases;
using CoffeeShop.Counter.Domain;
using CoffeeShop.Counter.Infrastructure;
using Moq;
using Xunit;

namespace CoffeeShop.Tests.Unit.UseCases;

/// <summary>
/// TDD tests for PlaceOrder use case.
/// These tests MUST fail before T035 implementation is written.
///
/// Tests validate the pre-placement sequence (SC-008):
///   1. Non-empty items  (FR-015)
///   2. Quantity 1–5     (FR-024)
///   3. MCP availability (FR-021, FR-016)
///   4. Pickup-time calc (FR-008)
///   5. TryAdd only after all validation passes
/// </summary>
public sealed class PlaceOrderTests
{
    // -----------------------------------------------------------------------
    // Fixture helpers
    // -----------------------------------------------------------------------
    private static readonly Customer AliceGold = new(
        "C-1001", "Alice", "Johnson", "alice@example.com",
        "+1-555-0101", CustomerTier.Gold, new DateOnly(2023, 6, 15));

    private static MenuItem Item(ItemType type, ItemCategory cat, bool available = true) =>
        new(type, type.ToString(), cat, 4.00m, available);

    private static OrderItemRequest Req(ItemType type, int qty = 1) =>
        new(type.ToString(), qty);

    private static PlaceOrder CreateSut(
        IProductCatalogClient catalog,
        InMemoryOrderStore? orderStore = null,
        InMemoryCustomerStore? customerStore = null)
    {
        var cs = customerStore ?? new InMemoryCustomerStore();
        cs.TryAdd(AliceGold);

        return new PlaceOrder(
            cs,
            orderStore ?? new InMemoryOrderStore(),
            catalog);
    }

    private static IProductCatalogClient CatalogWith(params MenuItem[] items)
    {
        var mock = new Mock<IProductCatalogClient>();
        mock.Setup(c => c.GetMenuItemsAsync(default))
            .ReturnsAsync((IReadOnlyList<MenuItem>)items);
        return mock.Object;
    }

    private static IProductCatalogClient UnavailableCatalog()
    {
        var mock = new Mock<IProductCatalogClient>();
        mock.Setup(c => c.GetMenuItemsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProductCatalogUnavailableException(
                "Menu is currently unavailable — please try again shortly."));
        return mock.Object;
    }

    // -----------------------------------------------------------------------
    // FR-015: empty order → OrderValidationException
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EmptyItems_ThrowsOrderValidationException()
    {
        var sut = CreateSut(CatalogWith());
        var request = new PlaceOrderRequest("C-1001", Array.Empty<OrderItemRequest>(), null);

        await Assert.ThrowsAsync<OrderValidationException>(
            () => sut.ExecuteAsync(request));
    }

    // -----------------------------------------------------------------------
    // FR-024: invalid quantity
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task InvalidQuantity_ThrowsOrderValidationException(int qty)
    {
        var sut = CreateSut(CatalogWith(
            Item(ItemType.LATTE, ItemCategory.Beverages)));

        var request = new PlaceOrderRequest("C-1001",
            new[] { Req(ItemType.LATTE, qty) }, null);

        await Assert.ThrowsAsync<OrderValidationException>(
            () => sut.ExecuteAsync(request));
    }

    // -----------------------------------------------------------------------
    // FR-021: product-catalog unreachable → ProductCatalogUnavailableException
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CatalogUnavailable_ThrowsProductCatalogUnavailableException()
    {
        var sut = CreateSut(UnavailableCatalog());
        var request = new PlaceOrderRequest("C-1001",
            new[] { Req(ItemType.LATTE) }, null);

        await Assert.ThrowsAsync<ProductCatalogUnavailableException>(
            () => sut.ExecuteAsync(request));
    }

    // -----------------------------------------------------------------------
    // FR-016: unavailable item → ItemUnavailableException
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UnavailableItem_ThrowsItemUnavailableException()
    {
        var sut = CreateSut(CatalogWith(
            Item(ItemType.LATTE, ItemCategory.Beverages, available: false)));

        var request = new PlaceOrderRequest("C-1001",
            new[] { Req(ItemType.LATTE) }, null);

        await Assert.ThrowsAsync<ItemUnavailableException>(
            () => sut.ExecuteAsync(request));
    }

    // -----------------------------------------------------------------------
    // FR-008: pickup time calculation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BeveragesOnly_EstimatedPickupIsFiveMinutes()
    {
        var orderStore = new InMemoryOrderStore();
        var sut = CreateSut(CatalogWith(
            Item(ItemType.LATTE,      ItemCategory.Beverages),
            Item(ItemType.CAPPUCCINO, ItemCategory.Beverages)),
            orderStore);

        var request = new PlaceOrderRequest("C-1001",
            new[] { Req(ItemType.LATTE), Req(ItemType.CAPPUCCINO) }, null);

        var order = await sut.ExecuteAsync(request);

        Assert.Equal("Ready in about 5 minutes", order.EstimatedPickup);
    }

    [Fact]
    public async Task MixedOrder_EstimatedPickupIsTenMinutes()
    {
        var orderStore = new InMemoryOrderStore();
        var sut = CreateSut(CatalogWith(
            Item(ItemType.LATTE,     ItemCategory.Beverages),
            Item(ItemType.CROISSANT, ItemCategory.Food)),
            orderStore);

        var request = new PlaceOrderRequest("C-1001",
            new[] { Req(ItemType.LATTE), Req(ItemType.CROISSANT) }, null);

        var order = await sut.ExecuteAsync(request);

        Assert.Equal("Ready in about 10 minutes", order.EstimatedPickup);
    }

    [Fact]
    public async Task FoodOnly_EstimatedPickupIsTenMinutes()
    {
        var orderStore = new InMemoryOrderStore();
        var sut = CreateSut(CatalogWith(
            Item(ItemType.CROISSANT, ItemCategory.Food)),
            orderStore);

        var request = new PlaceOrderRequest("C-1001",
            new[] { Req(ItemType.CROISSANT) }, null);

        var order = await sut.ExecuteAsync(request);

        Assert.Equal("Ready in about 10 minutes", order.EstimatedPickup);
    }

    // -----------------------------------------------------------------------
    // SC-008: TryAdd to store ONLY after all validation passes
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidationFailure_OrderIsNeverAddedToStore()
    {
        var orderStore = new InMemoryOrderStore();
        var sut = CreateSut(
            CatalogWith(Item(ItemType.LATTE, ItemCategory.Beverages, available: false)),
            orderStore);

        var request = new PlaceOrderRequest("C-1001",
            new[] { Req(ItemType.LATTE) }, null);

        await Assert.ThrowsAsync<ItemUnavailableException>(
            () => sut.ExecuteAsync(request));

        // No order should be visible in the store
        var found = orderStore.GetByCustomerId("C-1001");
        Assert.Empty(found);
    }

    [Fact]
    public async Task ValidOrder_OrderAddedToStoreWithConfirmedStatus()
    {
        var orderStore = new InMemoryOrderStore();
        var sut = CreateSut(CatalogWith(
            Item(ItemType.LATTE, ItemCategory.Beverages)),
            orderStore);

        var request = new PlaceOrderRequest("C-1001",
            new[] { Req(ItemType.LATTE) }, "Extra hot");

        var order = await sut.ExecuteAsync(request);

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal("Extra hot", order.Notes);
        Assert.True(orderStore.TryGetById(order.Id, out _));
    }

    // -----------------------------------------------------------------------
    // Total price calculation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TotalPrice_IsSumOfLineTotals()
    {
        var sut = CreateSut(CatalogWith(
            new MenuItem(ItemType.LATTE,     "LATTE",     ItemCategory.Beverages, 4.50m, true),
            new MenuItem(ItemType.CROISSANT, "CROISSANT", ItemCategory.Food,      3.25m, true)));

        var request = new PlaceOrderRequest("C-1001",
            new[] { Req(ItemType.LATTE, 2), Req(ItemType.CROISSANT, 1) }, null);

        var order = await sut.ExecuteAsync(request);

        Assert.Equal(12.25m, order.TotalPrice); // 4.50*2 + 3.25*1
    }
}
