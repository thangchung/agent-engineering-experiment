using System.Diagnostics;
using System.Text;
using LoopRuntime.Contracts;
using Microsoft.Extensions.Logging;

namespace LoopRuntime.Sandbox;

public sealed class DockerSandbox : ICodeSandbox
{
    private readonly string _image;
    private readonly TimeSpan _timeout;
    private readonly ILogger<DockerSandbox>? _logger;

    public DockerSandbox(string image = "python:3-slim", TimeSpan? timeout = null, ILogger<DockerSandbox>? logger = null)
    {
        _image = image;
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        _logger = logger;
    }

    public async Task<RunResult> RunAsync(string code, CancellationToken cancellationToken)
    {
        var scratchRoot = Path.Combine(Path.GetTempPath(), "loop-runtime-sandbox", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchRoot);
        var scriptPath = Path.Combine(scratchRoot, "main.py");

        _logger?.LogInformation("Docker sandbox run starting. Image={Image} Timeout={TimeoutSeconds}s", _image, _timeout.TotalSeconds);
        await File.WriteAllTextAsync(scriptPath, code, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add("--rm");
            psi.ArgumentList.Add("--network=none");
            psi.ArgumentList.Add("--memory=256m");
            psi.ArgumentList.Add("--cpus=1");
            psi.ArgumentList.Add("--pids-limit=64");
            psi.ArgumentList.Add("--read-only");
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add("65534:65534");
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add($"{scratchRoot}:/work:rw");
            psi.ArgumentList.Add("-w");
            psi.ArgumentList.Add("/work");
            psi.ArgumentList.Add(_image);
            psi.ArgumentList.Add("python");
            psi.ArgumentList.Add("/work/main.py");

            using var process = new Process { StartInfo = psi };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_timeout);

            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
                TryKill(process);
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            _logger?.LogInformation("Docker sandbox run completed. ExitCode={ExitCode} TimedOut={TimedOut}", timedOut ? -1 : process.ExitCode, timedOut);
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                _logger?.LogDebug("Docker sandbox stderr: {Stderr}", stderr);
            }

            return new RunResult(
                ExitCode: timedOut ? -1 : process.ExitCode,
                Stdout: stdout,
                Stderr: stderr,
                TimedOut: timedOut);
        }
        finally
        {
            try
            {
                Directory.Delete(scratchRoot, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best effort
        }
    }
}
