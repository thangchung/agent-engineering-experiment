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
    public void ResolveSources_ReadsMultipleConfiguredSources()
    {
        string root = GetRepositoryRoot();
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenApi:Sources:0:Name"] = "brewery",
                ["OpenApi:Sources:0:Path"] = Path.Combine(root, "contracts", "openbrewerydb.v1.openapi.yaml"),
                ["OpenApi:Sources:1:Name"] = "petstore",
                ["OpenApi:Sources:1:Url"] = "https://petstore3.swagger.io/api/v3/openapi.json",
            })
            .Build();

        IReadOnlyList<OpenApiToolCatalogBuilder.OpenApiSourceDefinition> sources =
            OpenApiToolCatalogBuilder.ResolveSources(config, Path.Combine(root, "src", "McpServer", "bin", "Debug", "net10.0"));

        Assert.Collection(
            sources,
            source =>
            {
                Assert.Equal("brewery", source.Name);
                Assert.EndsWith("contracts/openbrewerydb.v1.openapi.yaml", source.Location, StringComparison.OrdinalIgnoreCase);
            },
            source =>
            {
                Assert.Equal("petstore", source.Name);
                Assert.Equal("https://petstore3.swagger.io/api/v3/openapi.json", source.Location);
            });
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

        [Fact]
        public async Task BuildToolsAsync_MergesMultipleSpecs()
        {
                string tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDirectory);

                try
                {
                        string alphaSpecPath = Path.Combine(tempDirectory, "alpha.openapi.json");
                        string betaSpecPath = Path.Combine(tempDirectory, "beta.openapi.json");

                        await File.WriteAllTextAsync(alphaSpecPath, """
                                {
                                    "openapi": "3.0.3",
                                    "info": { "title": "Alpha API", "version": "v1" },
                                    "servers": [{ "url": "https://alpha.example.com" }],
                                    "paths": {
                                        "/alpha": {
                                            "get": {
                                                "operationId": "getAlpha",
                                                "responses": {
                                                    "200": { "description": "ok" }
                                                }
                                            }
                                        }
                                    }
                                }
                                """);

                        await File.WriteAllTextAsync(betaSpecPath, """
                                {
                                    "openapi": "3.0.3",
                                    "info": { "title": "Beta API", "version": "v1" },
                                    "servers": [{ "url": "https://beta.example.com" }],
                                    "paths": {
                                        "/beta": {
                                            "get": {
                                                "operationId": "getBeta",
                                                "responses": {
                                                    "200": { "description": "ok" }
                                                }
                                            }
                                        }
                                    }
                                }
                                """);

                        IReadOnlyList<McpServer.Registry.ToolDescriptor> tools = await OpenApiToolCatalogBuilder.BuildToolsAsync(
                        [
                                new OpenApiToolCatalogBuilder.OpenApiSourceDefinition("alpha", alphaSpecPath),
                                new OpenApiToolCatalogBuilder.OpenApiSourceDefinition("beta", betaSpecPath),
                        ]);

                        string[] names = tools.Select(tool => tool.Name).ToArray();

                        Assert.Contains("get_alpha", names, StringComparer.OrdinalIgnoreCase);
                        Assert.Contains("get_beta", names, StringComparer.OrdinalIgnoreCase);
                }
                finally
                {
                        if (Directory.Exists(tempDirectory))
                        {
                                Directory.Delete(tempDirectory, recursive: true);
                        }
                }
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
