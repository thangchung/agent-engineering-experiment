using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace McpServer.CodeMode;

/// <summary>
/// Local execution backend that enforces timeout and max-call limits.
/// </summary>
public sealed class LocalConstrainedRunner(TimeSpan timeout, int maxToolCalls, ILogger<LocalConstrainedRunner> logger) : ISandboxRunner
{
    private static readonly ActivitySource ActivitySource = new("McpServer.CodeMode.LocalConstrainedRunner");
    private static readonly Regex AssignCallRegex = new(
        "^(?:(?:const|let|var)\\s+)?(?<var>[A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*await\\s+call_tool\\((?<name>.+?),\\s*(?<args>\\{.*\\})\\)\\s*;?\\s*$",
        RegexOptions.Compiled);

    private static readonly Regex ReturnCallRegex = new(
        "^return\\s+await\\s+call_tool\\((?<name>.+?),\\s*(?<args>\\{.*\\})\\)\\s*;?\\s*$",
        RegexOptions.Compiled);

    private static readonly Regex ReturnVarRegex = new(
        "^return\\s+(?<var>[A-Za-z_][A-Za-z0-9_]*)\\s*;?\\s*$",
        RegexOptions.Compiled);

    private static readonly Regex ResultRefRegex = new(
        "^(?<var>[A-Za-z_][A-Za-z0-9_]*)\\[(\"result\"|'result')\\]$",
        RegexOptions.Compiled);

    /// <summary>
    /// Runs constrained code and returns only the final value.
    /// </summary>
    public async Task<RunnerResult> RunAsync(string code, ToolBridge bridge, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(bridge);

        logger?.LogInformation("Code generated {code}. Timeout: {timeout}, MaxToolCalls: {maxToolCalls}.", code, timeout, maxToolCalls);

        using Activity? activity = ActivitySource.StartActivity("codemode.run", ActivityKind.Internal);
        activity?.SetTag("mcp.code", code);
        activity?.SetTag("mcp.code.length", code.Length);
        activity?.SetTag("mcp.execute.timeout.ms", timeout.TotalMilliseconds);
        activity?.SetTag("mcp.execute.maxToolCalls", maxToolCalls);

        using CancellationTokenSource timeoutCts = new(timeout);
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        Dictionary<string, object?> locals = [];
        object? finalValue = null;
        int executedCalls = 0;

        foreach (string statement in ParseStatements(code))
        {
            linkedCts.Token.ThrowIfCancellationRequested();

            Match assignCall = AssignCallRegex.Match(statement);
            if (assignCall.Success)
            {
                object? callResult = await InvokeBridgeAsync(assignCall, bridge, locals, executedCalls, linkedCts.Token);
                executedCalls++;
                locals[assignCall.Groups["var"].Value] = callResult;
                finalValue = callResult;
                continue;
            }

            Match returnCall = ReturnCallRegex.Match(statement);
            if (returnCall.Success)
            {
                object? callResult = await InvokeBridgeAsync(returnCall, bridge, locals, executedCalls, linkedCts.Token);
                executedCalls++;
                activity?.SetTag("mcp.execute.callCount", executedCalls);
                activity?.SetTag("mcp.execute.hasFinalValue", callResult is not null);
                return new RunnerResult(callResult, executedCalls);
            }

            Match returnVar = ReturnVarRegex.Match(statement);
            if (returnVar.Success)
            {
                string varName = returnVar.Groups["var"].Value;
                if (!locals.TryGetValue(varName, out object? localValue))
                {
                    throw new InvalidOperationException($"Unknown variable in return statement: {varName}.");
                }

                activity?.SetTag("mcp.execute.callCount", executedCalls);
                activity?.SetTag("mcp.execute.hasFinalValue", localValue is not null);
                return new RunnerResult(localValue, executedCalls);
            }

            throw new InvalidOperationException($"Unsupported execute statement: {statement}");
        }

        activity?.SetTag("mcp.execute.callCount", executedCalls);
        activity?.SetTag("mcp.execute.hasFinalValue", finalValue is not null);
        return new RunnerResult(finalValue, executedCalls);
    }

    private async Task<object?> InvokeBridgeAsync(
        Match match,
        ToolBridge bridge,
        IReadOnlyDictionary<string, object?> locals,
        int executedCalls,
        CancellationToken ct)
    {
        if (executedCalls >= maxToolCalls)
        {
            throw new InvalidOperationException($"Maximum tool calls exceeded: {maxToolCalls}.");
        }

        string toolName = ParseQuotedString(match.Groups["name"].Value.Trim());
        using Activity? activity = ActivitySource.StartActivity("codemode.call_tool", ActivityKind.Internal);
        activity?.SetTag("mcp.tool.name", toolName);
        activity?.SetTag("mcp.execute.callIndex", executedCalls + 1);

        Dictionary<string, object?> args = ParseArguments(match.Groups["args"].Value, locals);
        activity?.SetTag("mcp.args.count", args.Count);

        JsonElement argsElement = JsonSerializer.SerializeToElement(args);
        object? result = await bridge(toolName, argsElement, ct);
        activity?.SetTag("mcp.call.hasResult", result is not null);
        return result;
    }

