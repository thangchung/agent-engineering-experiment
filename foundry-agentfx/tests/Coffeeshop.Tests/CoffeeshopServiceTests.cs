using Coffeeshop.Mcp.Services;
using Coffeeshop.Models;

namespace Coffeeshop.Tests;

public class InMemoryMenuServiceTests
{
    private readonly IMenuService _service = new InMemoryMenuService();

    [Fact]
    public async Task GetAllItems_Returns_NonEmpty_List()
    {
        var items = await _service.GetAllItemsAsync();
        Assert.NotEmpty(items);
    }

    [Fact]
    public async Task GetByIdAsync_Known_Id_Returns_Item()
    {
        var item = await _service.GetByIdAsync("latte");
        Assert.NotNull(item);
        Assert.Equal("latte", item.Id);
    }

    [Fact]
    public async Task GetByIdAsync_Unknown_Id_Returns_Null()
    {
        var item = await _service.GetByIdAsync("notexist");
        Assert.Null(item);
    }

    [Fact]
    public async Task GetByIdAsync_CaseInsensitive()
    {
        var item = await _service.GetByIdAsync("LATTE");
        Assert.NotNull(item);
        Assert.Equal("latte", item.Id);
    }

    [Fact]
    public async Task GetAllItems_Contains_Coffee_Category()
    {
        var items = await _service.GetAllItemsAsync();
        Assert.Contains(items, i => i.Category == "Coffee");
    }
}

public class InMemoryCustomerServiceTests
{
    private readonly ICustomerService _service = new InMemoryCustomerService();

    [Fact]
    public async Task LookupByName_Returns_Customer()
    {
        var customer = await _service.LookupAsync("Alice");
        Assert.NotNull(customer);
        Assert.Equal("cust-001", customer.Id);
    }

    [Fact]
    public async Task LookupByPhone_Returns_Customer()
    {
        var customer = await _service.LookupAsync("555-0102");
        Assert.NotNull(customer);
        Assert.Equal("cust-002", customer.Id);
    }

    [Fact]
    public async Task LookupByEmail_Returns_Customer()
    {
        var customer = await _service.LookupAsync("charlie@example.com");
        Assert.NotNull(customer);
        Assert.Equal("cust-003", customer.Id);
    }

    [Fact]
    public async Task Lookup_Unknown_Returns_Null()
    {
        var customer = await _service.LookupAsync("nobody");
        Assert.Null(customer);
    }
}

public class InMemoryOrderServiceTests
{
    private readonly IMenuService _menuService = new InMemoryMenuService();
    private IOrderService CreateService() => new InMemoryOrderService(_menuService, new NullAuditService());

    private sealed class NullAuditService : IAuditService
    {
        public Task WriteOrderAuditAsync(OrderResult order, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task SubmitOrder_Valid_Returns_OrderResult()
    {
        var service = CreateService();
        var result = await service.SubmitAsync("cust-001",
        [
            new OrderItem("latte", "", 2, 0),
        ]);

        Assert.NotNull(result);
        Assert.StartsWith("ORD-", result.OrderId);
        Assert.Equal("cust-001", result.CustomerId);
        Assert.Equal(2, result.Items[0].Quantity);
        Assert.Equal(4.50m * 2, result.Total);
    }

    [Fact]
    public async Task SubmitOrder_MultipleItems_TotalIsCorrect()
    {
        var service = CreateService();
        var result = await service.SubmitAsync("cust-001",
        [
            new OrderItem("latte", "", 1, 0),
            new OrderItem("croissant", "", 2, 0),
        ]);

        Assert.Equal(4.50m + 3.50m * 2, result.Total);
    }

    [Fact]
    public async Task SubmitOrder_UnknownItem_Throws()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitAsync("cust-001", [new OrderItem("notexist", "", 1, 0)]));
    }

    [Fact]
    public async Task SubmitOrder_EmptyCustomerId_Throws()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SubmitAsync("", [new OrderItem("latte", "", 1, 0)]));
    }

    [Fact]
    public async Task SubmitOrder_EmptyItems_Throws()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SubmitAsync("cust-001", []));
    }

    [Fact]
    public async Task SubmitOrder_ZeroQuantity_Throws()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SubmitAsync("cust-001", [new OrderItem("latte", "", 0, 0)]));
    }
}

public class OrderAuditServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"audit-test-{Guid.NewGuid():N}");

    private sealed class SimpleConfig : Microsoft.Extensions.Configuration.IConfiguration
    {
        private readonly Dictionary<string, string?> _data;
        public SimpleConfig(Dictionary<string, string?> data) => _data = data;
        public string? this[string key] { get => _data.GetValueOrDefault(key); set { } }
        public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key)
            => new SimpleSection(key, _data);
        public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() => [];
        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() =>
            new Microsoft.Extensions.Primitives.CancellationChangeToken(CancellationToken.None);

        private sealed class SimpleSection(string key, Dictionary<string, string?> data)
            : Microsoft.Extensions.Configuration.IConfigurationSection
        {
            public string? this[string k] { get => data.GetValueOrDefault($"{key}:{k}"); set { } }
            public string Key => key;
            public string Path => key;
            public string? Value { get => data.GetValueOrDefault(key); set { } }
            public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string k)
                => new SimpleSection($"{key}:{k}", data);
            public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() => [];
            public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() =>
                new Microsoft.Extensions.Primitives.CancellationChangeToken(CancellationToken.None);
        }
    }

    [Fact]
    public async Task WriteOrderAudit_Creates_Markdown_File()
    {
        var config = new SimpleConfig(new() { ["Audit:OrdersPath"] = _tempDir });
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderAuditService>.Instance;
        var service = new OrderAuditService(config, logger);

        var order = new OrderResult(
            OrderId: "ORD-20260101-ABCDEF",
            CustomerId: "cust-001",
            CustomerName: "Alice",
            Items: [new OrderItem("latte", "Caffè Latte", 2, 4.50m)],
            Total: 9.00m,
            PlacedAt: new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

        await service.WriteOrderAuditAsync(order);

        var date = order.PlacedAt.ToString("yyyy-MM-dd");
        var path = Path.Combine(_tempDir, date, $"{order.OrderId}.md");
        Assert.True(File.Exists(path));

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("ORD-20260101-ABCDEF", content);
        Assert.Contains("Alice", content);
        Assert.Contains("$9.00", content);
        Assert.Contains("AuditAgent", content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
