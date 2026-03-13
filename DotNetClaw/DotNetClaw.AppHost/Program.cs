var builder = DistributedApplication.CreateBuilder(args);

// Register the main DotNetClaw project as a service named "dotnetclaw".
// Aspire will launch it, inject ASPNETCORE_URLS, and expose it in the dashboard.
var app = builder.AddProject<Projects.DotNetClaw>("dotnetclaw")
    .WithEnvironment("CoffeeshopCli__Mode", "cli"); // cli or mcp

// Register coffeeshop-cli and force HTTP MCP bridge mode.
// This makes the CLI run as an MCP server instead of terminal command mode.
//var coffeeshop = builder
//	.AddProject<Projects.CoffeeshopCli>("coffeeshop-cli")
//	.WithEnvironment("Hosting__EnableHttpMcpBridge", "true");

builder.Build().Run();