    private static IEnumerable<string> ParseStatements(string code)
    {
        return code
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'));
    }

    private static string ParseQuotedString(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        throw new InvalidOperationException($"Expected quoted string but found: {value}");
    }

    private static Dictionary<string, object?> ParseArguments(string argsSource, IReadOnlyDictionary<string, object?> locals)
    {
        string trimmed = argsSource.Trim();
        if (trimmed == "{}")
        {
            return [];
        }

        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
        {
            throw new InvalidOperationException($"Arguments must be an object literal: {trimmed}");
        }

        string content = trimmed[1..^1].Trim();
        if (content.Length == 0)
        {
            return [];
        }

        Dictionary<string, object?> args = [];
        foreach (string pair in SplitTopLevel(content, ','))
        {
            int colonIndex = FindTopLevelColon(pair);
            if (colonIndex <= 0)
            {
                throw new InvalidOperationException($"Invalid argument expression: {pair}");
            }

            string rawKey = pair[..colonIndex].Trim();
            string rawValue = pair[(colonIndex + 1)..].Trim();

            // Accept both quoted ("key") and bare identifier (key) forms.
            string key = (rawKey.Length >= 2 &&
                          ((rawKey[0] == '"' && rawKey[^1] == '"') || (rawKey[0] == '\'' && rawKey[^1] == '\'')))
                ? rawKey[1..^1]
                : rawKey;
            args[key] = ParseValue(rawValue, locals);
        }

        return args;
    }

    private static object? ParseValue(string value, IReadOnlyDictionary<string, object?> locals)
    {
        if (value == "null")
        {
            return null;
        }

        if (value == "true")
        {
            return true;
        }

        if (value == "false")
        {
            return false;
        }

        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        Match refMatch = ResultRefRegex.Match(value);
        if (refMatch.Success)
        {
            string varName = refMatch.Groups["var"].Value;
            if (!locals.TryGetValue(varName, out object? localValue))
            {
                throw new InvalidOperationException($"Unknown variable in arguments: {varName}.");
            }

            if (TryReadResultValue(localValue, out object? resultValue))
            {
                return resultValue;
            }

            throw new InvalidOperationException($"Variable '{varName}' does not contain a readable 'result' value.");
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
        {
            return intValue;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleValue))
        {
            return doubleValue;
        }

        // Allow bare variable references (e.g. data: results)
        if (System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z_][A-Za-z0-9_]*$"))
        {
            if (locals.TryGetValue(value, out object? localVarValue))
            {
                return localVarValue;
            }

            throw new InvalidOperationException($"Unknown variable in arguments: {value}.");
        }

        throw new InvalidOperationException($"Unsupported argument value: {value}");
    }

    private static bool TryReadResultValue(object? localValue, out object? result)
    {
        if (localValue is JsonElement localJson &&
            localJson.ValueKind is JsonValueKind.Object &&
            localJson.TryGetProperty("result", out JsonElement resultProperty))
        {
            result = JsonSerializer.Deserialize<object?>(resultProperty.GetRawText());
            return true;
        }

        if (localValue is IReadOnlyDictionary<string, object?> readOnlyDict &&
            readOnlyDict.TryGetValue("result", out object? readOnlyValue))
        {
            result = readOnlyValue;
            return true;
        }

        if (localValue is IDictionary<string, object?> dict &&
            dict.TryGetValue("result", out object? dictValue))
        {
            result = dictValue;
            return true;
        }

        result = null;
        return false;
    }

    private static int FindTopLevelColon(string source)
    {
        bool inSingleQuote = false;
        bool inDoubleQuote = false;

        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (c == ':' && !inSingleQuote && !inDoubleQuote)
            {
                return i;
            }
        }

        return -1;
    }

    private static IEnumerable<string> SplitTopLevel(string source, char separator)
    {
        List<string> parts = [];
        int start = 0;
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        int depth = 0;

        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (inSingleQuote || inDoubleQuote)
            {
                continue;
            }

            if (c == '{' || c == '[')
            {
                depth++;
                continue;
            }

            if (c == '}' || c == ']')
            {
                depth--;
                continue;
            }

            if (c == separator && depth == 0)
            {
                parts.Add(source[start..i].Trim());
                start = i + 1;
            }
        }

        parts.Add(source[start..].Trim());
        return parts.Where(part => part.Length > 0);
    }
}
