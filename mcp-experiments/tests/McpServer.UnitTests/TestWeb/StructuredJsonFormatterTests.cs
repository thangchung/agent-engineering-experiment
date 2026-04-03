using System.Text.Json;
using TestWeb.Components.Pages;

namespace McpServer.UnitTests.TestWeb;

public sealed class StructuredJsonFormatterTests
{
    [Fact]
    public void TryCreateTable_ReturnsTable_ForUniformObjectArray()
    {
        using JsonDocument document = JsonDocument.Parse("""
        [
          { "brewery_type": "micro", "count": 45 },
          { "brewery_type": "brewpub", "count": 23 }
        ]
        """);

        bool success = StructuredJsonFormatter.TryCreateTable(document.RootElement, out StructuredJsonTable table);

        Assert.True(success);
        Assert.Equal(["brewery_type", "count"], table.Columns);
        Assert.Equal("micro", table.Rows[0][0]);
        Assert.Equal("23", table.Rows[1][1]);
    }

    [Fact]
    public void TryCreateTable_ReturnsFalse_ForNestedObjectArray()
    {
        using JsonDocument document = JsonDocument.Parse("""
        [
          { "name": "sample", "meta": { "city": "San Diego" } }
        ]
        """);

        bool success = StructuredJsonFormatter.TryCreateTable(document.RootElement, out StructuredJsonTable table);

        Assert.False(success);
        Assert.Equal(default, table);
    }

    [Fact]
    public void FormatLabel_ExpandsSnakeCaseAndCamelCase()
    {
        Assert.Equal("San Diego Total", StructuredJsonFormatter.FormatLabel("san_diego_total"));
        Assert.Equal("Moon Top 5", StructuredJsonFormatter.FormatLabel("moonTop5"));
    }
}