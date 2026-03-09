using CoffeeshopCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CoffeeshopCli.Commands.Docs;

public sealed class DocsBrowseSettings : CommandSettings
{
}

/// <summary>
/// Displays discoverable docs content (models + skills) in a browsable tree view.
/// </summary>
public sealed class DocsBrowseCommand : Command<DocsBrowseSettings>
{
    private readonly IDiscoveryService _discovery;

    public DocsBrowseCommand(IDiscoveryService discovery)
    {
        _discovery = discovery;
    }

    public override int Execute(CommandContext context, DocsBrowseSettings settings)
    {
        var root = new Tree("[bold]Coffeeshop Docs[/]");

        var modelsNode = root.AddNode("[cyan]Models[/]");
        foreach (var model in _discovery.DiscoverModels().OrderBy(m => m.Name))
        {
            modelsNode.AddNode($"{model.Name} ({model.PropertyCount} props)");
        }

        var skillsNode = root.AddNode("[green]Skills[/]");
        foreach (var skill in _discovery.DiscoverSkills().OrderBy(s => s.Name))
        {
            skillsNode.AddNode($"{skill.Name} (v{skill.Version})");
        }

        AnsiConsole.Write(root);
        return 0;
    }
}
