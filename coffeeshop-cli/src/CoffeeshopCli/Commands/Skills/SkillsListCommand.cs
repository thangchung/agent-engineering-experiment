using CoffeeshopCli.Infrastructure;
using CoffeeshopCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CoffeeshopCli.Commands.Skills;

public sealed class SkillsListSettings : CommandSettings
{
    [CommandOption("--json")]
    public bool Json { get; init; }
}

/// <summary>
/// Lists all discovered agent skills.
/// </summary>
public sealed class SkillsListCommand : Command<SkillsListSettings>
{
    private readonly IDiscoveryService _discovery;

    public SkillsListCommand(IDiscoveryService discovery)
    {
        _discovery = discovery;
    }

    public override int Execute(CommandContext context, SkillsListSettings settings)
    {
        var skills = _discovery.DiscoverSkills();

        if (settings.Json)
        {
            var output = skills.Select(s => new
            {
                name = s.Name,
                description = s.Description,
                version = s.Version,
                category = s.Category,
                loop_type = s.LoopType
            });
            AnsiConsole.WriteLine(JsonHelper.ToJson(output));
            return 0;
        }

        // TUI mode
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Name[/]")
            .AddColumn("[bold]Description[/]")
            .AddColumn("[bold]Version[/]")
            .AddColumn("[bold]Loop Type[/]");

        foreach (var skill in skills)
        {
            var desc = skill.Description.Length > 60 
                ? skill.Description[..60] + "…" 
                : skill.Description;
            
            table.AddRow(
                $"[cyan]{skill.Name}[/]",
                desc,
                skill.Version,
                skill.LoopType);
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
