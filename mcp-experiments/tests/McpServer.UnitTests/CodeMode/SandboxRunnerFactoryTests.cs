using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using McpServer.CodeMode;
using McpServer.CodeMode.Hyperlight;
using McpServer.CodeMode.Local;
using McpServer.CodeMode.OpenSandbox;
using System.Reflection;

namespace McpServer.UnitTests.CodeMode;

public sealed class SandboxRunnerFactoryTests
{
    [Fact]
    public void Create_HyperlightRunnerSelectedByDefault()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        ISandboxRunner runner = SandboxRunnerFactory.Create(configuration, NullLoggerFactory.Instance);

        Assert.IsType<HyperlightSandboxRunner>(runner);
    }

    [Fact]
    public void Create_OpenSandboxRunnerRequiresDomain()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodeMode:Runner"] = "opensandbox",
            })
            .Build();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            SandboxRunnerFactory.Create(configuration, NullLoggerFactory.Instance));

        Assert.Contains("OpenSandbox:Domain", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenSandboxRunner_RunAsync_ExecutesRemoteCode()
    {
        bool remoteCalled = false;

        OpenSandboxRunner runner = new(
            new OpenSandboxRunnerOptions
            {
                Domain = "localhost:8080",
                Timeout = TimeSpan.FromSeconds(1),
                MaxToolCalls = 5,
            },
            NullLoggerFactory.Instance,
            remoteExecutor: (_, _) =>
            {
                remoteCalled = true;
                return Task.FromResult(new RunnerResult("remote-result", 0));
            });

        RunnerResult result = await runner.RunAsync(
            "result = 1",
            CancellationToken.None);

        Assert.True(remoteCalled);
        Assert.Equal("remote-result", result.FinalValue);
        Assert.Equal(0, result.CallsExecuted);
    }

    [Fact]
    public async Task OpenSandboxRunner_RunAsync_PassesCodeToExecutor()
    {
        bool remoteCalled = false;
        string? codeReceivedBySandbox = null;

        OpenSandboxRunner runner = new(
            new OpenSandboxRunnerOptions
            {
                Domain = "localhost:8080",
                Timeout = TimeSpan.FromSeconds(5),
                MaxToolCalls = 5,
            },
            NullLoggerFactory.Instance,
            remoteExecutor: (code, _) =>
            {
                remoteCalled = true;
                codeReceivedBySandbox = code;
                return Task.FromResult(new RunnerResult("remote-result", 0));
            });

        string testCode = "x = 1 + 1\nresult = str(x)";
        RunnerResult result = await runner.RunAsync(testCode, CancellationToken.None);

        Assert.True(remoteCalled);
        Assert.NotNull(codeReceivedBySandbox);
        Assert.Equal(testCode, codeReceivedBySandbox);
        Assert.Equal("remote-result", result.FinalValue);
    }

    [Fact]
    public void OpenSandboxRunner_BuildPythonCommand_UsesFlushLeftHeredocTerminator()
    {
        MethodInfo? buildPythonCommand = typeof(OpenSandboxRunner).GetMethod(
            "BuildPythonCommand",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(buildPythonCommand);

        string command = Assert.IsType<string>(buildPythonCommand.Invoke(null, ["ZHVtbXk=", "https://example.test/api"]));
        string normalizedCommand = command.Replace("\r\n", "\n", StringComparison.Ordinal);
        string[] lines = normalizedCommand.Split('\n');

        Assert.Equal("python3 - <<'PY'", lines[0]);
        Assert.EndsWith("\nPY\n", normalizedCommand, StringComparison.Ordinal);
    }
}
