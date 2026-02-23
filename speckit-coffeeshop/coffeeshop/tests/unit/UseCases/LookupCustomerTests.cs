using CoffeeShop.Counter.Application;
using CoffeeShop.Counter.Application.UseCases;
using CoffeeShop.Counter.Domain;
using CoffeeShop.Counter.Infrastructure;
using Xunit;

namespace CoffeeShop.Tests.Unit.UseCases;

/// <summary>
/// TDD tests for LookupCustomer use case.
/// These tests MUST fail before T023 implementation is written.
/// </summary>
public sealed class LookupCustomerTests
{
    // -----------------------------------------------------------------------
    // Shared test fixture: pre-seeded stores
    // -----------------------------------------------------------------------
    private readonly InMemoryCustomerStore _customerStore;
    private readonly InMemoryOrderStore _orderStore;
    private readonly LookupCustomer _sut;

    public LookupCustomerTests()
    {
        _customerStore = new InMemoryCustomerStore();
        _orderStore = new InMemoryOrderStore();

        // Seed customers
        _customerStore.TryAdd(new Customer(
            "C-1001", "Alice", "Johnson", "alice@example.com",
            "+1-555-0101", CustomerTier.Gold, new DateOnly(2023, 6, 15)));
        _customerStore.TryAdd(new Customer(
            "C-1002", "Bob", "Martinez", "bob.m@example.com",
            "+1-555-0102", CustomerTier.Silver, new DateOnly(2024, 1, 22)));

        // Seed order belonging to C-1001 (to test orderId→customer resolution)
        var items = new List<OrderItem>
        {
            new(ItemType.LATTE, "LATTE", 1, 4.50m, 4.50m),
        }.AsReadOnly();
        var order = new Order(
            "ORD-5001", "C-1001", items, 4.50m, OrderStatus.Completed,
            null, null, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow);
        _orderStore.TryAdd(order);

        _sut = new LookupCustomer(_customerStore, _orderStore);
    }

    // -----------------------------------------------------------------------
    // Lookup by customer ID
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LookupByCustomerId_ExistingId_ReturnsCustomer()
    {
        var result = await _sut.ExecuteAsync("C-1001");

        Assert.Equal("C-1001", result.Id);
        Assert.Equal("Alice", result.FirstName);
        Assert.Equal(CustomerTier.Gold, result.Tier);
    }

    [Fact]
    public async Task LookupByCustomerId_UnknownId_ThrowsCustomerNotFoundException()
    {
        await Assert.ThrowsAsync<CustomerNotFoundException>(
            () => _sut.ExecuteAsync("C-9999"));
    }

    // -----------------------------------------------------------------------
    // Lookup by email (case-insensitive)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("alice@example.com")]
    [InlineData("ALICE@EXAMPLE.COM")]
    [InlineData("Alice@Example.Com")]
    public async Task LookupByEmail_CaseInsensitive_ReturnsCorrectCustomer(string email)
    {
        var result = await _sut.ExecuteAsync(email);

        Assert.Equal("C-1001", result.Id);
        Assert.Equal("Alice", result.FirstName);
    }

    [Fact]
    public async Task LookupByEmail_UnknownEmail_ThrowsCustomerNotFoundException()
    {
        await Assert.ThrowsAsync<CustomerNotFoundException>(
            () => _sut.ExecuteAsync("nobody@nowhere.com"));
    }

    // -----------------------------------------------------------------------
    // Lookup by order ID (orderId → customerId → customer)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LookupByOrderId_ExistingOrder_ReturnsOrderOwner()
    {
        var result = await _sut.ExecuteAsync("ORD-5001");

        Assert.Equal("C-1001", result.Id);
        Assert.Equal("Alice", result.FirstName);
    }

    [Fact]
    public async Task LookupByOrderId_UnknownOrder_ThrowsCustomerNotFoundException()
    {
        await Assert.ThrowsAsync<CustomerNotFoundException>(
            () => _sut.ExecuteAsync("ORD-9999"));
    }

    // -----------------------------------------------------------------------
    // Blank / empty identifier
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LookupByBlankIdentifier_ThrowsCustomerNotFoundException(string identifier)
    {
        await Assert.ThrowsAsync<CustomerNotFoundException>(
            () => _sut.ExecuteAsync(identifier));
    }

    // -----------------------------------------------------------------------
    // Resolution order: orderId checked before customerId before email
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LookupResolutionOrder_OrderIdPrefix_TakesOrderIdPathFirst()
    {
        // ORD-5001 maps to C-1001 (Alice).
        // Verifies the ORD-prefix triggers the order-ID path.
        var result = await _sut.ExecuteAsync("ORD-5001");
        Assert.Equal("Alice", result.FirstName);
    }

    [Fact]
    public async Task LookupResolutionOrder_CustomerIdPrefix_TakesCustomerIdPath()
    {
        // C-1002 maps to Bob without any email or order context.
        var result = await _sut.ExecuteAsync("C-1002");
        Assert.Equal("Bob", result.FirstName);
    }
}
