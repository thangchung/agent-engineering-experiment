using System.Diagnostics;

namespace McpServer.CodeMode.Validation;

public sealed class AstParseSyntaxValidator : IPythonSyntaxValidator
{
    private readonly PreflightOptions options;
    private readonly ILogger<AstParseSyntaxValidator> logger;
    private static readonly ActivitySource ActivitySource = new("McpServer.CodeMode.AstParseSyntaxValidator");

    public AstParseSyntaxValidator(PreflightOptions options, ILogger<AstParseSyntaxValidator> logger)
    {
        this.options = options;
        this.logger = logger;
    }

    public async Task EnsureValidAsync(string code, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        using Activity? activity = ActivitySource.StartActivity("codemode.syntax.validate", ActivityKind.Internal);
        activity?.SetTag("mcp.syntax.validator", "ast.parse");
        activity?.SetTag("mcp.syntax.length", code.Length);

        string stderr = await RunParserAsync(code, ct);
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return;
        }

        logger.LogWarning("Python syntax preflight failed: {Error}", stderr.Trim());
        throw new SyntaxValidationException(
            "Python syntax preflight failed. Fix the syntax error and try execute again. Parser output: " + stderr.Trim());
    }

    private async Task<string> RunParserAsync(string code, CancellationToken ct)
    {
        using Process process = CreateProcess();

        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start Python syntax preflight using '{options.PythonPath}'.");
        }

        await process.StandardInput.WriteAsync(code.AsMemory(), ct);
        await process.StandardInput.FlushAsync();
        process.StandardInput.Close();

        TimeSpan timeout = TimeSpan.FromMilliseconds(Math.Max(250, options.TimeoutMs));
        using CancellationTokenSource timeoutCts = new(timeout);
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new SyntaxValidationException(
                $"Python syntax preflight timed out after {timeout.TotalMilliseconds:0}ms.",
                ex);
        }

        string stderr = await process.StandardError.ReadToEndAsync(ct);
        if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(stderr))
        {
            stderr = $"Python exited with code {process.ExitCode}.";
        }

        Activity.Current?.SetTag("mcp.syntax.exitCode", process.ExitCode);
        Activity.Current?.SetTag("mcp.syntax.ok", process.ExitCode == 0);

        return stderr;
    }

    private Process CreateProcess()
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = options.PythonPath,
                Arguments = "-c \"import ast,sys; ast.parse(sys.stdin.read())\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
    }
}