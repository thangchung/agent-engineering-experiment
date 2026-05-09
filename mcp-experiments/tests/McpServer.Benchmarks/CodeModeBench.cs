using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using McpServer.Benchmarks.StubServer;
using McpServer.CodeMode;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace McpServer.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
[Config(typeof(BenchmarkConfig))]
public class CodeModeBench
{
    private ISandboxRunner runner = null!;
    private WebApplication? stubServer;
    private string baseUrl = string.Empty;

    [Params("simple", "complex")]
    public string Workload { get; set; } = "simple";

    [ParamsSource(nameof(StyleNames))]
    public string Style { get; set; } = "a_requests";

    [ParamsSource(nameof(RunnerNames))]
    public string Runner { get; set; } = "hyperlight";

    public static IEnumerable<string> RunnerNames
    {
        get
        {
            var forcedRunner = Environment.GetEnvironmentVariable("MCP_BENCH_RUNNER");
            if (!string.IsNullOrWhiteSpace(forcedRunner))
            {
                return new[] { forcedRunner.Trim().ToLowerInvariant() };
            }

            return new[] { "local", "hyperlight" };
        }
    }

    public static IEnumerable<string> StyleNames
    {
        get
        {
            var forcedRunner = Environment.GetEnvironmentVariable("MCP_BENCH_RUNNER");
            if (string.Equals(forcedRunner, "local", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(forcedRunner, "opensandbox", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "a_requests" };
            }

            return new[] { "a_requests", "b_http_get" };
        }
    }

    [GlobalSetup]
    public async Task GlobalSetupAsync()
    {
        if (Style == "b_http_get" && Runner != "hyperlight")
        {
            throw new InvalidOperationException($"Style b_http_get not supported on {Runner}");
        }

        if (string.Equals(Runner, "opensandbox", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("BENCH_OPENSANDBOX"), "1"))
                throw new InvalidOperationException("opensandbox bench requires BENCH_OPENSANDBOX=1 env");
        }

        var loggerFactory = LoggerFactory.Create(lb => lb.SetMinimumLevel(LogLevel.Critical));
        
        // Find appsettings.json - try current directory first, then assembly directory
        var configBasePath = Directory.GetCurrentDirectory();
        var configFile = Path.Combine(configBasePath, "appsettings.json");
        if (!File.Exists(configFile))
        {
            var assemblyDir = Path.GetDirectoryName(typeof(CodeModeBench).Assembly.Location);
            configFile = Path.Combine(assemblyDir!, "appsettings.json");
        }
        if (!File.Exists(configFile))
        {
            configBasePath = AppContext.BaseDirectory;
        }
        
        var config = new ConfigurationBuilder()
            .SetBasePath(configBasePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
        runner = RunnerBuilder.Build(Runner, config, loggerFactory);

        stubServer = StubHttpServerFactory.BuildStubServer(5555);
        _ = stubServer.RunAsync();
        await Task.Delay(500);

        baseUrl = "http://127.0.0.1:5555";
    }

    [GlobalCleanup]
    public async Task GlobalCleanupAsync()
    {
        if (runner is IAsyncDisposable ad)
            await ad.DisposeAsync();
        else if (runner is IDisposable d)
            d.Dispose();

        if (stubServer is not null)
            await stubServer.DisposeAsync();
    }

    [Benchmark]
    public async Task<string> Execute()
    {
        var code = WorkloadCatalog.Get(Workload, Style, runtimeSupportsHttpGet: Runner == "hyperlight");
        code = code.Replace("BASE_URL", $"\"{baseUrl}\"");

        var result = await runner.RunAsync(code, CancellationToken.None);
        return result.FinalValue?.ToString() ?? string.Empty;
    }
}

[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class CodeModeColdBench
{
    private string baseUrl = string.Empty;
    private WebApplication? stubServer;
    private Stopwatch setupWatch = null!;

    [Params("simple")]
    public string Workload { get; set; } = "simple";

    [Params("a_requests")]
    public string Style { get; set; } = "a_requests";

    [ParamsSource(nameof(RunnerNames))]
    public string Runner { get; set; } = "hyperlight";

    public static IEnumerable<string> RunnerNames
    {
        get
        {
            var forcedRunner = Environment.GetEnvironmentVariable("MCP_BENCH_RUNNER");
            if (!string.IsNullOrWhiteSpace(forcedRunner))
            {
                return new[] { forcedRunner.Trim().ToLowerInvariant() };
            }

            return new[] { "hyperlight" };
        }
    }

    [GlobalSetup]
    public async Task GlobalSetupAsync()
    {
        stubServer = StubHttpServerFactory.BuildStubServer(5556);
        _ = stubServer.RunAsync();
        await Task.Delay(500);
        baseUrl = "http://127.0.0.1:5556";
    }

    [GlobalCleanup]
    public async Task GlobalCleanupAsync()
    {
        if (stubServer is not null)
            await stubServer.DisposeAsync();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        setupWatch = Stopwatch.StartNew();
    }

    [Benchmark]
    public async Task<string> Execute()
    {
        var loggerFactory = LoggerFactory.Create(lb => lb.SetMinimumLevel(LogLevel.Critical));
        
        // Find appsettings.json - try current directory first, then assembly directory
        var configBasePath = Directory.GetCurrentDirectory();
        var configFile = Path.Combine(configBasePath, "appsettings.json");
        if (!File.Exists(configFile))
        {
            var assemblyDir = Path.GetDirectoryName(typeof(CodeModeBench).Assembly.Location);
            configFile = Path.Combine(assemblyDir!, "appsettings.json");
        }
        if (!File.Exists(configFile))
        {
            configBasePath = AppContext.BaseDirectory;
        }
        
        var config = new ConfigurationBuilder()
            .SetBasePath(configBasePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
        var runner = RunnerBuilder.Build(Runner, config, loggerFactory);

        var code = WorkloadCatalog.Get(Workload, Style, runtimeSupportsHttpGet: Runner == "hyperlight");
        code = code.Replace("BASE_URL", $"\"{baseUrl}\"");

        var result = await runner.RunAsync(code, CancellationToken.None);
        
        if (runner is IAsyncDisposable ad)
            await ad.DisposeAsync();
        else if (runner is IDisposable d)
            d.Dispose();

        setupWatch.Stop();
        return $"cold_ms:{setupWatch.ElapsedMilliseconds}|result:{result.FinalValue?.ToString() ?? "err"}";
    }
}

public class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        AddDiagnoser(new MemoryDiagnoser(new MemoryDiagnoserConfig(true)));
        AddExporter(MarkdownExporter.Default);
        AddExporter(JsonExporter.Default);
    }
}
