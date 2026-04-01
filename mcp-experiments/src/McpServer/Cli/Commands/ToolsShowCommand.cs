using System.ComponentModel;
using System.Text.Json;
using McpServer.Registry;
using Spectre.Console;
using Spectre.Console.Cli;

namespace McpServer.Cli.Commands;

/// <summary>
/// Shows metadata for one tool by name.
/// </summary>
[Description("Show metadata and input schema for one tool")]
internal sealed class ToolsShowCommand(IToolRegistry toolRegistry, UserContext userContext)
    : BaseCliCommand<ToolsShowCommand.Settings>(toolRegistry, userContext)
{
    /// <summary>
    /// CLI settings for tools show command.
    /// </summary>
    internal sealed class Settings : VerboseCommandSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("Tool name to display")]
        public string Name { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    public override Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        ToolDescriptor? tool = ToolRegistry.FindByName(settings.Name, UserContext);
        if (tool is null)
        {
            AnsiConsole.MarkupLine($"[red]Tool not found:[/] {Markup.Escape(settings.Name)}");
            return Task.FromResult(1);
        }

        Table table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Field");
        table.AddColumn("Value");
        table.AddRow("Name", tool.Name);
        table.AddRow("Description", tool.Description);
        table.AddRow("Pinned", tool.IsPinned.ToString());
        table.AddRow("Synthetic", tool.IsSynthetic.ToString());
        table.AddRow("Tags", string.Join(", ", tool.Tags));

        AnsiConsole.Write(table);
        AnsiConsole.Write(new Panel(new Text(tool.InputJsonSchema)).Header("Input JSON Schema"));
        AnsiConsole.Write(new Panel(new Text(BuildSampleUsage(tool))).Header("Sample CLI Usage"));
        return Task.FromResult(0);
    }

    internal static string BuildSampleUsage(ToolDescriptor tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        string argsJson = BuildSampleArgs(tool.InputJsonSchema);
        return string.Join(Environment.NewLine,
        [
            $"dotnet run --no-launch-profile --project src/McpServer/McpServer.csproj -- query --tool {tool.Name} --args '{argsJson}'",
            "Adjust the sample argument values to match your request.",
        ]);
    }

    private static string BuildSampleArgs(string schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return "{}";
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(schemaJson);
            if (!document.RootElement.TryGetProperty("properties", out JsonElement properties) ||
                properties.ValueKind != JsonValueKind.Object)
            {
                return "{}";
            }

            Dictionary<string, object?> sampleArgs = [];
            foreach (JsonProperty property in properties.EnumerateObject())
            {
                sampleArgs[property.Name] = BuildSampleValue(property.Name, property.Value);
            }

            return JsonSerializer.Serialize(sampleArgs);
        }
        catch (JsonException)
        {
            return "{}";
        }
    }

    private static object? BuildSampleValue(string propertyName, JsonElement schema)
    {
        string type = schema.TryGetProperty("type", out JsonElement typeElement) && typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString() ?? "string"
            : "string";

        return propertyName.ToLowerInvariant() switch
        {
            "query" => "moon",
            "page" => 1,
            "per_page" => 5,
            "body" => new Dictionary<string, object?>(),
            _ => type switch
            {
                "integer" => 1,
                "number" => 1,
                "boolean" => true,
                "array" => Array.Empty<object?>(),
                "object" => new Dictionary<string, object?>(),
                _ => "sample",
            },
        };
    }
}
