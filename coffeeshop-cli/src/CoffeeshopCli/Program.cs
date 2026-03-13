using CoffeeshopCli.Commands.Skills;
using CoffeeshopCli.Commands.Mcp;
using CoffeeshopCli.Commands.Models;
using CoffeeshopCli.Commands.Docs;
using CoffeeshopCli.Configuration;
using CoffeeshopCli.Infrastructure;
using CoffeeshopCli.Mcp;
using CoffeeshopCli.Mcp.Tools;
using CoffeeshopCli.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Spectre.Console.Cli;

var cfg = ConfigLoader.Load();

return cfg.Hosting.EnableHttpMcpBridge
    ? await RunHttpBridgeAsync(cfg)
    : RunCliMode(args, cfg);

static int RunCliMode(string[] args, CliConfig cfg)
{
    var services = new ServiceCollection();
    services.AddSingleton(cfg);
    services.AddSingleton<ModelRegistry>();
    services.AddSingleton<IDiscoveryService>(sp =>
    {
        var registry = sp.GetRequiredService<ModelRegistry>();
        var loaded = sp.GetRequiredService<CliConfig>();
        return new FileSystemDiscoveryService(registry, loaded.Discovery.SkillsDirectory);
    });
    services.AddSingleton<SkillRunner>();
    services.AddSingleton<ModelTools>();
    services.AddSingleton<SkillTools>();
    services.AddSingleton<OrderTools>();
    services.AddSingleton<OrderSubmitHandler>();
    services.AddSingleton<ToolRegistry>();
    services.AddSingleton<McpServerHost>();

    var registrar = new TypeRegistrar(services);
    var app = new CommandApp(registrar);

    app.Configure(config =>
    {
        config.SetApplicationName("coffeeshop-cli");

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
}

static async Task<int> RunHttpBridgeAsync(CliConfig cfg)
{
    var builder = WebApplication.CreateBuilder();

    if (!string.IsNullOrWhiteSpace(cfg.Hosting.Urls))
    {
        builder.WebHost.UseUrls(cfg.Hosting.Urls);
    }

    builder.Services.AddSingleton(cfg);
    builder.Services.AddSingleton<ModelRegistry>();
    builder.Services.AddSingleton<IDiscoveryService>(sp =>
    {
        var registry = sp.GetRequiredService<ModelRegistry>();
        var loaded = sp.GetRequiredService<CliConfig>();
        return new FileSystemDiscoveryService(registry, loaded.Discovery.SkillsDirectory);
    });
    builder.Services.AddSingleton<SkillParser>();
    builder.Services.AddSingleton<OrderSubmitHandler>();
    builder.Services.AddHealthChecks();

    builder.Services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = "coffeeshop-cli-http-bridge",
                Version = "0.1.0"
            };
        })
        .WithHttpTransport()
        .WithTools<CoffeeshopMcpBridgeTools>();

    var app = builder.Build();
    app.MapHealthChecks(cfg.Hosting.HealthRoute);
    app.MapMcp(cfg.Hosting.HttpMcpRoute);

    await app.RunAsync();
    return 0;
}

