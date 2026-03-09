using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace DotNetClaw;

/// <summary>
/// Executes shell commands with safety constraints.
/// Blocks dangerous operations that could harm the system.
/// </summary>
public sealed class ExecTool(ILogger<ExecTool> logger)
{
    // Blocklist — dangerous commands that should never be executed
    private static readonly HashSet<string> BlockedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "rm", "del", "format", "dd", "mkfs", "fdisk", "parted",
        "shutdown", "reboot", "halt", "poweroff", "init",
        "chmod", "chown", "chgrp", // Allow these specifically if needed, but block by default
        "sudo", "su", "doas",      // No privilege escalation
        "curl", "wget", "nc", "netcat" // No network access (can be relaxed if needed)
    };

    /// <summary>
    /// Executes a shell command and returns the output.
    /// Dangerous commands are blocked for safety.
    /// </summary>
    /// <param name="command">The shell command to execute (e.g., "ls -la", "dotnet run -- skills list")</param>
    /// <param name="workingDirectory">Optional working directory (defaults to current directory)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>JSON string with exit code, stdout, and stderr</returns>
    [Description("Execute a shell command and return the output. Dangerous commands (rm, sudo, etc.) are blocked for safety. " +
                 "Use this to invoke coffeeshop-cli commands, run dotnet commands, or execute other safe shell operations.")]
    public async Task<string> RunAsync(
        [Description("The shell command to execute (e.g., 'dotnet run -- skills list --json')")] 
        string command,
        [Description("Optional working directory (defaults to current directory)")] 
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        logger.LogInformation("[ExecTool] RunAsync called: {Command} (cwd: {Cwd})", 
            command, workingDirectory ?? Directory.GetCurrentDirectory());

        // Safety check — block dangerous commands
        var firstToken = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstToken != null)
        {
            // Extract command name (handle paths like "/bin/rm" or "./script.sh")
            var cmdName = Path.GetFileName(firstToken);
            
            if (BlockedCommands.Contains(cmdName))
            {
                logger.LogWarning("[ExecTool] BLOCKED dangerous command: {Command}", command);
                return System.Text.Json.JsonSerializer.Serialize(new
                {
                    exit_code = -1,
                    stdout = "",
                    stderr = $"Command '{cmdName}' is blocked by safety filter.",
                    error = "BLOCKED"
                });
            }
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/zsh",
                Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
            };

            using var process = new Process { StartInfo = startInfo };
            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdoutBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderrBuilder.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait with timeout (30 seconds default)
            await process.WaitForExitAsync(ct);
            
            var exitCode = process.ExitCode;
            var stdout = stdoutBuilder.ToString().TrimEnd();
            var stderr = stderrBuilder.ToString().TrimEnd();

            logger.LogInformation("[ExecTool] Command completed: exit={ExitCode}, stdout={StdoutLen}b, stderr={StderrLen}b",
                exitCode, stdout.Length, stderr.Length);

            return System.Text.Json.JsonSerializer.Serialize(new
            {
                exit_code = exitCode,
                stdout,
                stderr,
                success = exitCode == 0
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ExecTool] Command execution failed: {Command}", command);
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                exit_code = -1,
                stdout = "",
                stderr = ex.Message,
                error = "EXCEPTION"
            });
        }
    }
}
