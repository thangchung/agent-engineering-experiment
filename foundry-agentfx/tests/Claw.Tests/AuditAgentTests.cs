using Claw.Api.Agents;
using Coffeeshop.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Claw.Tests;

public class AuditAgentTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"audit-claw-{Guid.NewGuid():N}");

    private AuditWorkflowExecutor CreateExecutor()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Audit:OrdersPath"] = _tempDir })
            .Build();
        return new AuditWorkflowExecutor(config, NullLogger.Instance);
    }

    [Fact]
    public void AuditAgent_Name_Is_AuditAgent()
    {
        var executor = CreateExecutor();
        Assert.Equal("AuditAgent", executor.Name);
    }

    [Fact]
    public async Task LogAsync_Writes_Markdown_File()
    {
        var executor = CreateExecutor();
        var order = new OrderResult(
            OrderId: "ORD-TEST-001",
            CustomerId: "cust-1",
            CustomerName: "Bob",
            Items: [new OrderItem("espresso", "Espresso", 1, 3.50m)],
            Total: 3.50m,
            PlacedAt: new DateTimeOffset(2026, 5, 25, 10, 0, 0, TimeSpan.Zero));

        await executor.LogAsync(order);

        var path = Path.Combine(_tempDir, "2026-05-25", "ORD-TEST-001.md");
        Assert.True(File.Exists(path));
        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("ORD-TEST-001", content);
        Assert.Contains("Bob", content);
        Assert.Contains("3.50", content);
        Assert.Contains("AuditAgent", content);
    }

    [Fact]
    public async Task LogAsync_Multiple_Orders_Same_Day()
    {
        var executor = CreateExecutor();
        var day = new DateTimeOffset(2026, 5, 25, 10, 0, 0, TimeSpan.Zero);
        var order1 = new OrderResult("ORD-001", "c1", "Alice", [], 0m, day);
        var order2 = new OrderResult("ORD-002", "c2", "Bob", [], 0m, day);

        await executor.LogAsync(order1);
        await executor.LogAsync(order2);

        var dir = Path.Combine(_tempDir, "2026-05-25");
        Assert.True(File.Exists(Path.Combine(dir, "ORD-001.md")));
        Assert.True(File.Exists(Path.Combine(dir, "ORD-002.md")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
