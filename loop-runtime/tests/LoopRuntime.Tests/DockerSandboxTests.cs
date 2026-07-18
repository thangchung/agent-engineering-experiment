using LoopRuntime.Contracts;
using LoopRuntime.Sandbox;

namespace LoopRuntime.Tests;

/// <summary>
/// DockerSandbox integration tests.
/// Skipped automatically when Docker daemon is not reachable.
/// Mark individual tests [Trait("Category","docker")] so CI can exclude them.
/// </summary>
public sealed class DockerSandboxTests
{
    private static bool DockerAvailable()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            p.WaitForExit(5_000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    [SkippableFact]
    [Trait("Category", "docker")]
    public async Task ValidPython_ExitsZero_StdoutReturned()
    {
        Skip.If(!DockerAvailable(), "Docker daemon not available.");

        var sandbox = new DockerSandbox();
        var result = await sandbox.RunAsync("print('hello')", CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.Stdout, StringComparison.Ordinal);
        Assert.False(result.TimedOut);
    }

    [SkippableFact]
    [Trait("Category", "docker")]
    public async Task InfiniteLoop_TimesOut()
    {
        Skip.If(!DockerAvailable(), "Docker daemon not available.");

        var sandbox = new DockerSandbox(timeout: TimeSpan.FromSeconds(3));
        var result = await sandbox.RunAsync("while True: pass", CancellationToken.None);

        Assert.True(result.TimedOut, "Expected sandbox to kill the infinite loop.");
    }

    [SkippableFact]
    [Trait("Category", "docker")]
    public async Task NetworkCall_FailsWithNoNet()
    {
        Skip.If(!DockerAvailable(), "Docker daemon not available.");

        var sandbox = new DockerSandbox();
        const string code = """
            import urllib.request
            urllib.request.urlopen("http://example.com", timeout=2)
            """;
        var result = await sandbox.RunAsync(code, CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(result.TimedOut);
    }
}
