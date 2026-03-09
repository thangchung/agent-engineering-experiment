using CoffeeshopCli.Configuration;
using CoffeeshopCli.Infrastructure;
using CoffeeshopCli.Mcp;
using CoffeeshopCli.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CoffeeshopCli.Commands.Models;

public sealed class ModelsQuerySettings : CommandSettings
{
    [CommandArgument(0, "<model>")]
    public required string Model { get; init; }

    [CommandOption("--email <EMAIL>")]
    public string? Email { get; init; }

    [CommandOption("--customer-id <ID>")]
    public string? CustomerId { get; init; }

    [CommandOption("--order-id <ID>")]
    public string? OrderId { get; init; }

    [CommandOption("--json")]
    public bool Json { get; init; }
}

/// <summary>
/// Query data records by filters (email, customer_id, order_id).
/// Returns filtered results with only relevant fields for agent use.
/// </summary>
public sealed class ModelsQueryCommand : Command<ModelsQuerySettings>
{
    private readonly IMcpClient _client;

    public ModelsQueryCommand(IMcpClient client)
    {
        _client = client;
    }

    public override int Execute(CommandContext context, ModelsQuerySettings settings)
    {
        try
        {
            if (settings.Model.Equals("Customer", StringComparison.OrdinalIgnoreCase))
            {
                return QueryCustomer(settings);
            }

            if (settings.Json)
            {
                AnsiConsole.WriteLine(JsonHelper.ToJson(new
                {
                    error = new
                    {
                        type = "not_implemented",
                        message = $"Query not implemented for model '{settings.Model}'. Currently only 'Customer' is supported."
                    }
                }));
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Query not implemented for model:[/] {settings.Model}");
                AnsiConsole.MarkupLine("[dim]Currently only 'Customer' is supported[/]");
            }
            return 1;
        }
        catch (Exception ex)
        {
            if (settings.Json)
            {
                AnsiConsole.WriteLine(JsonHelper.ToJson(new
                {
                    error = new
                    {
                        type = "query",
                        message = ex.Message
                    }
                }));
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Query failed:[/] {ex.Message}");
            }
            return 1;
        }
    }

    private int QueryCustomer(ModelsQuerySettings settings)
    {
        if (string.IsNullOrEmpty(settings.Email) && string.IsNullOrEmpty(settings.CustomerId))
        {
            if (settings.Json)
            {
                AnsiConsole.WriteLine(JsonHelper.ToJson(new
                {
                    error = new
                    {
                        type = "validation",
                        message = "Provide at least one filter: --email or --customer-id"
                    }
                }));
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Provide at least one filter: --email or --customer-id");
            }
            return 1;
        }

        var customers = _client.GetCustomersAsync().GetAwaiter().GetResult();
        Customer? found = null;

        if (!string.IsNullOrEmpty(settings.Email))
        {
            found = customers.FirstOrDefault(c =>
                c.Email.Equals(settings.Email, StringComparison.OrdinalIgnoreCase));
        }
        else if (!string.IsNullOrEmpty(settings.CustomerId))
        {
            found = customers.FirstOrDefault(c =>
                c.CustomerId.Equals(settings.CustomerId, StringComparison.OrdinalIgnoreCase));
        }

        if (found is null)
        {
            if (settings.Json)
            {
                AnsiConsole.WriteLine(JsonHelper.ToJson(new
                {
                    ok = false,
                    error = $"No customer found with {(settings.Email != null ? $"email '{settings.Email}'" : $"id '{settings.CustomerId}'")}"
                }));
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]No customer found[/]");
            }
            return 0;
        }

        // Return filtered customer data (brevity: only relevant fields)
        var filteredCustomer = new
        {
            customer_id = found.CustomerId,
            name = found.Name,
            email = found.Email,
            tier = found.Tier.ToString()
        };

        if (settings.Json)
        {
            AnsiConsole.WriteLine(JsonHelper.ToJson(new
            {
                ok = true,
                customer = filteredCustomer
            }));
        }
        else
        {
            var table = new Table();
            table.AddColumn("Field");
            table.AddColumn("Value");
            table.AddRow("Customer ID", filteredCustomer.customer_id);
            table.AddRow("Name", filteredCustomer.name);
            table.AddRow("Email", filteredCustomer.email);
            table.AddRow("Tier", filteredCustomer.tier);

            AnsiConsole.Write(table);
        }

        return 0;
    }
}
