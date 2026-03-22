using McpServer.Registry;
using System.Diagnostics;
using System.Text.Json;

namespace McpServer.CodeMode;

/// <summary>
/// Executes a validated plan through the configured sandbox runner.
/// </summary>
public sealed class ExecuteTool(IToolRegistry registry, ISandboxRunner runner)
{
    private static readonly ActivitySource ActivitySource = new("McpServer.CodeMode.ExecuteTool");
    /// <summary>
    /// Executes code and returns only the final value to reduce token usage.
    /// </summary>
    public async Task<ExecuteResponse> ExecuteAsync(string code, UserContext context, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(context);

        using Activity? activity = ActivitySource.StartActivity("codemode.execute", ActivityKind.Internal);
        activity?.SetTag("mcp.code.length", code.Length);

        RunnerResult result = await runner.RunAsync(
            code,
            (toolName, args, token) => toolName == "select_fields"
                ? Task.FromResult<object?>(SelectFields(args))
                : registry.InvokeAsync(toolName, args, context, token),
            ct);

        activity?.SetTag("mcp.execute.callCount", result.CallsExecuted);
        activity?.SetTag("mcp.execute.hasFinalValue", result.FinalValue is not null);

        return new ExecuteResponse(result.FinalValue);
    }

    /// <summary>
    /// Built-in virtual tool: projects a comma-separated set of fields from each object in an array.
    /// Arguments: data (array), fields (comma-separated string e.g. "name,city").
    /// </summary>
    private static object? SelectFields(JsonElement args)
    {
        if (!args.TryGetProperty("data", out JsonElement dataEl) ||
            !args.TryGetProperty("fields", out JsonElement fieldsEl))
        {
            throw new InvalidOperationException("select_fields requires 'data' and 'fields' arguments.");
        }

        string[] fields = fieldsEl.GetString()!
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (dataEl.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("select_fields: 'data' must be an array.");
        }

        List<Dictionary<string, object?>> projected = [];
        foreach (JsonElement item in dataEl.EnumerateArray())
        {
            Dictionary<string, object?> row = [];
            foreach (string field in fields)
            {
                if (item.TryGetProperty(field, out JsonElement prop))
                {
                    row[field] = prop.ValueKind switch
                    {
                        JsonValueKind.String => prop.GetString(),
                        JsonValueKind.Number when prop.TryGetInt32(out int i) => i,
                        JsonValueKind.Number => prop.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => prop.GetRawText(),
                    };
                }
                else
                {
                    row[field] = null;
                }
            }
            projected.Add(row);
        }

        return projected;
    }
}
