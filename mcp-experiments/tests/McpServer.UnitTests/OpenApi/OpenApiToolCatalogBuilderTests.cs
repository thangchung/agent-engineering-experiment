using McpServer.OpenApi;
using Microsoft.Extensions.Configuration;

namespace McpServer.UnitTests.OpenApi;

public sealed class OpenApiToolCatalogBuilderTests
{
    [Fact]
    public void ResolveSpecPath_FindsSingleContractInRepository()
    {
        string root = GetRepositoryRoot();
        IConfiguration config = new ConfigurationBuilder().Build();

        string resolved = OpenApiToolCatalogBuilder.ResolveSpecPath(config, Path.Combine(root, "src", "McpServer", "bin", "Debug", "net10.0"));

        Assert.EndsWith("contracts/openbrewerydb.v1.openapi.yaml", resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildTools_KeepsOpenBreweryCompatibilityAliases()
    {
        string root = GetRepositoryRoot();
        string specPath = Path.Combine(root, "contracts", "openbrewerydb.v1.openapi.yaml");

        IReadOnlyList<McpServer.Registry.ToolDescriptor> tools = OpenApiToolCatalogBuilder.BuildTools(specPath);
        string[] names = tools.Select(tool => tool.Name).ToArray();

        Assert.Contains("brewery_get", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("brewery_list", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("brewery_random", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("brewery_search", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("brewery_meta", names, StringComparer.OrdinalIgnoreCase);
    }

    private static string GetRepositoryRoot()
    {
        string current = AppContext.BaseDirectory;
        DirectoryInfo? dir = new(current);

        while (dir is not null)
        {
            string potential = Path.Combine(dir.FullName, "McpExperiments.slnx");

            if (File.Exists(potential))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
