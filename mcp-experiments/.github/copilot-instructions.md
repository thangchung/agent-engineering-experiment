# Copilot Instructions

This is a .NET/C# MCP server experiment combining three advanced patterns: **tool-search** (dynamic tool discovery), **code-mode** (LLM-driven code execution), and **sandbox** (isolated execution via OpenSandbox). The goal is to explore how these patterns reduce token cost and improve tool-use accuracy for large tool catalogs.

## Architecture

The server exposes a large set of MCP tools but wraps them with two transforms:

1. **Tool Search** — Instead of listing all tools upfront, the server exposes `search_tools` and `call_tool` meta-tools. The LLM searches the catalog on demand (BM25 or regex), then invokes the real tool via the `call_tool` proxy. This mirrors [FastMCP's search transforms](https://gofastmcp.com/servers/transforms/tool-search) and [Spring AI's dynamic tool search](https://docs.spring.io/spring-ai/reference/2.0/guides/dynamic-tool-search.html).

2. **Code Mode** — Instead of round-tripping one tool per LLM turn, the server exposes meta-tools for discovery (`search`, `get_schema`) and execution (`execute`). The LLM writes a small C# or Python script that chains multiple tool calls in a sandbox and returns only the final result. This mirrors [FastMCP's CodeMode transform](https://gofastmcp.com/servers/transforms/code-mode).

3. **Sandbox** — Code execution runs inside an isolated [OpenSandbox](https://github.com/alibaba/OpenSandbox) environment via the `Alibaba.OpenSandbox` NuGet package. The sandbox is created on-demand, used for a single script execution, then terminated.

```
MCP Client
    │
    ▼
[MCP Server (stdio or streamable-http)]
    │
    ├── Transform: ToolSearch (search_tools / call_tool)
    └── Transform: CodeMode  (search / get_schema / execute)
                                  │
                                  ▼
                        [OpenSandbox (isolated exec)]
                                  │
                                  ▼
                        [Real MCP Tools]
```

## Key Packages

| Package | Purpose |
|---|---|
| `ModelContextProtocol` | Core MCP server + DI hosting |
| `ModelContextProtocol.AspNetCore` | Only if using HTTP transport |
| `Alibaba.OpenSandbox` | Sandbox lifecycle, command execution, file I/O |

## Build, Test, and Lint

```bash
# Build
dotnet build

# Run the MCP server
dotnet run

# Run all tests
dotnet test

# Run a single test by name (filter is a substring of the test's fully-qualified name)
dotnet test --filter "FullyQualifiedName~MyTest"

# Lint / format check
dotnet format --verify-no-changes

# Auto-fix formatting
dotnet format
```

## MCP Server Setup Pattern (C# SDK)

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()   // or .WithHttpTransport() for streamable-http
    .WithToolsFromAssembly();     // auto-discovers [McpServerTool] classes

var app = builder.Build();
await app.RunAsync();
```

Tool classes:
```csharp
[McpServerToolType]
public class MyTools
{
    [McpServerTool, Description("Brief, precise description the LLM reads to decide whether to invoke.")]
    public static async Task<string> my_tool_name(
        [Description("What this param does")] string param1,
        CancellationToken ct)
    {
        // ...
    }
}
```

## Tool Search Pattern

The `search_tools` / `call_tool` meta-tools are implemented as a pair of `[McpServerTool]` methods that:
- `search_tools(query)` — searches a BM25 or regex index built from tool names + descriptions + parameter names, returns matching tool schemas.
- `call_tool(name, arguments)` — dispatches to the real tool by name (via `IMcpServer` or a local registry).

Real tools are **not** registered directly with the MCP server; they are registered in an internal `ToolRegistry` and surfaced only through the search/call proxy.

## Code Mode Pattern

`execute(code)` takes a C# or Python script string and runs it inside an OpenSandbox instance. The sandbox has the tool registry pre-populated so that scripts can call `ToolClient.Call("tool_name", args)` directly. Execution flow:

1. LLM calls `search(query)` → gets tool names + brief descriptions.
2. LLM calls `get_schema(toolNames[])` → gets full parameter schemas.
3. LLM calls `execute(code)` → sandbox runs the script and returns stdout.

## Sandbox Pattern

Use `await using` to ensure the sandbox is always terminated:

```csharp
var config = new ConnectionConfig(new ConnectionConfigOptions
{
    Domain  = Environment.GetEnvironmentVariable("SANDBOX_DOMAIN")!,
    ApiKey  = Environment.GetEnvironmentVariable("SANDBOX_API_KEY")!,
});

await using var sandbox = await Sandbox.CreateAsync(new SandboxCreateOptions
{
    ConnectionConfig = config,
    Image            = "ubuntu",
    TimeoutSeconds   = 5 * 60,
});

var result = await sandbox.Commands.RunAsync(code);
await sandbox.KillAsync();
```

Environment variables required: `SANDBOX_DOMAIN`, `SANDBOX_API_KEY`.

## Conventions

- **Tool naming**: `snake_case` verbs — `search_tools`, `call_tool`, `execute_code`.
- **Descriptions are the interface**: every tool and parameter `[Description(...)]` is what the LLM reads. Make them precise and action-oriented.
- **Transport default**: `stdio` for local dev; switch to `streamable-http` for multi-client scenarios.
- **Sandbox is ephemeral**: create one per `execute` call, kill it when done. Never reuse across requests.
- **ToolRegistry is the source of truth**: real tools are registered there, not directly with the MCP server.
- **Always pin tools that need to be visible upfront** (e.g., `help`, `search_tools`) so they appear in `list_tools` without requiring a search step.

# Principles

- KISS/YAGNI/DRY/Boy-Scout principles apply. Implement the simplest thing that could possibly work to explore these patterns. Avoid over-engineering or adding features not required by the core experiments.
- TDD red-green cycle for all new code. Write tests first, watch them fail, then implement the minimum code to make them pass.
