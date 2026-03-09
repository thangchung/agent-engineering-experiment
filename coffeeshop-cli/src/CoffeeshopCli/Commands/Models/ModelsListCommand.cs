using CoffeeshopCli.Infrastructure;
using CoffeeshopCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CoffeeshopCli.Commands.Models;

public sealed class ModelsListSettings : CommandSettings
{
    [CommandOption("--json")]
    public bool Json { get; init; }
}

/// <summary>
/// Lists all discovered data models.
/// </summary>
public sealed class ModelsListCommand : Command<ModelsListSettings>
{
    private readonly IDiscoveryService _discovery;

    public ModelsListCommand(IDiscoveryService discovery)
    {
        _discovery = discovery;
    }

    public override int Execute(CommandContext context, ModelsListSettings settings)
    {
        var models = _discovery.DiscoverModels();

        if (settings.Json)
        {
            var output = models.Select(m => new
            {
                name = m.Name,
                property_count = m.PropertyCount
            });
            AnsiConsole.WriteLine(JsonHelper.ToJson(output));
            return 0;
        }

        // TUI mode
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Name[/]")
            .AddColumn("[bold]Properties[/]")
            .AddColumn("[bold]Type[/]");

        foreach (var model in models)
        {
            table.AddRow(
                $"[cyan]{model.Name}[/]",
                model.PropertyCount.ToString(),
                "Record");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
