var builder = DistributedApplication.CreateBuilder(args);

// Foundry Local configuration
var foundryEndpoint = builder.AddParameter("foundry-endpoint", "http://127.0.0.1:55930/v1");
var foundryModel = builder.AddParameter("foundry-model", "qwen2.5-14b-instruct-generic-cpu:4");

// MCP Tool Server
var mcpToolServer = builder.AddProject<Projects.McpToolServer>("mcp-tools")
    .WithExternalHttpEndpoints();

// Agent Service - connects to MCP Tool Server
var agentService = builder.AddProject<Projects.AgentService>("agentservice")
    .WithReference(mcpToolServer)
    .WithEnvironment("FOUNDRY_ENDPOINT", foundryEndpoint)
    .WithEnvironment("FOUNDRY_MODEL", foundryModel)
    .WithEnvironment("services__mcp-tools__http__0", mcpToolServer.GetEndpoint("http"))
    .WithExternalHttpEndpoints();

builder.Build().Run();
