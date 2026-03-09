using CoffeeshopCli.Errors;
using CoffeeshopCli.Infrastructure;
using CoffeeshopCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CoffeeshopCli.Commands.Models;

public sealed class ModelsShowSettings : CommandSettings
{
    [CommandArgument(0, "<name>")]
    public required string Name { get; init; }

    [CommandOption("--json")]
    public bool Json { get; init; }
}

/// <summary>
/// Displays a model's schema (properties, types, validation rules).
/// </summary>
public sealed class ModelsShowCommand : Command<ModelsShowSettings>
{
    private readonly ModelRegistry _registry;

    public ModelsShowCommand(ModelRegistry registry)
    {
        _registry = registry;
    }

    public override int Execute(CommandContext context, ModelsShowSettings settings)
    {
        try
        {
            var schema = _registry.GetSchema(settings.Name);

            if (settings.Json)
            {
                var output = new
                {
                    name = schema.Name,
                    properties = schema.Properties.Select(p => new
                    {
                        name = p.Name,
                        type = p.TypeName,
                        required = p.IsRequired,
                        nullable = p.IsNullable,
                        attributes = p.Attributes,
                        enum_values = p.EnumValues,
                        has_children = p.ChildProperties != null
                    })
                };
                AnsiConsole.WriteLine(JsonHelper.ToJson(output));
                return 0;
            }

            // TUI mode - render as tree
            var tree = new Tree($"[bold cyan]{schema.Name}[/]");
            BuildTree(tree.AddNode("[yellow]Properties[/]"), schema.Properties);

            AnsiConsole.Write(tree);
            return 0;
        }
        catch (KeyNotFoundException ex)
        {
            if (settings.Json)
            {
                AnsiConsole.WriteLine(JsonHelper.ToJson(new
                {
                    error = new
                    {
                        type = "discovery",
                        message = ex.Message
                    }
                }));
            }
            else
            {
                var panel = new Panel($"[red]{ex.Message}[/]")
                {
                    Header = new PanelHeader(" Error "),
                    Border = BoxBorder.Rounded
                };
                AnsiConsole.Write(panel);
            }
            return 2; // Discovery error exit code
        }
    }

    private static void BuildTree(TreeNode parent, List<PropertySchema> properties)
    {
        foreach (var prop in properties)
        {
            var label = $"[green]{prop.Name}[/]: [blue]{prop.TypeName}[/]";

            if (prop.IsRequired)
            {
                label += " [red](required)[/]";
            }

            if (prop.Attributes.Any())
            {
                var attrs = string.Join(", ", prop.Attributes.Select(a => $"{a.Key}={a.Value}"));
                label += $" [dim]({attrs})[/]";
            }

            var node = parent.AddNode(label);

            // Add enum values
            if (prop.EnumValues != null && prop.EnumValues.Any())
            {
                var enumNode = node.AddNode("[yellow]Enum values:[/]");
                foreach (var value in prop.EnumValues)
                {
                    enumNode.AddNode($"[dim]{value}[/]");
                }
            }

            // Add child properties
            if (prop.ChildProperties != null && prop.ChildProperties.Any())
            {
                BuildTree(node, prop.ChildProperties);
            }
        }
    }
}
