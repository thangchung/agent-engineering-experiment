using CoffeeshopCli.Commands.Models;
using CoffeeshopCli.Commands.Mcp;
using CoffeeshopCli.Commands.Docs;
using CoffeeshopCli.Commands.Skills;
using CoffeeshopCli.Configuration;
using CoffeeshopCli.Infrastructure;
using CoffeeshopCli.Mcp;
using CoffeeshopCli.Mcp.Tools;
using CoffeeshopCli.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

var services = new ServiceCollection();

// Register services
services.AddSingleton<ModelRegistry>();
services.AddSingleton<IDiscoveryService>(sp =>
{
    var registry = sp.GetRequiredService<ModelRegistry>();
    var config = ConfigLoader.Load();
    return new FileSystemDiscoveryService(registry, config.Discovery.SkillsDirectory);
});
services.AddSingleton<SkillRunner>();
services.AddSingleton<ModelTools>();
services.AddSingleton<SkillTools>();
services.AddSingleton<OrderTools>();
services.AddSingleton<ToolRegistry>();
services.AddSingleton<McpServerHost>();

// Create CommandApp with DI
var registrar = new TypeRegistrar(services);
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetApplicationName("coffeeshop-cli");

    // models branch
    config.AddBranch("models", models =>
    {
        models.SetDescription("Browse and submit data models");
        models.AddCommand<ModelsListCommand>("list")
            .WithDescription("List all discovered data models");
        models.AddCommand<ModelsShowCommand>("show")
            .WithDescription("Display model schema (properties, types, validation rules)");
        models.AddCommand<ModelsQueryCommand>("query")
            .WithDescription("Query data records by filters (email, customer_id)");
        models.AddCommand<ModelsBrowseCommand>("browse")
            .WithDescription("List all records for a data model");
        models.AddCommand<ModelsSubmitCommand>("submit")
            .WithDescription("Submit JSON payload against a model for validation");
    });

    // skills branch
    config.AddBranch("skills", skills =>
    {
        skills.SetDescription("Browse and invoke agent skills");
        skills.AddCommand<SkillsListCommand>("list")
            .WithDescription("List all discovered skills");
        skills.AddCommand<SkillsShowCommand>("show")
            .WithDescription("Display skill manifest (frontmatter + body)");
        skills.AddCommand<SkillsInvokeCommand>("invoke")
            .WithDescription("Run skill loop interactively");
    });

    config.AddBranch("mcp", mcp =>
    {
        mcp.SetDescription("Expose tools over stdio MCP transport");
        mcp.AddCommand<McpServeCommand>("serve")
            .WithDescription("Start MCP stdio server");
    });

    config.AddBranch("docs", docs =>
    {
        docs.SetDescription("Browse docs in TUI");
        docs.AddCommand<DocsBrowseCommand>("browse")
            .WithDescription("Browse models and skills");
    });
});

return app.Run(args);
