# Plan: Integrate coffeeshop-cli into DotNetClaw — Detailed Approaches A & B

**Reference:** [cli_dotnetclaw_bak.md](cli_dotnetclaw_bak.md) (feasibility analysis), DotNetClaw [improve.md](../DotNetClaw/improve.md) (Improvement 2 code templates)

---

## Executive Summary

Two complementary integration paths for coffeeshop-cli → DotNetClaw:

- **Approach A (MCP stdio)**: coffeeshop-cli acts as MCP server. DotNetClaw connects via McpToolLoader. 9 typed tools (list_models, lookup_customer, create_order, etc.) appear natively in the agent.
- **Approach B (MAF Skills + ExecTool)**: Load coffeeshop-counter-service SKILL.md via MAF FileAgentSkillsProvider. Agent follows 4-step agentic loop using ExecTool to shell out to coffeeshop-cli `--json`.

**Recommendation:** Implement B first (fewer prerequisites), add A when MCP infrastructure is ready on both sides.

---

## Approach A: MCP stdio Integration

### Workflow

```
coffeeshop-cli (MCP Server)              DotNetClaw (MCP Client)
────────────────────────                  ────────────────────────
1. `Coffeeshop-Cli mcp serve` starts       4. McpToolLoader reads Mcp:Servers config
   McpServerHost over stdio                  starts coffeeshop-cli as child process
                                             connects via stdin/stdout JSON-RPC
2. Registers 9 MCP tools:               5. ListToolsAsync() discovers 9 tools
   ├─ list_models, show_model             wraps each as AITool via AIFunctionFactory
   ├─ lookup_customer, get_order       6. Merged into agent's List<AITool>
   ├─ order_history, get_menu          7. Agent calls tools via MCP protocol:
   ├─ create_order, update_order          agent → CallToolAsync() → stdio
   └─ get_item_types                      → McpServerHost → handler → response
                                          
3. Tools delegate to:
   ├─ ModelRegistry (reflection)
   └─ McpClientFactory → Python MCP servers
```

### Project Layout

**coffeeshop-cli side** — new MCP server infrastructure:
```
coffeeshop-cli/src/CoffeeshopCli/
├── Commands/
│   └── Mcp/
│       └── McpServeCommand.cs              [NEW] Spectre.Console.Cli command
├── Mcp/
│   ├── McpServerHost.cs                   [NEW] stdio server bootstrap
│   ├── McpClientFactory.cs                [NEW] upstream MCP server connections
│   └── Tools/
│       ├── ModelTools.cs                  [NEW] list_models, show_model
│       ├── OrderTools.cs                  [NEW] lookup_customer, create_order, etc.
│       └── SkillTools.cs                  [NEW] list_skills, show_skill
```

**DotNetClaw side** — MCP client loader:
```
DotNetClaw/DotNetClaw/
├── McpToolLoader.cs                    [NEW] from improve.md Improvement 2
├── DotNetClaw.csproj                   [MODIFY] + ModelContextProtocol NuGet
├── appsettings.json                    [MODIFY] + Mcp:Servers section
└── Program.cs                          [MODIFY] merge MCP tools into agent (line ~55)
```

### Code: McpServerHost.cs

**Location:** `coffeeshop-cli/src/CoffeeshopCli/Mcp/McpServerHost.cs`

Bootstraps the MCP stdio server, registers tool handlers, connects to upstream Python servers.

```csharp
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeshopCli.Mcp;

public static class McpServerHost
{
    /// <summary>
    /// Start MCP server over stdio transport.
    /// Registers tools from ModelTools, OrderTools, SkillTools.
    /// Each tool delegates to upstream Python MCP servers or local services.
    /// </summary>
    public static async Task RunAsync(CancellationToken ct)
    {
        var builder = McpServerApp.CreateBuilder();
        
        builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "coffeeshop-cli",
                Version = "1.0.0"
            };
        })
        .WithStdioTransport()                            // stdio JSON-RPC transport
        .WithTools<ModelTools>()                         // model discovery tools
        .WithTools<OrderTools>()                         // order domain tools
        .WithTools<SkillTools>();                       // skill discovery tools

        // Register shared services for tools
        builder.Services.AddSingleton<ModelRegistry>();
        builder.Services.AddSingleton<McpClientFactory>();
        builder.Services.AddLogging();

        var app = builder.Build();
        await app.RunAsync(ct);
    }
}
```

