using McpServer.CodeMode;

namespace McpServer.UnitTests.CodeMode;

public sealed class SandboxCodeGuardTests
{
    [Fact]
    public void ContainsForbiddenMetaToolUsage_ReturnsTrue_ForDirectMetaToolCall()
    {
        string code = """
            result = Search("breweries")
            """;

        Assert.True(SandboxCodeGuard.ContainsForbiddenMetaToolUsage(code));
    }

    [Fact]
    public void ContainsForbiddenMetaToolUsage_ReturnsFalse_ForOrdinaryVariableNames()
    {
        string code = """
            search_results = [1, 2, 3]
            result = len(search_results)
            """;

        Assert.False(SandboxCodeGuard.ContainsForbiddenMetaToolUsage(code));
    }

    [Fact]
    public void ContainsForbiddenMetaToolUsage_ReturnsFalse_ForStringContentOnly()
    {
        string code = """
            message = "Search(results) is shown in docs"
            result = message
            """;

        Assert.False(SandboxCodeGuard.ContainsForbiddenMetaToolUsage(code));
    }

    [Fact]
    public void ContainsForbiddenMetaToolUsage_ReturnsTrue_WhenWhitespaceAppearsBeforeCallParen()
    {
        string code = """
            result = GetSchema   ("tool")
            """;

        Assert.True(SandboxCodeGuard.ContainsForbiddenMetaToolUsage(code));
    }
}