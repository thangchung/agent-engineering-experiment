using CoffeeshopCli.Errors;
using CoffeeshopCli.Models;
using Spectre.Console;

namespace CoffeeshopCli.Services;

/// <summary>
/// Executes a simplified 4-step skill loop with persistent in-memory state.
/// </summary>
public sealed class SkillRunner
{
    private readonly IDiscoveryService _discoveryService;

    public SkillRunner(IDiscoveryService discoveryService)
    {
        _discoveryService = discoveryService;
    }

    public Dictionary<string, object?> State { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CUSTOMER"] = null,
        ["INTENT"] = null,
        ["ORDER"] = null
    };

    public int RunInteractive(string skillName)
    {
        var skill = _discoveryService.DiscoverSkills()
            .FirstOrDefault(s => s.Name.Equals(skillName, StringComparison.OrdinalIgnoreCase));
        if (skill is null)
        {
            throw new SkillError($"Skill '{skillName}' not found");
        }

        AnsiConsole.MarkupLine("[bold cyan]STEP 1 - INTAKE[/]");
        var customerId = AnsiConsole.Ask<string>("Enter customer id (e.g. C-1001):");
        State["CUSTOMER"] = customerId;

        AnsiConsole.MarkupLine("[bold cyan]STEP 2 - CLASSIFY[/]");
        var intent = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select intent")
                .AddChoices("order-status", "account", "item-types", "process-order")
        );
        State["INTENT"] = intent;

        AnsiConsole.MarkupLine("[bold cyan]STEP 3 - REVIEW[/]");
        AnsiConsole.MarkupLine($"Customer: [green]{Markup.Escape(customerId)}[/]");
        AnsiConsole.MarkupLine($"Intent: [green]{Markup.Escape(intent)}[/]");

        AnsiConsole.MarkupLine("[bold cyan]STEP 4 - FINALIZE[/]");
        if (intent.Equals("process-order", StringComparison.OrdinalIgnoreCase))
        {
            var itemType = AnsiConsole.Prompt(
                new SelectionPrompt<ItemType>()
                    .Title("Select item")
                    .AddChoices(Enum.GetValues<ItemType>())
            );
            var qty = AnsiConsole.Ask<int>("Quantity:", 1);
            State["ORDER"] = new { item_type = itemType, qty };
            AnsiConsole.MarkupLine($"[green]Captured order request:[/] {itemType} x {qty}");
        }

        AnsiConsole.MarkupLine("[green]Skill execution complete.[/]");
        return 0;
    }
}
