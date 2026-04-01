using System.Text.Json;
using Spectre.Console;

namespace McpServer.Cli;

/// <summary>
/// Shared rendering helpers for CLI output in human and JSON forms.
/// </summary>
internal static class JsonFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Formats a successful invocation result for output.
    /// </summary>
    public static string FormatResult(ToolInvocationResult result, bool asJson)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (asJson)
        {
            return JsonSerializer.Serialize(new
            {
                ok = result.IsSuccess,
                tool = result.ToolName,
                output = result.Output,
            }, JsonOptions);
        }

        return result.Output switch
        {
            null => "(null)",
            string text => text,
            _ => JsonSerializer.Serialize(result.Output, JsonOptions),
        };
    }

    /// <summary>
    /// Formats an exception in human or JSON form.
    /// </summary>
    public static string FormatError(Exception exception, bool asJson)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (asJson)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = exception.Message,
                type = exception.GetType().Name,
            }, JsonOptions);
        }

        return $"Error: {Markup.Escape(CliErrorHandler.FormatErrorMessage(exception))}";
    }
}
