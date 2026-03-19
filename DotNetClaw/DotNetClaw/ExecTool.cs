using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace DotNetClaw;

/// <summary>
/// Executes shell commands with safety constraints.
/// Blocks dangerous operations that could harm the system.
/// </summary>
public sealed class ExecTool
{
    private readonly ILogger<ExecTool> logger;
    private readonly string? coffeeshopCliExecutablePath;

    public ExecTool(ILogger<ExecTool> logger, IConfiguration? configuration = null)
    {
        this.logger = logger;
        
        if (configuration != null)
        {
            coffeeshopCliExecutablePath = configuration["CoffeeshopCli:ExecutablePath"];
            if (!string.IsNullOrWhiteSpace(coffeeshopCliExecutablePath))
            {
                coffeeshopCliExecutablePath = Path.GetFullPath(coffeeshopCliExecutablePath);
            }
        }
    }

    private static readonly HashSet<string> BlockedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "rm", "del", "format", "dd", "mkfs", "fdisk", "parted",
        "shutdown", "reboot", "halt", "poweroff", "init",
        "chmod", "chown", "chgrp", // Allow these specifically if needed, but block by default
        "sudo", "su", "doas",      // No privilege escalation
        "curl", "wget", "nc", "netcat" // No network access (can be relaxed if needed)
    };

    [Description("Execute a shell command and return the output. Dangerous commands (rm, sudo, etc.) are blocked for safety. " +
                 "Use this to invoke coffeeshop-cli commands, run dotnet commands, or execute other safe shell operations.")]
    public async Task<string> RunAsync(
        [Description("The shell command to execute (e.g., 'dotnet run -- skills list --json')")] 
        string command,
        [Description("Optional working directory (defaults to current directory)")] 
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        // Validate working directory — LLMs sometimes hallucinate paths from training data
        if (!string.IsNullOrWhiteSpace(workingDirectory) && !Directory.Exists(workingDirectory))
        {
            logger.LogWarning("[ExecTool] Working directory does not exist, ignoring: {Cwd}", workingDirectory);
            workingDirectory = null;
        }

        logger.LogInformation("[ExecTool] RunAsync called: {Command} (cwd: {Cwd})", 
            command, workingDirectory ?? Directory.GetCurrentDirectory());

        var originalCommand = command;
        command = RewriteCoffeeshopCliCommand(command);
        
        if (command != originalCommand)
        {
            logger.LogDebug("[ExecTool] Command rewritten: {Original} → {Rewritten}", 
                originalCommand, command);
        }

        var firstToken = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstToken != null)
        {
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
            // Cross-platform shell selection and command escaping
            var isWindows = OperatingSystem.IsWindows();
            
            // Windows: cmd.exe /c command (quotes in command are passed through)
            // Unix: zsh -c "command" (quotes in command are escaped with backslash)
            var (fileName, arguments) = isWindows
                ? ("cmd.exe", $"/c {command}")
                : ("/bin/zsh", $"-c \"{command.Replace("\"", "\\\"")}\"");

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
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

    /// <summary>
    /// Rewrites commands starting with 'coffeeshop-cli' or 'Coffeeshop-Cli'
    /// to use the configured executable path.
    /// </summary>
    private string RewriteCoffeeshopCliCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(coffeeshopCliExecutablePath))
        {
            return command;
        }

        var trimmed = command.TrimStart();
        
        // Check if command starts with coffeeshop-cli (case-insensitive)
        if (trimmed.StartsWith("coffeeshop-cli ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("coffeeshop-cli", StringComparison.OrdinalIgnoreCase))
        {
            // Extract the rest of the command after 'coffeeshop-cli'
            var restOfCommand = trimmed.Length > 15 
                ? trimmed.Substring(15) 
                : "";
            
            // Reconstruct with full path (space required between quoted path and args)
            return string.IsNullOrEmpty(restOfCommand)
                ? $"\"{coffeeshopCliExecutablePath}\""
                : $"\"{coffeeshopCliExecutablePath}\" {restOfCommand}";
        }

        return command;
    }
}
