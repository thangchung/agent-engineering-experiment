using ToolSearch.Gateway.Registry;

namespace ToolSearch.Gateway.Search;

public interface IToolSearcher
{
    IReadOnlyList<ToolDescriptor> Search(string query, int limit, UserContext context);
}