### Code: OrderTools.cs (example)

**Location:** `coffeeshop-cli/src/CoffeeshopCli/Mcp/Tools/OrderTools.cs`

MCP tool definitions that proxy to upstream Python MCP servers. Each tool:
1. Takes parameters
2. Calls upstream MCP server via McpClientFactory
3. Returns JSON result

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CoffeeshopCli.Mcp.Tools;

[McpServerToolType]
public sealed class OrderTools(McpClientFactory mcpFactory)
{
    [McpServerTool]
    [Description("Look up customer by email or ID. Returns customer record with name, tier, account creation date.")]
    public async Task<string> lookup_customer(
        [Description("Customer email")] string? email = null,
        [Description("Customer ID (format: C-XXXX)")] string? customer_id = null)
    {
        var client = await mcpFactory.GetClientAsync("orders");
        var result = await client.CallToolAsync("lookup_customer",
            new Dictionary<string, object?> { ["email"] = email, ["customer_id"] = customer_id });
        return result.Content.FirstOrDefault()?.Text ?? "{}";
    }

    [McpServerTool]
    [Description("Get all menu items with prices, categories, and descriptions.")]
    public async Task<string> get_menu()
    {
        var client = await mcpFactory.GetClientAsync("orders");
        var result = await client.CallToolAsync("get_menu", new Dictionary<string, object?>());
        return result.Content.FirstOrDefault()?.Text ?? "{}";
    }

    [McpServerTool]
    [Description("Create a new order for a customer. Requires customer_id and order_dto (JSON).")]
    public async Task<string> create_order(
        [Description("Customer ID")] string customer_id,
        [Description("Order DTO as JSON: { items: [{ item_type, qty, price }, ...], total }")] string order_dto)
    {
        var client = await mcpFactory.GetClientAsync("orders");
        var result = await client.CallToolAsync("create_order",
            new Dictionary<string, object?> { ["customer_id"] = customer_id, ["order_dto"] = order_dto });
        return result.Content.FirstOrDefault()?.Text ?? "{}";
    }

    [McpServerTool]
    [Description("Get details for a specific order.")]
    public async Task<string> get_order(string order_id)
    {
        var client = await mcpFactory.GetClientAsync("orders");
        var result = await client.CallToolAsync("get_order",
            new Dictionary<string, object?> { ["order_id"] = order_id });
        return result.Content.FirstOrDefault()?.Text ?? "{}";
    }

    [McpServerTool]
    [Description("List all orders for a customer.")]
    public async Task<string> order_history(string customer_id)
    {
        var client = await mcpFactory.GetClientAsync("orders");
        var result = await client.CallToolAsync("order_history",
            new Dictionary<string, object?> { ["customer_id"] = customer_id });
        return result.Content.FirstOrDefault()?.Text ?? "{}";
    }

    [McpServerTool]
    [Description("Update order status (pending → confirmed → preparing → ready → completed) and/or add a note.")]
    public async Task<string> update_order(
        [Description("Order ID")] string order_id,
        [Description("New status (optional)")] string? status = null,
        [Description("Note to add (optional)")] string? add_note = null)
    {
        var client = await mcpFactory.GetClientAsync("orders");
        var result = await client.CallToolAsync("update_order",
            new Dictionary<string, object?> { ["order_id"] = order_id, ["status"] = status, ["add_note"] = add_note });
        return result.Content.FirstOrDefault()?.Text ?? "{}";
    }
}
```

### Code: McpToolLoader.cs (DotNetClaw)

**Location:** `DotNetClaw/DotNetClaw/McpToolLoader.cs`

From [improve.md](../DotNetClaw/improve.md) Improvement 2. Connects to MCP servers, wraps discovered tools as AITool.

```csharp
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Types;
using Microsoft.Agents.AI;

namespace DotNetClaw;

