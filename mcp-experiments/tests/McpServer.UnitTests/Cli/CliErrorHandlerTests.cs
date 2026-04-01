using McpServer.Cli;
using McpServer.Registry;

namespace McpServer.UnitTests.Cli;

public sealed class CliErrorHandlerTests
{
    [Fact]
    public void MapExceptionToExitCode_ReturnsExpectedCodes()
    {
        Assert.Equal(2, CliErrorHandler.MapExceptionToExitCode(new ToolNotFoundException("x")));
        Assert.Equal(4, CliErrorHandler.MapExceptionToExitCode(new ToolAccessDeniedException("x")));
        Assert.Equal(3, CliErrorHandler.MapExceptionToExitCode(new SyntheticToolRecursionException("x")));
        Assert.Equal(1, CliErrorHandler.MapExceptionToExitCode(new InvalidOperationException("x")));
    }
}
