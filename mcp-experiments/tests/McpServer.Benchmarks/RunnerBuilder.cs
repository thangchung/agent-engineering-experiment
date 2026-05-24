using McpServer.CodeMode;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace McpServer.Benchmarks;

public static class RunnerBuilder
{
    public static ISandboxRunner Build(string runnerName, IConfiguration config, ILoggerFactory loggerFactory)
    {
        var normalizedName = (runnerName ?? "auto").ToLowerInvariant();
        var configDict = new Dictionary<string, string?> { { "CodeMode:Runner", normalizedName } };
        var mergedConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        return SandboxRunnerFactory.Create(mergedConfig, loggerFactory, Array.Empty<string>());
    }
}
