var builder = DistributedApplication.CreateBuilder(args);

// Foundry Local configuration
var foundryEndpoint = builder.AddParameter("foundry-endpoint", "http://127.0.0.1:55930/v1");
// http://127.0.0.1:55930/v1/models
var foundryModel = builder.AddParameter("foundry-model", "Phi-4-mini-instruct-cuda-gpu:5");

// MCP Tool Server
var mcpToolServer = builder.AddProject<Projects.McpToolServer>("mcp-tools")
    .WithExternalHttpEndpoints();

// Agent Service - connects to MCP Tool Server
var agentService = builder.AddProject<Projects.AgentService>("agentservice")
    .WithReference(mcpToolServer)
    .WithEnvironment("FOUNDRY_ENDPOINT", foundryEndpoint)
    .WithEnvironment("FOUNDRY_MODEL", foundryModel)
    .WithEnvironment("MCP_TOOLS", mcpToolServer.GetEndpoint("http"))
    .WithExternalHttpEndpoints()
    .WaitFor(mcpToolServer);

builder.Build().Run();
