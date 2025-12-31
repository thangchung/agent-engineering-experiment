var builder = DistributedApplication.CreateBuilder(args);

// =============================================================================
// Provider Configuration
// =============================================================================
// Active provider: "FoundryLocal" or "Ollama"
var activeProvider = builder.AddParameter("agent-provider", "Ollama");

// Foundry Local configuration
var foundryEndpoint = builder.AddParameter("foundry-endpoint", "http://127.0.0.1:58250/v1");
var foundryModel = builder.AddParameter("foundry-model", "qwen2.5-14b-instruct-generic-cpu:4");

// Ollama configuration
var ollamaEndpoint = builder.AddParameter("ollama-endpoint", "http://localhost:11434/");
var ollamaModel = builder.AddParameter("ollama-model", "mistral"); //deepseek-v3.1:671b-cloud gpt-oss:20b llama3.2:3b

// =============================================================================
// MCP Tool Server
// =============================================================================
var mcpToolServer = builder.AddProject<Projects.McpToolServer>("mcp-tools")
    .WithExternalHttpEndpoints();

// =============================================================================
// Agent Service - connects to MCP Tool Server
// =============================================================================
var agentService = builder.AddProject<Projects.AgentService>("agentservice")
    .WithReference(mcpToolServer)
    // Provider selection
    .WithEnvironment("AGENT_PROVIDER", activeProvider)
    // Foundry Local settings
    .WithEnvironment("FOUNDRY_ENDPOINT", foundryEndpoint)
    .WithEnvironment("FOUNDRY_MODEL", foundryModel)
    // Ollama settings
    .WithEnvironment("OLLAMA_ENDPOINT", ollamaEndpoint)
    .WithEnvironment("OLLAMA_MODEL", ollamaModel)
    // MCP Tools
    .WithEnvironment("MCP_TOOLS", mcpToolServer.GetEndpoint("http"))
    .WithExternalHttpEndpoints()
    .WaitFor(mcpToolServer);

builder.Build().Run();
