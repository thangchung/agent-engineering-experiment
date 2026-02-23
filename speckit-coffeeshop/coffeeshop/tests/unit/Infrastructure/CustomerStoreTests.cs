using CoffeeShop.Counter.Domain;
using CoffeeShop.Counter.Infrastructure;
using Xunit;

namespace CoffeeShop.Tests.Unit.Infrastructure;

/// <summary>
/// TDD tests for InMemoryCustomerStore.
/// These tests confirm T012 implementation and must be green before or at T022-definition.
/// Also exercises the thread-safety behaviour required by the store contract.
/// </summary>
public sealed class CustomerStoreTests
{
    private static Customer MakeCustomer(string id, string email = "test@example.com") =>
        new(id, "Test", "User", email, "+1-555-0000",
            CustomerTier.Standard, new DateOnly(2024, 1, 1));

    // -----------------------------------------------------------------------
    // TryGetById
    // -----------------------------------------------------------------------

    [Fact]
    public void TryGetById_ExistingId_ReturnsTrueAndCustomer()
    {
        var store = new InMemoryCustomerStore();
        var customer = MakeCustomer("C-0001");
        store.TryAdd(customer);

        var found = store.TryGetById("C-0001", out var result);

        Assert.True(found);
        Assert.NotNull(result);
        Assert.Equal("C-0001", result!.Id);
    }

    [Fact]
    public void TryGetById_MissingId_ReturnsFalseAndNull()
    {
        var store = new InMemoryCustomerStore();

        var found = store.TryGetById("C-9999", out var result);

        Assert.False(found);
        Assert.Null(result);
    }

    // -----------------------------------------------------------------------
    // TryGetByEmail — case-insensitive
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("alice@example.com")]
    [InlineData("ALICE@EXAMPLE.COM")]
    [InlineData("Alice@Example.Com")]
    public void TryGetByEmail_CaseInsensitive_ReturnsCustomer(string lookupEmail)
    {
        var store = new InMemoryCustomerStore();
        store.TryAdd(MakeCustomer("C-0001", "alice@example.com"));

        var found = store.TryGetByEmail(lookupEmail, out var result);

        Assert.True(found);
        Assert.NotNull(result);
        Assert.Equal("C-0001", result!.Id);
    }

    [Fact]
    public void TryGetByEmail_UnknownEmail_ReturnsFalseAndNull()
    {
        var store = new InMemoryCustomerStore();

        var found = store.TryGetByEmail("nobody@example.com", out var result);

        Assert.False(found);
        Assert.Null(result);
    }

    // -----------------------------------------------------------------------
    // TryAdd uniqueness
    // -----------------------------------------------------------------------

    [Fact]
    public void TryAdd_DuplicateId_ReturnsFalseAndDoesNotReplace()
    {
        var store = new InMemoryCustomerStore();
        var original = MakeCustomer("C-0001", "first@example.com");
        var duplicate = new Customer(
            "C-0001", "Other", "Guy", "second@example.com",
            "+1-555-9999", CustomerTier.Gold, new DateOnly(2024, 5, 1));

        Assert.True(store.TryAdd(original));
        Assert.False(store.TryAdd(duplicate)); // duplicate Id

        store.TryGetById("C-0001", out var stored);
        Assert.Equal("first@example.com", stored!.Email); // original is unchanged
    }

    // -----------------------------------------------------------------------
    // Concurrent TryAdd uniqueness
    // -----------------------------------------------------------------------

    [Fact]
    public void TryAdd_ConcurrentSameId_ExactlyOneSucceeds()
    {
        var store = new InMemoryCustomerStore();
        const int threadCount = 20;
        int successCount = 0;

        var tasks = Enumerable.Range(0, threadCount).Select(_ =>
            Task.Run(() =>
            {
                var c = MakeCustomer("C-CONCURRENT", $"c{Guid.NewGuid():N}@x.com");
                if (store.TryAdd(c))
                    Interlocked.Increment(ref successCount);
            }));

        Task.WhenAll(tasks).GetAwaiter().GetResult();

        Assert.Equal(1, successCount);
    }

    // -----------------------------------------------------------------------
    // Email index is updated on TryAdd
    // -----------------------------------------------------------------------

    [Fact]
    public void TryAdd_NewCustomer_EmailIndexIsSearchable()
    {
        var store = new InMemoryCustomerStore();
        store.TryAdd(MakeCustomer("C-0002", "bob@example.com"));

        var found = store.TryGetByEmail("bob@example.com", out var result);

        Assert.True(found);
        Assert.Equal("C-0002", result!.Id);
    }
}
