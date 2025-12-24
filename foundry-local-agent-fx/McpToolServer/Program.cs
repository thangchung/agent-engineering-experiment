using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Add MCP server with HTTP transport
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
app.MapDefaultEndpoints();

// Map MCP endpoints at /mcp
app.MapMcp("/mcp");

app.Run();

// Make Program accessible for Aspire
public partial class Program { }
