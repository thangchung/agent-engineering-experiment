using CoffeeshopCli.Errors;
using CoffeeshopCli.Infrastructure;
using CoffeeshopCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CoffeeshopCli.Commands.Skills;

public sealed class SkillsInvokeSettings : CommandSettings
{
    [CommandArgument(0, "<name>")]
    public required string Name { get; init; }

    [CommandOption("--json")]
    public bool Json { get; init; }
}

/// <summary>
/// Invokes a skill using the simplified interactive state-machine runner.
/// </summary>
public sealed class SkillsInvokeCommand : Command<SkillsInvokeSettings>
{
    private readonly SkillRunner _runner;

    public SkillsInvokeCommand(SkillRunner runner)
    {
        _runner = runner;
    }

    public override int Execute(CommandContext context, SkillsInvokeSettings settings)
    {
        try
        {
            var code = _runner.RunInteractive(settings.Name);
            if (settings.Json)
            {
                AnsiConsole.WriteLine(JsonHelper.ToJson(new
                {
                    success = true,
                    exit_code = code,
                    state = _runner.State
                }));
            }

            return code;
        }
        catch (SkillError ex)
        {
            if (settings.Json)
            {
                AnsiConsole.WriteLine(JsonHelper.ToJson(new
                {
                    error = new
                    {
                        type = ex.Type,
                        message = ex.Message
                    }
                }));
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            }

            return ex.ExitCode;
        }
    }
}
