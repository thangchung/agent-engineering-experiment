using ToolSearch.Gateway.Registry;
using ToolSearch.Gateway.Search;

namespace ToolSearch.Tests;

public class TextNormalizerTests
{
    [Fact]
    public void Tokenize_Returns_Lowercase_Tokens()
    {
        var tokens = TextNormalizer.Tokenize("Hello World");
        Assert.Contains("hello", tokens);
        Assert.Contains("world", tokens);
    }

    [Fact]
    public void Tokenize_Null_Returns_Empty()
    {
        var tokens = TextNormalizer.Tokenize(null);
        Assert.Empty(tokens);
    }

    [Fact]
    public void Tokenize_Empty_Returns_Empty()
    {
        var tokens = TextNormalizer.Tokenize("   ");
        Assert.Empty(tokens);
    }

    [Fact]
    public void Tokenize_Deduplicates()
    {
        var tokens = TextNormalizer.Tokenize("coffee coffee latte");
        Assert.Equal(tokens.Distinct(StringComparer.Ordinal).Count(), tokens.Length);
    }

    [Fact]
    public void Tokenize_Splits_On_Punctuation()
    {
        var tokens = TextNormalizer.Tokenize("coffee-latte");
        Assert.Contains("coffee", tokens);
        Assert.Contains("latte", tokens);
    }
}

public class ToolRegistryTests
{
    private static ToolDescriptor MakeTool(string name, string description)
        => new(name, description, """{"type":"object"}""",
            [], false, false, _ => true,
            (_, _) => Task.FromResult<object?>(null));

    [Fact]
    public void GetVisibleTools_Returns_All_Tools()
    {
        var tools = new List<ToolDescriptor>
        {
            MakeTool("tool_a", "First tool"),
            MakeTool("tool_b", "Second tool"),
        };
        var registry = new ToolRegistry(tools);
        var visible = registry.GetVisibleTools(new UserContext());
        Assert.Equal(2, visible.Count);
    }

    [Fact]
    public void FindByName_CaseInsensitive()
    {
        var registry = new ToolRegistry([MakeTool("my_tool", "Test")]);
        Assert.NotNull(registry.FindByName("MY_TOOL", new UserContext()));
        Assert.NotNull(registry.FindByName("my_tool", new UserContext()));
    }

    [Fact]
    public void FindByName_Unknown_Returns_Null()
    {
        var registry = new ToolRegistry([MakeTool("my_tool", "Test")]);
        Assert.Null(registry.FindByName("notexist", new UserContext()));
    }

    [Fact]
    public void InvisibleTool_Not_Returned()
    {
        var hiddenTool = new ToolDescriptor(
            "hidden", "Hidden", """{"type":"object"}""",
            [], false, false,
            ctx => ctx.IsAdmin,
            (_, _) => Task.FromResult<object?>(null));

        var registry = new ToolRegistry([hiddenTool]);
        var visible = registry.GetVisibleTools(new UserContext(IsAdmin: false));
        Assert.Empty(visible);
    }

    [Fact]
    public async Task InvokeAsync_Calls_Handler()
    {
        var called = false;
        var tool = new ToolDescriptor("my_tool", "Test", """{"type":"object"}""",
            [], false, false, _ => true,
            (args, ct) => { called = true; return Task.FromResult<object?>("done"); });

        var registry = new ToolRegistry([tool]);
        var result = await registry.InvokeAsync("my_tool", default, new UserContext(), CancellationToken.None);
        Assert.True(called);
        Assert.Equal("done", result);
    }

    [Fact]
    public async Task InvokeAsync_Unknown_Throws_ToolNotFoundException()
    {
        var registry = new ToolRegistry([]);
        await Assert.ThrowsAsync<ToolNotFoundException>(() =>
            registry.InvokeAsync("missing", default, new UserContext(), CancellationToken.None));
    }

    [Fact]
    public async Task InvokeAsync_Invisible_Throws_ToolAccessDeniedException()
    {
        var tool = new ToolDescriptor("secret", "Hidden", """{"type":"object"}""",
            [], false, false, ctx => ctx.IsAdmin,
            (_, _) => Task.FromResult<object?>(null));

        var registry = new ToolRegistry([tool]);
        await Assert.ThrowsAsync<ToolAccessDeniedException>(() =>
            registry.InvokeAsync("secret", default, new UserContext(IsAdmin: false), CancellationToken.None));
    }
}

public class WeightedToolSearcherTests
{
    private static ToolDescriptor MakeTool(string name, string description, IReadOnlyList<string>? tags = null)
        => new(name, description, """{"type":"object"}""",
            tags ?? [], false, false, _ => true,
            (_, _) => Task.FromResult<object?>(null));

    [Fact]
    public void Search_ExactNameMatch_HighScore()
    {
        var registry = new ToolRegistry([
            MakeTool("menu_list_items", "List all menu items"),
            MakeTool("customer_lookup", "Look up a customer"),
        ]);
        var searcher = new WeightedToolSearcher(registry);
        var results = searcher.Search("menu_list_items", 5, new UserContext());
        Assert.NotEmpty(results);
        Assert.Equal("menu_list_items", results[0].Name);
    }

    [Fact]
    public void Search_Query_Finds_Relevant_Tool()
    {
        var registry = new ToolRegistry([
            MakeTool("menu_list_items", "List all menu items from coffeeshop"),
            MakeTool("customer_lookup", "Look up a customer by name"),
        ]);
        var searcher = new WeightedToolSearcher(registry);
        var results = searcher.Search("menu", 5, new UserContext());
        Assert.NotEmpty(results);
        Assert.Equal("menu_list_items", results[0].Name);
    }

    [Fact]
    public void Search_Limit_Respected()
    {
        var tools = Enumerable.Range(1, 10)
            .Select(i => MakeTool($"tool_{i}", $"Coffee tool number {i}"))
            .ToList();
        var registry = new ToolRegistry(tools);
        var searcher = new WeightedToolSearcher(registry);
        var results = searcher.Search("coffee", 3, new UserContext());
        Assert.True(results.Count <= 3);
    }

    [Fact]
    public void Search_ZeroLimit_Returns_Empty()
    {
        var registry = new ToolRegistry([MakeTool("my_tool", "Test tool")]);
        var searcher = new WeightedToolSearcher(registry);
        var results = searcher.Search("test", 0, new UserContext());
        Assert.Empty(results);
    }

    [Fact]
    public void Search_NoMatch_Returns_Empty()
    {
        var registry = new ToolRegistry([MakeTool("my_tool", "Coffee thing")]);
        var searcher = new WeightedToolSearcher(registry);
        var results = searcher.Search("xyzxyz", 5, new UserContext());
        Assert.Empty(results);
    }

    [Fact]
    public void Search_TagMatch_Boosts_Score()
    {
        var registry = new ToolRegistry([
            MakeTool("order_submit", "Submit an order", ["coffeeshop", "order"]),
            MakeTool("random_tool", "Unrelated tool", []),
        ]);
        var searcher = new WeightedToolSearcher(registry);
        var results = searcher.Search("order", 5, new UserContext());
        Assert.NotEmpty(results);
        Assert.Equal("order_submit", results[0].Name);
    }
}
