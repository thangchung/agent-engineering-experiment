using McpServer.Cli;
using McpServer.Tools;

namespace McpServer.UnitTests.Cli;

public sealed class IntentInvocationPlannerTests
{
    [Fact]
    public void Plan_UsesExplicitToolAndArgs_WhenProvided()
    {
        PlannedInvocation invocation = IntentInvocationPlanner.Plan(
            "ignored",
            "status",
            "{\"verbose\":true}",
            []);

        Assert.Equal("status", invocation.ToolName);
        Assert.Equal("{\"verbose\":true}", invocation.ArgsJson);
    }

    [Fact]
        public void Plan_InferLocationFilter_FromCandidateSchema()
    {
        ToolDefinition[] candidates =
        [
                        new(
                                "lookup_entities",
                                "List entities",
                                """
                                {
                                    "type": "object",
                                    "properties": {
                                        "by_city": {
                                            "type": "string",
                                            "description": "Filter by city name."
                                        }
                                    }
                                }
                                """,
                                [],
                                false,
                                false),
                        new(
                                "search_entities",
                                "Search entities",
                                """
                                {
                                    "type": "object",
                                    "properties": {
                                        "query": {
                                            "type": "string",
                                            "description": "Search phrase."
                                        }
                                    }
                                }
                                """,
                                [],
                                false,
                                false),
        ];

        PlannedInvocation invocation = IntentInvocationPlanner.Plan(
            "find breweries in seattle",
            null,
            null,
            candidates);

                Assert.Equal("lookup_entities", invocation.ToolName);
        Assert.Equal("{\"by_city\":\"seattle\"}", invocation.ArgsJson);
    }

        [Fact]
        public void Plan_InferSearchQuery_FromCandidateSchema()
        {
                ToolDefinition[] candidates =
                [
                        new(
                                "lookup_entities",
                                "List entities",
                                "{}",
                                [],
                                false,
                                false),
                        new(
                                "search_entities",
                                "Search entities",
                                """
                                {
                                    "type": "object",
                                    "properties": {
                                        "query": {
                                            "type": "string",
                                            "description": "Search term to match by name."
                                        }
                                    }
                                }
                                """,
                                [],
                                false,
                                false),
                ];

                PlannedInvocation invocation = IntentInvocationPlanner.Plan(
                        "find entities named acme",
                        null,
                        null,
                        candidates);

                Assert.Equal("search_entities", invocation.ToolName);
                Assert.Equal("{\"query\":\"acme\"}", invocation.ArgsJson);
        }

    [Fact]
    public void Plan_FallsBackToFirstCandidate_WhenNoHeuristicMatches()
    {
        ToolDefinition[] candidates =
        [
            new("status", "Status", "{}", [], false, false),
            new("list_breweries", "List breweries", "{}", [], false, false),
        ];

        PlannedInvocation invocation = IntentInvocationPlanner.Plan(
            "health check",
            null,
            null,
            candidates);

        Assert.Equal("status", invocation.ToolName);
        Assert.Equal("{}", invocation.ArgsJson);
    }
}