public static class McpToolLoader
{
    /// <summary>
    /// Connect to MCP servers defined in config and wrap their tools as MAF AITools.
    /// Each MCP tool becomes a callable function in the agent's tool belt.
    /// </summary>
    public static async Task<List<AITool>> LoadAsync(
        IConfiguration config, ILogger logger, CancellationToken ct = default)
    {
        var tools = new List<AITool>();
        var servers = config.GetSection("Mcp:Servers").GetChildren();

        foreach (var server in servers)
        {
            var name = server.Key;
            var transport = server["Transport"] ?? "stdio";

            try
            {
                IMcpClient client = transport switch
                {
                    "stdio" => await McpClientFactory.CreateAsync(
                        new McpClientOptions { ClientInfo = new() { Name = "dotnetclaw" } },
                        new StdioClientTransportOptions
                        {
                            Command = server["Command"]!,
                            Arguments = server.GetSection("Args").Get<string[]>() ?? [],
                        }, ct),

                    "http" => await McpClientFactory.CreateAsync(
                        new McpClientOptions { ClientInfo = new() { Name = "dotnetclaw" } },
                        new SseClientTransportOptions
                        {
                            Endpoint = new Uri(server["Url"]!),
                        }, ct),

                    _ => throw new ArgumentException($"Unknown transport: {transport}")
                };

                var mcpTools = await client.ListToolsAsync(ct);
                foreach (var mcpTool in mcpTools)
                {
                    var toolName = mcpTool.Name;
                    var toolDesc = mcpTool.Description ?? toolName;

                    tools.Add(AIFunctionFactory.Create(
                        async (string input, CancellationToken c) =>
                        {
                            var result = await client.CallToolAsync(
                                toolName,
                                new Dictionary<string, object?> { ["input"] = input }, c);
                            return result.Content.FirstOrDefault()?.Text ?? "";
                        },
                        toolName,
                        toolDesc));

                    logger.LogInformation("[MCP] Registered tool: {Server}/{Tool}", name, toolName);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[MCP] Failed to connect to server: {Name}", name);
                // Non-fatal — other servers and built-in tools still work
            }
        }

        return tools;
    }
}
```

### Code: appsettings.json (DotNetClaw)

**Location:** `DotNetClaw/DotNetClaw/appsettings.json`

Add this section to the existing config:

```json
{
  "Mcp": {
    "Servers": {
      "coffeeshop": {
        "Command": "dotnet",
        "Args": [
          "run",
          "--project", "../coffeeshop-cli/src/CoffeeshopCli",
          "--",
          "mcp", "serve"
        ]
      }
    }
  }
}
```

### Code: Program.cs changes (DotNetClaw)

**Location:** `DotNetClaw/DotNetClaw/Program.cs` (line ~55, after existing MemoryTool registration)

Replace the current `AIAgent` registration (lines 42–83) with:

```csharp
builder.Services.AddSingleton<AIAgent>(sp =>
{
    var mind          = sp.GetRequiredService<MindLoader>();
    var memTool       = sp.GetRequiredService<MemoryTool>();
    var systemMessage = mind.LoadSystemMessageAsync().GetAwaiter().GetResult();
    var startupLog    = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DotNetClaw.Startup");

    // ── Built-in tools (existing) ──
    var tools = new List<AITool>
    {
        AIFunctionFactory.Create(memTool.AppendLogAsync),
        AIFunctionFactory.Create(memTool.AddRuleAsync),
        AIFunctionFactory.Create(memTool.SaveFactAsync),
    };

    // ── MCP tools (NEW) ──
    var mcpTools = McpToolLoader.LoadAsync(
        sp.GetRequiredService<IConfiguration>(), startupLog, default)
        .GetAwaiter().GetResult();
    tools.AddRange(mcpTools);

    // ── Log startup ──
    foreach (var tool in tools.OfType<AIFunction>())
        startupLog.LogInformation("[Agent] Tool registered: {Name}", tool.Name);
    startupLog.LogInformation("[Agent] Total tools: {Count}", tools.Count);

    // ── Create agent ──
    var copilotClient = new CopilotClient(new CopilotClientOptions
    {
        Cwd       = mind.MindRoot,
        AutoStart = true,
        UseStdio  = true,
    });

    return copilotClient.AsAIAgent(
        ownsClient:   true,
        id:           "dotnetclaw",
        name:         "DotNetClaw",
        description:  "Personal AI assistant",
        tools:        tools,              // includes MCP tools
        instructions: systemMessage);
});
```

### Approach A: Runtime Workflow

```
User (Slack): "What's on the coffee menu?"
  │
  ├─ AIAgent identifies need for menu data
  │  calls tool: get_menu()
  │
  ├─ MAF dispatches to AITool (MCP wrapper)
  │  McpClient.CallToolAsync("get_menu", {})
  │
  ├─ Stdin/stdout JSON-RPC to coffeeshop-cli process
  │  `Coffeeshop-Cli mcp serve`
  │
  ├─ McpServerHost → OrderTools.get_menu()
  │  calls McpClientFactory → Python orders.py MCP server
  │
  └─ Response flows back: Python → coffeeshop-cli → DotNetClaw → Agent → Slack
     Agent: "Here's our menu! ☕ Latte $4.50, Cappuccino $4.00..."
```

---

## Approach B: MAF Skills (FileAgentSkillsProvider) + ExecTool

### Workflow

```
DotNetClaw Startup
──────────────────
1. FileAgentSkillsProvider scans skills/ directory
   finds coffeeshop-counter-service/SKILL.md
   parses YAML frontmatter (name, description, metadata)

2. GetContextAsync() returns AIContext:
   - Instructions: "Available skills: coffeeshop-counter-service — Handle coffee orders"
   - Tools: [load_skill(name), read_skill_resource(name, resource)]

3. ExecTool registered: RunAsync(command, workingDirectory?)

4. **Manual AIContext merge** (GitHubCopilotAgent doesn't support AIContextProviders):
   allTools = MemoryTools + ExecTool + SkillTools(load_skill, read_skill_resource)
   systemMessage = mind + skill advertisements

5. AsAIAgent(tools: allTools, instructions: systemMessage)

Runtime (Progressive Disclosure)
────────────────────────────────
6. User: "I want to order coffee"
7. Agent sees skill advertisement → calls load_skill("coffeeshop-counter-service")
8. Gets SKILL.md body (4-step agentic loop instructions)
9. Calls read_skill_resource("coffeeshop-counter-service", "assets/response-templates.md")
10. Follows INTAKE → exec("dotnet run ... --json")
11. Follows CLASSIFY → exec("dotnet run ... --json")
12. Follows REVIEW → exec("dotnet run ... --json")
13. Follows FINALIZE → exec("dotnet run ... --json")
```

### Project Layout

```
DotNetClaw/DotNetClaw/
├── ExecTool.cs                         [NEW] shell execution tool
├── DotNetClaw.csproj                   [MODIFY] MAF rc3 + skills copy-to-output
├── appsettings.json                    [MODIFY] add Exec config section
├── Program.cs                          [MODIFY] wire skills + ExecTool + merge (line ~40)
└── skills/                             [NEW] co-located skills directory
    └── coffeeshop-counter-service/
        ├── SKILL.md                    [NEW] adapted agentic loop
        └── assets/
            └── response-templates.md   [NEW] copied from agent-skills-coffeeshop
```

### Code: ExecTool.cs (DotNetClaw)

**Location:** `DotNetClaw/DotNetClaw/ExecTool.cs`

From [improve.md](../DotNetClaw/improve.md) Improvement 2. Shell execution with safety blocklist.

```csharp
using System.ComponentModel;
using System.Diagnostics;

namespace DotNetClaw;

/// <summary>
/// Execute shell commands with a safety blocklist.
/// Used by agent to run external tools: dotnet, git, curl, system info, etc.
/// </summary>
public sealed class ExecTool
{
    private static readonly string[] Blocked =
        ["rm -rf /", "mkfs", "dd if=", ":(){ :|:& };:", "shutdown", "reboot",
         "format c:", "del /f /s /q"];

    [Description("Execute a shell command and return its output and exit code. " +
                 "Use for: dotnet, git, npm, curl, system info. " +
                 "Dangerous commands (rm -rf /, shutdown, etc.) are blocked.")]
    public async Task<string> RunAsync(
        [Description("The shell command to execute")] string command,
        CancellationToken ct = default)
    {
        // Safety filter
        if (Blocked.Any(b => command.Contains(b, StringComparison.OrdinalIgnoreCase)))
            return "Blocked by safety filter.";

        var isWindows = OperatingSystem.IsWindows();
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : "/bin/bash",
                Arguments = isWindows
                    ? $"/c {command}"
                    : $"-c \"{command.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        proc.Start();

        // Read both streams concurrently to avoid deadlocks
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        await proc.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        var result = $"exit={proc.ExitCode}\n{stdout}";
        if (!string.IsNullOrWhiteSpace(stderr))
            result += $"\nSTDERR:\n{stderr}";

        return result.Length > 8000 ? result[..8000] + "\n[truncated]" : result;
    }
}
```

### Code: DotNetClaw.csproj changes

**Location:** `DotNetClaw/DotNetClaw/DotNetClaw.csproj`

Upgrade MAF and add skills copy-to-output:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>DotNetClaw</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="GitHub.Copilot.SDK" Version="0.1.23" />
    <PackageReference Include="Microsoft.Agents.AI" Version="1.0.0-rc3" />
    <PackageReference Include="Microsoft.Agents.AI.GitHub.Copilot" Version="1.0.0-preview.260304.1" />
    <PackageReference Include="SlackNet" Version="0.17.9" />
    <PackageReference Include="SlackNet.AspNetCore" Version="0.17.9" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.*" />
    <PackageReference Include="Scalar.AspNetCore" Version="2.*" />
  </ItemGroup>

  <!-- Copy skills to output directory -->
  <ItemGroup>
    <None Include="skills/**/*.*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

### Code: appsettings.json (DotNetClaw)

**Location:** `DotNetClaw/DotNetClaw/appsettings.json`

Add this section:

```json
{
  "Exec": {
    "AllowedCommands": ["dotnet"],
    "DefaultWorkingDirectory": "../coffeeshop-cli"
  }
}
```

### Code: Program.cs changes (DotNetClaw) — the key manual AIContext merge

**Location:** `DotNetClaw/DotNetClaw/Program.cs` (line ~40)

Replace the current `AIAgent` registration with:

```csharp
builder.Services.AddSingleton<AIAgent>(sp =>
{
    var mind    = sp.GetRequiredService<MindLoader>();
    var memTool = sp.GetRequiredService<MemoryTool>();
    var startupLog = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DotNetClaw.Startup");

    // ── 1. Built-in tools (memory + execution) ──
    var execTool = new ExecTool();
    var tools = new List<AITool>
    {
        AIFunctionFactory.Create(memTool.AppendLogAsync),
        AIFunctionFactory.Create(memTool.AddRuleAsync),
        AIFunctionFactory.Create(memTool.SaveFactAsync),
        AIFunctionFactory.Create(execTool.RunAsync),         // NEW: shell execution
    };

    // ── 2. Discover skills via MAF FileAgentSkillsProvider ──
    // (GitHubCopilotAgent doesn't natively support AIContextProviders, so manual merge required)
    var skillsDir = Path.Combine(AppContext.BaseDirectory, "skills");
    string? skillInstructions = null;

    if (Directory.Exists(skillsDir))
    {
        var skillsProvider = new FileAgentSkillsProvider(skillPath: skillsDir);
        var skillContext = skillsProvider.GetContextAsync(
            new InvokingContext(), CancellationToken.None).GetAwaiter().GetResult();

        if (skillContext?.Tools != null)
        {
            tools.AddRange(skillContext.Tools);  // adds: load_skill, read_skill_resource
            startupLog.LogInformation("[Skills] Added {Count} skill tools", skillContext.Tools.Count);
        }
        skillInstructions = skillContext?.Instructions;
    }

    // ── 3. Build merged system message ──
    var systemMessage = mind.LoadSystemMessageAsync().GetAwaiter().GetResult();
    if (skillInstructions != null)
        systemMessage += "\n\n" + skillInstructions;

    // ── 4. Log tools ──
    foreach (var tool in tools.OfType<AIFunction>())
        startupLog.LogInformation("[Agent] Tool: {Name}", tool.Name);
    startupLog.LogInformation("[Agent] Total tools: {Count}", tools.Count);

    // ── 5. Create agent (merged tools + instructions) ──
    var copilotClient = new CopilotClient(new CopilotClientOptions
    {
        Cwd       = mind.MindRoot,
        AutoStart = true,
        UseStdio  = true,
    });

    return copilotClient.AsAIAgent(
        ownsClient:   true,
        id:           "dotnetclaw",
        name:         "DotNetClaw",
        description:  "Personal AI assistant",
        tools:        tools,              // merged: memory + exec + skills
        instructions: systemMessage);     // merged: mind + skill advertisements
});
```

### Code: SKILL.md (adapted for ExecTool)

**Location:** `DotNetClaw/DotNetClaw/skills/coffeeshop-counter-service/SKILL.md`

Adapted from `agent-skills-coffeeshop/skills/coffeeshop/SKILL.md`. Tool calls rewritten from `mcp__*` to `exec("dotnet run ... --json")`.

```yaml
---
name: coffeeshop-counter-service
description: >-
  Handle coffee shop customer interactions end-to-end: lookup customers,
  browse the menu, place orders, and check order status.
license: MIT
metadata:
  author: agent-skills-demo
  version: "3.2"
  category: demo
  loop-type: agentic
---

# Coffee Shop Counter Service

## Setup

This skill uses `RunAsync(command)` (ExecTool) to call coffeeshop-cli commands.  
All commands support `--json` for structured JSON output.

### Available Commands

- `Coffeeshop-Cli models list --json`
- `Coffeeshop-Cli models show <name> --json`
- `Coffeeshop-Cli models submit <name> --file <file> --json`

## Agentic Loop

### Internal State

- **CUSTOMER**: Current customer record (null until INTAKE completes)
- **INTENT**: Classified intent from user (null until CLASSIFY completes)
- **ORDER**: Order being built (null until REVIEW starts)

### STEP 1 — INTAKE

Greet the customer and establish their identity.

**Prompt:** "Hi! I'd love to help you with a coffee order. What's your email or customer ID?"

**On user response:**
- Call: `RunAsync("Coffeeshop-Cli models submit Customer --json")`
- Pass user email/ID as JSON {"email": "...", "customer_id": "..."}
- Store result as CUSTOMER
- If not found: "Sorry, I couldn't find an account with that email/ID. Please try again."

### STEP 2 — CLASSIFY INTENT

Determine what the customer wants to do.

**Prompt:** "Hi {CUSTOMER.Name}! What can I help you with today? You can: check an order status, browse the menu, place a new order, or ask about your account."

**On user response, classify into:**
- `order-status`: "Let me look up order {order_id}"
  - Call: `RunAsync("Coffeeshop-Cli models show Order --json")`
  - Display order details
  - Loop back to STEP 2
- `account`: Display account details from CUSTOMER, loop back to STEP 2
- `item-types`: "Let me show you our menu"
  - Call: `RunAsync("Coffeeshop-Cli models list --json")`
  - Display menu
  - Loop back to STEP 2
- `process-order`: Continue to STEP 3

### STEP 3 — REVIEW & CONFIRM

Build order from conversation, get pricing, show summary.

**From conversation, extract:**
- Item types (CAPPUCCINO, LATTE, CROISSANT, etc.)
- Quantities per item

**Call:** `RunAsync("Coffeeshop-Cli models show MenuItem --json")`
- Get prices for selected items
- Calculate total: sum(price * qty)

**Format summary using [assets/response-templates.md](assets/response-templates.md):**
```
☕ 2x Latte — $9.00
🥐 1x Croissant — $3.00
───────────────────
Total: $12.00

Confirm? (yes/no)
```

**On confirmation:**
- Store ORDER = { customer_id: CUSTOMER.CustomerID, items: [...], total: ... }
- Proceed to STEP 4
- On rejection: Loop back to STEP 2

### STEP 4 — FINALIZE

Create order and confirm.

**Call:** `RunAsync("Coffeeshop-Cli models submit Order --json")`
- Pass ORDER as JSON
- Receive created order with OrderID

**Call:** `RunAsync("Coffeeshop-Cli models submit OrderNote --json")`
- Add confirmation note: "Order confirmed by customer"
- Update order status to "confirmed"

**Display confirmation:**
```
Order ORD-1004 confirmed! ☕
Estimated pickup: 10 minutes
```

---

## Response Templates

See [assets/response-templates.md](assets/response-templates.md) for greeting, order summary, and confirmation phrasing.
```

### Approach B: Runtime Workflow — Progressive Disclosure

```
User (Slack): "I want to order coffee"
│
├─ AIAgent sees system prompt: "Available skills: coffeeshop-counter-service"
│  Recognizes intent aligns with skill
│
├─ Calls tool: load_skill("coffeeshop-counter-service")
│  FileAgentSkillsProvider reads SKILL.md
│  Returns full markdown body (4-step agentic loop + state variables)
│
├─ Calls tool: read_skill_resource("coffeeshop-counter-service", "assets/response-templates.md")
│  Returns greeting + order summary + confirmation templates
│
├─ STEP 1 — INTAKE
│  Agent: "Hi! I'd love to help you order. What's your email or customer ID?"
│  User: "C-1001"
│  → exec("dotnet run ... --json") 
│  → coffeeshop-cli → Python MCP server → { name: "Alice", tier: "Gold" }
│
├─ STEP 2 — CLASSIFY INTENT
│  Agent: "Hi Alice! What can I help you with?"
│  User: "I'd like 2 lattes and a croissant"
│  Agent classifies: INTENT = process-order
│
├─ STEP 3 — REVIEW & CONFIRM
│  → exec("dotnet run ... get_items_prices --json")
│  → { LATTE: 4.50, CROISSANT: 3.00 }
│  Agent: "☕ 2x Latte — $9.00 | 🥐 1x Croissant — $3.00 | Total: $12.00. Confirm?"
│  User: "Yes"
│
└─ STEP 4 — FINALIZE
   → exec("dotnet run ... create_order --json")
   → exec("dotnet run ... update_order --json") with status="confirmed"
   Agent: "Order ORD-1004 confirmed! Estimated pickup: 10 minutes. ☕"
```

---

## Comparison

| Dimension | A: MCP stdio | B: MAF Skills + ExecTool |
|:----------|:-------------|:------------------------|
| **Tool surface** | 9 typed MCP tools with input schemas | 1 generic `RunAsync(command)` + 2 skill tools (`load_skill`, `read_skill_resource`) |
| **Agent guidance** | Minimal — agent discovers by schema | Rich — SKILL.md provides 4-step loop, state mgmt, templates |
| **Latency** | Low (persistent stdio pipe) | ~1-2s per `dotnet run` spawn |
| **Type safety** | High (MCP parameter schemas) | Low (string commands) |
| **Prerequisites** | coffeeshop-cli Phase 3 + DotNetClaw McpToolLoader | coffeeshop-cli Phase 1-2 + MAF rc3 upgrade |
| **DotNetClaw code** | `McpToolLoader.cs` (~70 lines) + config + NuGet | `ExecTool.cs` (~50 lines) + `Program.cs` merge (~30 lines) + skills/ dir |
| **coffeeshop-cli code** | MCP server infrastructure (host + 3 tool classes) | None (just needs `--json` commands working) |
| **OpenClaw equivalence** | Option 2 (MCP via exec) | Option 1 (SKILL.md + exec) — direct match |

### Pros & Cons

**Approach A (MCP stdio)**
- **Pros:** Typed schemas, persistent low-latency connection, protocol-level separation, works for any MCP host
- **Cons:** Requires both MCP implementations, no guided agentic loop, agent must infer ordering workflow

**Approach B (MAF Skills + ExecTool)**
- **Pros:** Rich agent guidance (SKILL.md loop), progressive disclosure, mirrors OpenClaw exactly, multi-skill extensible
- **Cons:** Per-command startup overhead, MAF rc3 upgrade risk, manual `AIContext` merging required

---

## Decisions & Rationale

1. **Two approaches, not one:** MCP for raw typed tools + SKILL.md for guided workflows. Complementary, not competing.
2. **Implement B first:** Only needs coffeeshop-cli Phase 1-2 + MAF rc3. Faster path to integration.
3. **Add A when ready:** Once MCP infrastructure is built on both sides, add Approach A for faster, persistent tool access.
4. **Manual AIContext merge required:** `GitHubCopilotAgent` doesn't support `AIContextProviders`. Pattern documented in [cli_dotnetclaw_bak.md](cli_dotnetclaw_bak.md).
5. **ExecTool with allowlist:** Safety-first. Default: `["dotnet"]` only. Dangerous commands (rm -rf /, shutdown, etc.) blocked.
6. **Co-locate SKILL.md in DotNetClaw:** Adapted copy lives in `skills/` dir, not external reference. Simpler deployment.
7. **Excluded:** Copilot SDK native `SessionConfig.SkillDirectories` (bypasses MAF, less visibility). Direct C# project reference (too tightly coupled).

---

## Summary

Both approaches are viable. **Start with Approach B** (faster, fewer dependencies), then add **Approach A** when ready for scalability. The result: a flexible coffeeshop-cli → DotNetClaw integration that works at multiple levels (guided workflows + raw typed tools) and mirrors the OpenClaw integration strategy exactly.
