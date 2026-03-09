using System.Text.Json;
using CoffeeshopCli.Configuration;
using CoffeeshopCli.Errors;
using CoffeeshopCli.Infrastructure;
using CoffeeshopCli.Mcp;
using CoffeeshopCli.Models;
using CoffeeshopCli.Services;
using CoffeeshopCli.Validation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CoffeeshopCli.Commands.Models;

public sealed class ModelsSubmitSettings : CommandSettings
{
    [CommandArgument(0, "<model>")]
    public required string Model { get; init; }

    [CommandOption("--file <FILE>")]
    public string? File { get; init; }

    [CommandOption("--json")]
    public bool Json { get; init; }
}

/// <summary>
/// Submit JSON payload for validation against a data model.
/// Reads from stdin if --file not provided.
/// </summary>
public sealed class ModelsSubmitCommand : Command<ModelsSubmitSettings>
{
    private readonly IDiscoveryService _discovery;

    public ModelsSubmitCommand(IDiscoveryService discovery)
    {
        _discovery = discovery;
    }

    public override int Execute(CommandContext context, ModelsSubmitSettings settings)
    {
        // Check if model exists
        var models = _discovery.DiscoverModels();
        var modelInfo = models.FirstOrDefault(m =>
            m.Name.Equals(settings.Model, StringComparison.OrdinalIgnoreCase));

        if (modelInfo is null)
        {
            if (settings.Json)
            {
                AnsiConsole.WriteLine(JsonHelper.ToJson(new
                {
                    error = new
                    {
                        type = "discovery",
                        message = $"Model '{settings.Model}' not found"
                    }
                }));
            }
            else
            {
                var errorPanel = new Panel($"[red]Model not found:[/] {settings.Model}")
                {
                    Header = new PanelHeader(" Error "),
                    Border = BoxBorder.Rounded
                };
                AnsiConsole.Write(errorPanel);
            }
            return 2;
        }

        // Read JSON input
        string jsonInput;
        if (settings.File != null)
        {
            if (!System.IO.File.Exists(settings.File))
            {
                if (settings.Json)
                {
                    AnsiConsole.WriteLine(JsonHelper.ToJson(new
                    {
                        error = new
                        {
                            type = "discovery",
                            message = $"File not found: {settings.File}"
                        }
                    }));
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]File not found:[/] {settings.File}");
                }
                return 2;
            }
            jsonInput = System.IO.File.ReadAllText(settings.File);
        }
        else
        {
            // Read from stdin
            jsonInput = Console.In.ReadToEnd();
        }

        if (string.IsNullOrWhiteSpace(jsonInput))
        {
            if (settings.Json)
            {
                AnsiConsole.WriteLine(JsonHelper.ToJson(new
                {
                    error = new
                    {
                        type = "validation",
                        message = "No input provided"
                    }
                }));
            }
            else
            {
                AnsiConsole.MarkupLine("[red]No input provided[/]");
            }
            return 1;
        }

        // Deserialize and validate
        try
        {
            if (modelInfo.Type.Name.Equals("Order", StringComparison.OrdinalIgnoreCase))
            {
                var input = DeserializeModel<SimplifiedOrderInput>(jsonInput);
                if (input is null)
                {
                    throw new ValidationError("Failed to deserialize simplified order input");
                }

                var handler = new OrderSubmitHandler();
                var submittedOrder = handler.SubmitAsync(input).GetAwaiter().GetResult();

                if (settings.Json)
                {
                    AnsiConsole.WriteLine(JsonHelper.ToJson(new
                    {
                        success = true,
                        message = "Order submitted successfully",
                        order = submittedOrder
                    }));
                }
                else
                {
                    var successPanel = new Panel($"[green]Order submitted successfully:[/] {submittedOrder.OrderId}")
                    {
                        Header = new PanelHeader(" Success "),
                        Border = BoxBorder.Rounded
                    };
                    AnsiConsole.Write(successPanel);
                }

                return 0;
            }

            var validationError = modelInfo.Type.Name switch
            {
                "Customer" => ValidateModel<Customer>(jsonInput, new CustomerValidator()),
                "MenuItem" => ValidateModel<MenuItem>(jsonInput, new MenuItemValidator()),
                "OrderItem" => ValidateModel<OrderItem>(jsonInput, new OrderItemValidator()),
                _ => null
            };

            if (validationError != null)
            {
                if (settings.Json)
                {
                    var errorObj = new
                    {
                        error = new
                        {
                            type = validationError.Type,
                            message = validationError.Message,
                            details = validationError.Details
                        }
                    };
                    AnsiConsole.WriteLine(JsonHelper.ToJson(errorObj));
                }
                else
                {
                    var panel = new Panel($"[red]{validationError.Message}[/]")
                    {
                        Header = new PanelHeader(" Validation Error "),
                        Border = BoxBorder.Rounded
                    };
                    AnsiConsole.Write(panel);

                    if (validationError.Details != null && validationError.Details.TryGetValue("errors", out var errorsObj))
                    {
                        if (errorsObj is List<string> errorList)
                        {
                            foreach (var error in errorList)
                            {
                                AnsiConsole.MarkupLine($"  [red]•[/] {Markup.Escape(error)}");
                            }
                        }
                    }
                }
                return 1;
            }

            // Success
            if (settings.Json)
            {
                AnsiConsole.WriteLine(JsonHelper.ToJson(new
                {
                    success = true,
                    message = $"{modelInfo.Name} validated successfully"
                }));
            }
            else
            {
                var successPanel = new Panel($"[green]{modelInfo.Name} validated successfully[/]")
                {
                    Header = new PanelHeader(" Success "),
                    Border = BoxBorder.Rounded
                };
                AnsiConsole.Write(successPanel);
            }
            return 0;
        }
        catch (CliError cliError)
        {
            if (settings.Json)
            {
                AnsiConsole.WriteLine(JsonHelper.ToJson(new
                {
                    error = new
                    {
                        type = cliError.Type,
                        message = cliError.Message,
                        details = cliError.Details
                    }
                }));
            }
            else
            {
                var panel = new Panel($"[red]{Markup.Escape(cliError.Message)}[/]")
                {
                    Header = new PanelHeader($" {cliError.Type.ToUpperInvariant()} Error "),
                    Border = BoxBorder.Rounded
                };
                AnsiConsole.Write(panel);
            }

            return cliError.ExitCode;
        }
        catch (JsonException ex)
        {
            if (settings.Json)
            {
                AnsiConsole.WriteLine(JsonHelper.ToJson(new
                {
                    error = new
                    {
                        type = "validation",
                        message = $"Invalid JSON: {ex.Message}"
                    }
                }));
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Invalid JSON:[/] {Markup.Escape(ex.Message)}");
            }
            return 1;
        }
    }

    private static Errors.ValidationError? ValidateModel<T>(string json, IValidator<T> validator)
    {
        var model = DeserializeModel<T>(json);

        if (model is null)
        {
            return new Errors.ValidationError("Failed to deserialize model");
        }

        return validator.Validate(model);
    }

    private static T? DeserializeModel<T>(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        return System.Text.Json.JsonSerializer.Deserialize<T>(json, options);
    }
}
