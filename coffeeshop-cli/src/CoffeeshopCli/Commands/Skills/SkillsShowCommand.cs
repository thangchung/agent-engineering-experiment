using CoffeeshopCli.Infrastructure;
using CoffeeshopCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CoffeeshopCli.Commands.Skills;

public sealed class SkillsShowSettings : CommandSettings
{
    [CommandArgument(0, "<name>")]
    public required string Name { get; init; }

    [CommandOption("--json")]
    public bool Json { get; init; }
}

/// <summary>
/// Displays full skill manifest (frontmatter + body).
/// </summary>
public sealed class SkillsShowCommand : Command<SkillsShowSettings>
{
    private readonly IDiscoveryService _discovery;

    public SkillsShowCommand(IDiscoveryService discovery)
    {
        _discovery = discovery;
    }

    public override int Execute(CommandContext context, SkillsShowSettings settings)
    {
        var skills = _discovery.DiscoverSkills();
        var skill = skills.FirstOrDefault(s =>
            s.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

        if (skill is null)
        {
            if (settings.Json)
            {
                AnsiConsole.WriteLine(JsonHelper.ToJson(new
                {
                    error = new
                    {
                        type = "discovery",
                        message = $"Skill '{settings.Name}' not found"
                    }
                }));
            }
            else
            {
                var errorPanel = new Panel($"[red]Skill not found:[/] {settings.Name}")
                {
                    Header = new PanelHeader(" Error "),
                    Border = BoxBorder.Rounded
                };
                AnsiConsole.Write(errorPanel);
            }
            return 1; // Validation error
        }

        var content = skill.Content ?? File.ReadAllText(skill.Path);
        var manifest = new SkillParser().Parse(content);

        if (settings.Json)
        {
            var output = new
            {
                frontmatter = new
                {
                    name = manifest.Frontmatter.Name,
                    description = manifest.Frontmatter.Description,
                    license = manifest.Frontmatter.License,
                    compatibility = manifest.Frontmatter.Compatibility,
                    metadata = manifest.Frontmatter.Metadata
                },
                body = manifest.Body
            };
            AnsiConsole.WriteLine(JsonHelper.ToJson(output));
            return 0;
        }

        // TUI mode - Panel with frontmatter header + body
        var headerText = $"[bold cyan]{manifest.Frontmatter.Name}[/] v{manifest.Frontmatter.Metadata.Version}\n" +
                         $"[dim]{manifest.Frontmatter.Description}[/]\n\n" +
                         $"[yellow]Loop:[/] {manifest.Frontmatter.Metadata.LoopType}  " +
                         $"[yellow]Category:[/] {manifest.Frontmatter.Metadata.Category}\n\n" +
                         $"───────────────────\n\n" +
                         Markup.Escape(manifest.Body);

        var panel = new Panel(headerText)
        {
            Header = new PanelHeader($" {manifest.Frontmatter.Name} "),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
        return 0;
    }
}
