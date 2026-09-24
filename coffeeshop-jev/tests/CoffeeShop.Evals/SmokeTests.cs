namespace CoffeeShop.Evals;

public class SmokeTests
{
    [Fact]
    public void Solution_Scaffold_Builds()
    {
        Assert.True(true);
    }

    [Fact]
    public void Require_ThrowsWithTheGivenMessage_WhenValueMissing()
    {
        // 2026-09-24: fail-fast, not skip - a human running the live evals with nothing
        // configured gets a clear reason and a fix, not a silent "Skipped" they might miss.
        // RequireOpenJev/RequireOpenAi build on this; tested here directly (not through them)
        // since those two depend on whatever happens to be configured on this machine.
        var ex = Assert.Throws<InvalidOperationException>(() => EvalConfig.Require(null, "set it up"));
        Assert.Equal("set it up", ex.Message);
        EvalConfig.Require("configured", "unreachable"); // does not throw
    }
}
