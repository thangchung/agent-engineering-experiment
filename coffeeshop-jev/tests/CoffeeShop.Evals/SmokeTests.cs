namespace CoffeeShop.Evals;

public class SmokeTests
{
    [Fact]
    public void Solution_Scaffold_Builds()
    {
        Assert.True(true);
    }

    [LiveFact]
    public void Skipped_Without_OpenJevUrl()
    {
        // This test only runs when OPENJEV_URL is set (T01 AC3).
        Assert.True(true);
    }
}
