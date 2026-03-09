using CoffeeshopCli.Infrastructure;
using CoffeeshopCli.Mcp;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CoffeeshopCli.Commands.Models;

public sealed class ModelsBrowseSettings : CommandSettings
{
    [CommandArgument(0, "<model>")]
    public required string Model { get; init; }

    [CommandOption("--json")]
    public bool Json { get; init; }
}

/// <summary>
/// Browse/list all records for a data model.
/// Returns filtered results with only relevant fields for agent use.
/// </summary>
public sealed class ModelsBrowseCommand : Command<ModelsBrowseSettings>
{
    private readonly IMcpClient _client;

    public ModelsBrowseCommand(IMcpClient client)
    {
        _client = client;
    }

    public override int Execute(CommandContext context, ModelsBrowseSettings settings)
    {
        try
        {
            if (settings.Model.Equals("Customer", StringComparison.OrdinalIgnoreCase))
            {
                return BrowseCustomers(settings);
            }

            if (settings.Model.Equals("MenuItem", StringComparison.OrdinalIgnoreCase))
            {
                return BrowseMenuItems(settings);
            }

            if (settings.Json)
            {
                AnsiConsole.WriteLine(JsonHelper.ToJson(new
                {
                    error = new
                    {
                        type = "not_implemented",
                        message = $"Browse not implemented for model '{settings.Model}'. Currently only 'Customer' and 'MenuItem' are supported."
                    }
                }));
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Browse not implemented for model:[/] {settings.Model}");
                AnsiConsole.MarkupLine("[dim]Currently only 'Customer' and 'MenuItem' are supported[/]");
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
                        type = "browse",
                        message = ex.Message
                    }
                }));
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Browse failed:[/] {ex.Message}");
            }
            return 1;
        }
    }

    private int BrowseCustomers(ModelsBrowseSettings settings)
    {
        var customers = _client.GetCustomersAsync().GetAwaiter().GetResult();

        // Return filtered customer data (brevity: only relevant fields)
        var filteredCustomers = customers.Select(c => new
        {
            customer_id = c.CustomerId,
            name = c.Name,
            email = c.Email,
            tier = c.Tier.ToString()
        }).ToList();

        if (settings.Json)
        {
            AnsiConsole.WriteLine(JsonHelper.ToJson(new
            {
                ok = true,
                count = filteredCustomers.Count,
                customers = filteredCustomers
            }));
        }
        else
        {
            var table = new Table();
            table.AddColumn("Customer ID");
            table.AddColumn("Name");
            table.AddColumn("Email");
            table.AddColumn("Tier");

            foreach (var customer in filteredCustomers)
            {
                table.AddRow(customer.customer_id, customer.name, customer.email, customer.tier);
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"\n[dim]Total: {filteredCustomers.Count} customers[/]");
        }

        return 0;
    }

    private int BrowseMenuItems(ModelsBrowseSettings settings)
    {
        var items = _client.GetMenuAsync().GetAwaiter().GetResult();

        var filteredItems = items.Select(i => new
        {
            item_type = i.ItemType.ToString(),
            name = i.Name,
            category = i.Category,
            price = i.Price
        }).ToList();

        if (settings.Json)
        {
            AnsiConsole.WriteLine(JsonHelper.ToJson(new
            {
                ok = true,
                count = filteredItems.Count,
                items = filteredItems
            }));
        }
        else
        {
            var table = new Table();
            table.AddColumn("Item Type");
            table.AddColumn("Name");
            table.AddColumn("Category");
            table.AddColumn("Price");

            foreach (var item in filteredItems)
            {
                table.AddRow(
                    item.item_type,
                    item.name,
                    item.category,
                    $"${item.price:F2}");
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"\n[dim]Total: {filteredItems.Count} items[/]");
        }

        return 0;
    }
}
