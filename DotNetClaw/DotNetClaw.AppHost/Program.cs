var builder = DistributedApplication.CreateBuilder(args);

// Register the main DotNetClaw project as a service named "dotnetclaw".
// Aspire will launch it, inject ASPNETCORE_URLS, and expose it in the dashboard.
var app = builder.AddProject<Projects.DotNetClaw>("dotnetclaw");

builder.Build().Run();
