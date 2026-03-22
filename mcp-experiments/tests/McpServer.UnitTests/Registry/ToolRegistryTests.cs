using McpServer.Registry;

namespace McpServer.UnitTests.Registry;

public sealed class ToolRegistryTests
{
    [Fact]
    public void GetVisibleTools_HonorsVisibility()
    {
        ToolRegistry registry = new(
        [
            TestTools.Create("visible", "Visible", "{}", isVisible: _ => true),
            TestTools.Create("hidden", "Hidden", "{}", isVisible: _ => false),
        ]);

        IReadOnlyList<ToolDescriptor> visible = registry.GetVisibleTools(new UserContext());

        Assert.Single(visible);
        Assert.Equal("visible", visible[0].Name);
    }

    [Fact]
    public async Task InvokeAsync_UnknownToolThrows()
    {
        ToolRegistry registry = new([TestTools.Create("known", "Known", "{}")] );

        await Assert.ThrowsAsync<ToolNotFoundException>(() =>
            registry.InvokeAsync("missing", TestTools.EmptyArgs(), new UserContext(), CancellationToken.None));
    }

    [Fact]
    public async Task InvokeAsync_HiddenToolDenied()
    {
        ToolRegistry registry = new(
        [
            TestTools.Create("hidden", "Hidden", "{}", isVisible: _ => false),
        ]);

        await Assert.ThrowsAsync<ToolAccessDeniedException>(() =>
            registry.InvokeAsync("hidden", TestTools.EmptyArgs(), new UserContext(), CancellationToken.None));
    }
}
