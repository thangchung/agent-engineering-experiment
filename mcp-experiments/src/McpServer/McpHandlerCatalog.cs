using System.Reflection;
using McpServer.CodeMode;
using McpServer.ToolSearch;
using ModelContextProtocol.Server;

namespace McpServer;

internal static class McpHandlerCatalog
{
    private static readonly Type[] HandlerTypes = [typeof(ToolSearchHandlers), typeof(CodeModeHandlers)];

    public static IReadOnlyList<MethodInfo> GetExposedToolMethods()
    {
        return HandlerTypes
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public static HashSet<string> GetExposedToolNames()
    {
        return GetExposedToolMethods()
            .Select(static method =>
            {
                McpServerToolAttribute? attribute = method.GetCustomAttribute<McpServerToolAttribute>();
                return string.IsNullOrWhiteSpace(attribute?.Name) ? method.Name : attribute.Name;
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}