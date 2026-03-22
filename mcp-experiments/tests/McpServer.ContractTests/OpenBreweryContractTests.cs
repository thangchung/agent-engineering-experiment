namespace McpServer.ContractTests;

public sealed class OpenBreweryContractTests
{
    [Fact]
    public void ContractFile_ParsesAndContainsRequiredPaths()
    {
        string root = GetRepositoryRoot();
        string contractPath = Path.Combine(root, "contracts", "openbrewerydb.v1.openapi.yaml");

        Assert.True(File.Exists(contractPath));

        string content = File.ReadAllText(contractPath);

        Assert.Contains("openapi:", content, StringComparison.Ordinal);
        // All five documented endpoints must be present.
        Assert.Contains("/breweries/{obdb-id}:", content, StringComparison.Ordinal);
        Assert.Contains("/breweries:", content, StringComparison.Ordinal);
        Assert.Contains("/breweries/random:", content, StringComparison.Ordinal);
        Assert.Contains("/breweries/search:", content, StringComparison.Ordinal);
        Assert.Contains("/breweries/meta:", content, StringComparison.Ordinal);
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
