using McpServer.Cli;

namespace McpServer.UnitTests.Cli;

public sealed class JsonFormatterTests
{
    [Fact]
    public void FormatResult_AsJson_ContainsToolAndOk()
    {
        ToolInvocationResult result = new(true, "status", new { ok = true }, null);

        string formatted = JsonFormatter.FormatResult(result, asJson: true);

        Assert.Contains("\"ok\": true", formatted);
        Assert.Contains("\"tool\": \"status\"", formatted);
    }

    [Fact]
    public void FormatError_AsJson_ContainsErrorFields()
    {
        string formatted = JsonFormatter.FormatError(new InvalidOperationException("boom"), asJson: true);

        Assert.Contains("\"ok\": false", formatted);
        Assert.Contains("\"error\": \"boom\"", formatted);
    }
}
