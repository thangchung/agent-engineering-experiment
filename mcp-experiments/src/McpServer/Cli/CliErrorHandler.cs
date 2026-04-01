using System.Text.Json;
using McpServer.Registry;

namespace McpServer.Cli;

/// <summary>
/// Maps known exceptions to deterministic CLI exit codes.
/// </summary>
internal static class CliErrorHandler
{
    /// <summary>
    /// Gets the process exit code for a command failure.
    /// </summary>
    public static int MapExceptionToExitCode(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            ToolNotFoundException => 2,
            JsonException => 1,
            SyntheticToolRecursionException => 3,
            ToolAccessDeniedException => 4,
            _ => 1,
        };
    }

    /// <summary>
    /// Builds a human-readable error line.
    /// </summary>
    public static string FormatErrorMessage(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Message;
    }
}
