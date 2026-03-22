using McpServer.Registry;

namespace McpServer.Search;

/// <summary>
/// Defines tool search and ranking behavior.
/// </summary>
public interface IToolSearcher
{
    /// <summary>
    /// Searches visible tools using deterministic ranking.
    /// </summary>
    IReadOnlyList<ToolDescriptor> Search(string query, int limit, UserContext context);
}
