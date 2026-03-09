# Plan: Move SKILL.md to coffeeshop-cli, Expose to DotNetClaw

## TL;DR

Move the `coffeeshop-counter-service` SKILL.md from `agent-skills-coffeeshop` into coffeeshop-cli's `skills/` directory, making coffeeshop-cli the **single source of truth** for skill discovery. coffeeshop-cli discovers and serves skills via `skills list/show` commands and MCP tools. DotNetClaw consumes them through ExecTool first (Approach B), then MCP (Approach A). Skill instructions reference coffeeshop-cli `--json` commands.

## What Changes vs. Current cli_dotnetclaw.md

| Aspect | Current (Approach B) | New Plan |
|--------|---------------------|----------|
| SKILL.md location | `DotNetClaw/skills/` | `coffeeshop-cli/skills/` |
| Skill discovery | DotNetClaw FileAgentSkillsProvider | coffeeshop-cli discovers; DotNetClaw fetches via CLI/MCP |
| Skill duplication | Each consumer has adapted copy | Single source; consumers fetch it |
| DotNetClaw code | FileAgentSkillsProvider + ExecTool + AIContext merge | ExecTool only → later MCP |

---

## Phase 1 — Host Skill in coffeeshop-cli

### Step 1: Create skills directory structure

**Files to create:**
```
coffeeshop-cli/skills/coffeeshop-counter-service/
├── SKILL.md
└── assets/
    └── response-templates.md
```

**response-templates.md** — copy verbatim from `agent-skills-coffeeshop/skills/coffeeshop/assets/response-templates.md`:

```markdown
# Response Templates

## Greeting
Hi {customer_name}, thanks for reaching out! I'm here to help.
I've pulled up your account — let me take a look at what's going on.

## Order Summary
📋 Order Summary — {order_id}
Items:
{items_list}
Total: ${total}
Does this look correct?

Placeholders:
- {items_list} — Format each item as: "- {qty}x {item_name} — ${price} each"
```

### Step 2: Adapt SKILL.md for coffeeshop-cli

**Source:** `agent-skills-coffeeshop/skills/coffeeshop/SKILL.md` (v3.1, 291 lines)

**Adapted SKILL.md** — full content for `coffeeshop-cli/skills/coffeeshop-counter-service/SKILL.md`:

```yaml
---
name: coffeeshop-counter-service
description: >
  Handle coffee shop order submissions end-to-end: receive customer requests,
  check available menu items, create and confirm the order, process updates
  or special instructions, and escalate to a human staff member when necessary.
license: MIT
compatibility: >
  Requires coffeeshop-cli with --json support. The CLI proxies to upstream
  Python MCP servers (orders, product_catalogs) for data.
metadata:
  author: agent-skills-demo
  version: "3.2"
  category: demo
  loop-type: agentic
---
```

```markdown
# Coffee Shop Order Submission Skill

You are a counter service agent. Follow the Agentic Loop below exactly.

## Setup

This skill uses coffeeshop-cli commands with `--json` output. All commands
are prefixed with `dotnet run --project <cli-path> --`.

### Available Commands

**Customer & Account:**
- `dotnet run -- models query Customer --email <email> --json` — lookup customer by email
  Example: `dotnet run -- models query Customer --email alice@example.com --json`
- `dotnet run -- models query Customer --customer-id <id> --json` — lookup customer by ID
  Example: `dotnet run -- models query Customer --customer-id C-1001 --json`
- `dotnet run -- models browse Customer --json` — list all customers

**Product Catalog:**
- `dotnet run -- models browse MenuItem --json` — list all menu items with prices

**Orders:**
- `dotnet run -- models submit Order --json` — create new order
  Input: `{"customer_id":"C-1001","items":[{"item_type":"LATTE","qty":2}]}`
  Returns: Complete order object with generated OrderId and status="Pending"

For response phrasing, see assets/response-templates.md (inlined below in each step).

## Agentic Loop

### Internal State

| Variable | Initial | Description |
|----------|---------|-------------|
| CUSTOMER | null | Customer record from lookup |
| INTENT | null | order-status / account / item-types / process-order |
| ORDER | null | Order being built or looked up |

### STEP 1 — INTAKE

Goal: Greet the customer and identify who they are.

1. Extract identifiers from the customer's message (email, customer_id, order_id).
2. IF email provided:
   - Call: `dotnet run -- models query Customer --email <email> --json`
   - Store result as CUSTOMER.
3. IF customer_id provided:
   - Call: `dotnet run -- models query Customer --customer-id <id> --json`
   - Store result as CUSTOMER.
4. IF only order_id provided:
   - Display order status from local cache or ask agent to clarify.
   - Extract customer_id, then call lookup as above.
   - Store both CUSTOMER and ORDER.
5. IF no identifier: ask the customer, wait, repeat step 1.
6. IF lookup returns empty/error: ask customer to try again.
7. Greet: "Hi {CUSTOMER.Name}, thanks for reaching out!"

GOTO → STEP 2

### STEP 2 — CLASSIFY INTENT

Goal: Determine what the customer needs.

1. Classify into: order-status | account | item-types | process-order
2. Store in INTENT.

Route:
- **order-status**: Lookup order_id from conversation context (coffeeshop-cli does not yet expose order queries).
  Display order details from conversation memory. GOTO → STEP 2 (loop).
- **account**: Display CUSTOMER details. GOTO → STEP 2 (loop).
- **item-types**: Call `dotnet run -- models browse MenuItem --json`
  Display menu. GOTO → STEP 2 (loop).
- **process-order**: GOTO → STEP 3.

### STEP 3 — REVIEW & CONFIRM ORDER

Goal: Build, price, and confirm the order.

1. Extract items + quantities from conversation.
2. Call: `dotnet run -- models browse MenuItem --json`
   Get prices for selected items.
3. Calculate total = sum(price × qty).
4. Display summary:
   ```
   📋 Order Summary
   - 2x Latte — $4.50 each
   - 1x Croissant — $3.25 each
   Total: $12.25
   Does this look correct?
   ```
5. IF confirmed → store ORDER, GOTO STEP 4.
6. IF rejected → ask what to change, GOTO STEP 2.

### STEP 4 — FINALIZE ORDER

Goal: Create order and confirm.

1. Call: `dotnet run -- models submit Order --json`
   Body: `{"customer_id":"<CUSTOMER.CustomerId>","items":[{"item_type":"LATTE","qty":2}]}`
   Note: Price and total are auto-calculated. OrderId is auto-generated.
   Store result (includes OrderId, Status=Pending, Total).
2. Display: "Order {ORDER.OrderId} created! ☕ Total: ${ORDER.Total}. Estimated pickup: 5-10 min (beverages) / 10-15 min (with food)."

GOTO → END
```

**Key differences from original:**
- Removed `allowed-tools: mcp__orders__* mcp__product_items__*`
- Removed `open_order_form` (MCP Apps UI) — agent constructs order JSON directly
- All tool calls → `dotnet run -- <command> --json`
- Version bumped `3.1` → `3.2`
- Response templates inlined into step instructions

### Step 3: Discovery config

**Workflow:**
```
coffeeshop-cli startup
└─ FileSystemDiscoveryService.DiscoverSkills()
   └─ scans config.skills_directory (default: ./skills/)
      └─ glob: */SKILL.md
         └─ finds: skills/coffeeshop-counter-service/SKILL.md
            └─ returns: [ SkillInfo { path, name, ... } ]
```

**Code:** `src/CoffeeshopCli/Services/FileSystemDiscoveryService.cs`

```csharp
public class FileSystemDiscoveryService : IDiscoveryService
{
    private readonly string _skillsDirectory;

    public FileSystemDiscoveryService(string skillsDirectory)
    {
        _skillsDirectory = skillsDirectory;
    }

    public IReadOnlyList<SkillInfo> DiscoverSkills()
    {
        var skills = new List<SkillInfo>();
        if (!Directory.Exists(_skillsDirectory))
            return skills;

        foreach (var skillDir in Directory.GetDirectories(_skillsDirectory))
        {
            var skillFile = Path.Combine(skillDir, "SKILL.md");
            if (File.Exists(skillFile))
            {
                var parser = new SkillParser();
                var manifest = parser.Parse(File.ReadAllText(skillFile));
                skills.Add(new SkillInfo
                {
                    Name = manifest.Frontmatter.Name,
                    Description = manifest.Frontmatter.Description,
                    Version = manifest.Frontmatter.Metadata.Version,
                    Category = manifest.Frontmatter.Metadata.Category,
                    LoopType = manifest.Frontmatter.Metadata.LoopType,
                    Path = skillFile,
                });
            }
        }
        return skills;
    }
}
```

---

## Phase 2 — coffeeshop-cli Skill Commands (*parallel with Phase 1*)

### Step 4: `skills list --json`

**Workflow:**
```
$ dotnet run -- skills list --json
                │
  SkillsListCommand.ExecuteAsync()
                │
  ┌─────────────▼─────────────┐
  │ IDiscoveryService          │
  │   .DiscoverSkills()        │
  │   scans skills/*/SKILL.md  │
  └─────────────┬─────────────┘
                │
  ┌─────────────▼─────────────┐
  │ SkillParser.Parse(content) │
  │   extracts YAML frontmatter│
  └─────────────┬─────────────┘
                │
  ┌──────┬──────┘
  │      │
  ▼ TUI  ▼ JSON (--json)
┌──────────────┐  ┌───────────────────────────┐
│ Spectre Table│  │ [                         │
│ Name | Desc  │  │   { "name": "coffeeshop.. │
│ Ver  | Loop  │  │     "version": "3.2",     │
└──────────────┘  │     "loop_type":"agentic" }│
                  │ ]                         │
                  └───────────────────────────┘
```

**Code:** `src/CoffeeshopCli/Services/SkillParser.cs`

```csharp
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CoffeeshopCli.Services;

public record SkillFrontmatter
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string License { get; init; } = "";
    public string Compatibility { get; init; } = "";
    public SkillMetadata Metadata { get; init; } = new();
}

public record SkillMetadata
{
    public string Author { get; init; } = "";
    public string Version { get; init; } = "";
    public string Category { get; init; } = "";
    [YamlMember(Alias = "loop-type")]
    public string LoopType { get; init; } = "";
}

public record SkillManifest
{
    public SkillFrontmatter Frontmatter { get; init; } = new();
    public string Body { get; init; } = "";
}

public class SkillParser
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Parse a SKILL.md file: extract YAML frontmatter between --- delimiters,
    /// deserialize it, and return the remaining markdown body.
    /// </summary>
    public SkillManifest Parse(string content)
    {
        var lines = content.Split('\n');
        if (lines.Length < 2 || lines[0].Trim() != "---")
            return new SkillManifest { Body = content };

        var endIndex = -1;
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                endIndex = i;
                break;
            }
        }

        if (endIndex < 0)
            return new SkillManifest { Body = content };

        var yamlBlock = string.Join('\n', lines[1..endIndex]);
        var body = string.Join('\n', lines[(endIndex + 1)..]).TrimStart();

        var frontmatter = YamlDeserializer.Deserialize<SkillFrontmatter>(yamlBlock);
        return new SkillManifest { Frontmatter = frontmatter, Body = body };
    }
}
```

**Code:** `src/CoffeeshopCli/Commands/Skills/SkillsListCommand.cs`

```csharp
using Spectre.Console;
using Spectre.Console.Cli;

namespace CoffeeshopCli.Commands.Skills;

public sealed class SkillsListSettings : CommandSettings
{
    [CommandOption("--json")]
    public bool Json { get; init; }
}

public sealed class SkillsListCommand : Command<SkillsListSettings>
{
    private readonly IDiscoveryService _discovery;

    public SkillsListCommand(IDiscoveryService discovery) => _discovery = discovery;

    public override int Execute(CommandContext context, SkillsListSettings settings)
    {
        var skills = _discovery.DiscoverSkills();

        if (settings.Json)
        {
            var output = skills.Select(s => new
            {
                s.Name, s.Description, s.Version, s.Category, s.LoopType
            });
            AnsiConsole.WriteLine(
                System.Text.Json.JsonSerializer.Serialize(output,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        var table = new Table()
            .AddColumn("Name")
            .AddColumn("Description")
            .AddColumn("Version")
            .AddColumn("Loop Type");

        foreach (var skill in skills)
            table.AddRow(
                skill.Name,
                skill.Description.Length > 60 ? skill.Description[..60] + "…" : skill.Description,
                skill.Version,
                skill.LoopType);

        AnsiConsole.Write(table);
        return 0;
    }
}
```

### Step 5: `skills show <name> --json`

**Workflow:**
```
$ dotnet run -- skills show coffeeshop-counter-service --json
                │
  SkillsShowCommand.ExecuteAsync()
                │
  ┌─────────────▼──────────────┐
  │ IDiscoveryService           │
  │   .DiscoverSkills()         │
  │   find by name match        │
  └─────────────┬──────────────┘
                │
  ┌─────────────▼──────────────┐
  │ SkillParser.Parse(content)  │
  │   full: frontmatter + body  │
  └─────────────┬──────────────┘
                │
  ┌──────┬──────┘
  ▼ TUI  ▼ JSON
┌─────────────────┐  ┌──────────────────────────────────┐
│ Spectre Panel    │  │ {                                │
│ ┌──────────────┐ │  │   "frontmatter": {               │
│ │ Frontmatter  │ │  │     "name": "coffeeshop-...",    │
│ │ ────────── │ │  │     "version": "3.2", ...        │
│ │ Body (md)    │ │  │   },                             │
│ └──────────────┘ │  │   "body": "# Coffee Shop..."    │
└─────────────────┘  │ }                                │
                     └──────────────────────────────────┘
```

**Code:** `src/CoffeeshopCli/Commands/Skills/SkillsShowCommand.cs`

```csharp
using Spectre.Console;
using Spectre.Console.Cli;

namespace CoffeeshopCli.Commands.Skills;

public sealed class SkillsShowSettings : CommandSettings
{
    [CommandArgument(0, "<name>")]
    public required string Name { get; init; }

    [CommandOption("--json")]
    public bool Json { get; init; }
}

public sealed class SkillsShowCommand : Command<SkillsShowSettings>
{
    private readonly IDiscoveryService _discovery;

    public SkillsShowCommand(IDiscoveryService discovery) => _discovery = discovery;

    public override int Execute(CommandContext context, SkillsShowSettings settings)
    {
        var skills = _discovery.DiscoverSkills();
        var skill = skills.FirstOrDefault(s =>
            s.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

        if (skill is null)
        {
            AnsiConsole.MarkupLine($"[red]Skill not found:[/] {settings.Name}");
            return 1;
        }

        var content = File.ReadAllText(skill.Path);
        var manifest = new SkillParser().Parse(content);

        if (settings.Json)
        {
            var output = new
            {
                frontmatter = new
                {
                    manifest.Frontmatter.Name,
                    manifest.Frontmatter.Description,
                    manifest.Frontmatter.License,
                    manifest.Frontmatter.Compatibility,
                    manifest.Frontmatter.Metadata,
                },
                body = manifest.Body
            };
            AnsiConsole.WriteLine(
                System.Text.Json.JsonSerializer.Serialize(output,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        // TUI: Panel with frontmatter header + body
        var panel = new Panel(
            new Markup(
                $"[bold]{manifest.Frontmatter.Name}[/] v{manifest.Frontmatter.Metadata.Version}\n" +
                $"[dim]{manifest.Frontmatter.Description}[/]\n\n" +
                $"[yellow]Loop:[/] {manifest.Frontmatter.Metadata.LoopType}  " +
                $"[yellow]Category:[/] {manifest.Frontmatter.Metadata.Category}\n\n" +
                $"───────────────────\n\n" +
                Markup.Escape(manifest.Body)))
        {
            Header = new PanelHeader($" {manifest.Frontmatter.Name} "),
            Border = BoxBorder.Rounded,
        };
        AnsiConsole.Write(panel);
        return 0;
    }
}
```

**Key:** This is the entry point DotNetClaw uses to fetch skill manifests. The JSON output includes the full `body` (agentic loop instructions) that the agent follows.

---

## Phase 3 — coffeeshop-cli MCP Skill Tools (*depends on Phase 2*)

### Step 6: Expose `list_skills` / `show_skill` on MCP server

**Workflow:**
```
DotNetClaw (MCP Client)                 coffeeshop-cli (MCP Server)
───────────────────                     ────────────────────────
McpToolLoader.LoadAsync()               McpServerHost.RunAsync()
  │                                       │
  ├─ ListToolsAsync()  ──stdio──►        Discovers 11 tools:
  │                                       ├─ list_models, show_model     (ModelTools)
  │                                       ├─ lookup_customer, ...        (OrderTools)
  │                                       ├─ [list_skills]               (SkillTools) ◄ NEW
  │                                       └─ [show_skill]                (SkillTools) ◄ NEW
  │
  ├─ CallToolAsync("list_skills")
  │   ──stdio──►  SkillTools.list_skills()
  │               └─ DiscoveryService.DiscoverSkills()
  │               └─ returns JSON array
  │
  └─ CallToolAsync("show_skill", {name: "coffeeshop-counter-service"})
      ──stdio──►  SkillTools.show_skill(name)
                  └─ SkillParser.Parse(SKILL.md)
                  └─ returns { frontmatter, body }
```

**Code:** `src/CoffeeshopCli/Mcp/Tools/SkillTools.cs`

```csharp
using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace CoffeeshopCli.Mcp.Tools;

[McpServerToolType]
public sealed class SkillTools(IDiscoveryService discovery)
{
    [McpServerTool]
    [Description("List all available agent skills. Returns name, description, version, loop-type for each skill.")]
    public string list_skills()
    {
        var skills = discovery.DiscoverSkills();
        var output = skills.Select(s => new
        {
            s.Name, s.Description, s.Version, s.Category, s.LoopType
        });
        return JsonSerializer.Serialize(output,
            new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool]
    [Description("Get the full skill manifest (YAML frontmatter + markdown body with agentic loop instructions) by name.")]
    public string show_skill(
        [Description("Skill name (e.g., coffeeshop-counter-service)")] string name)
    {
        var skills = discovery.DiscoverSkills();
        var skill = skills.FirstOrDefault(s =>
            s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (skill is null)
            return JsonSerializer.Serialize(new { error = "Skill not found", name });

        var content = File.ReadAllText(skill.Path);
        var manifest = new SkillParser().Parse(content);

        return JsonSerializer.Serialize(new
        {
            frontmatter = manifest.Frontmatter,
            body = manifest.Body
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
```

**McpServerHost.cs update** — add `SkillTools` registration:

```csharp
builder.Services.AddMcpServer(options => { ... })
    .WithStdioTransport()
    .WithTools<ModelTools>()
    .WithTools<OrderTools>()
    .WithTools<SkillTools>();           // ◄ NEW

builder.Services.AddSingleton<IDiscoveryService>(
    new FileSystemDiscoveryService("./skills"));  // ◄ needed for SkillTools
```

---

## Phase 4 — Update DotNetClaw Integration (*depends on Phase 2*)

### Step 7: Approach B — ExecTool-based skill loading (replaces FileAgentSkillsProvider)

**Workflow:**
```
DotNetClaw Startup (revised)
────────────────────────────
1. Register ExecTool

2. At startup, discover skills via exec:
   ExecTool.RunAsync("dotnet run --project ../coffeeshop-cli -- skills list --json")
   ──spawn──► coffeeshop-cli → returns JSON skill catalog
   Result: [{ name: "coffeeshop-counter-service", description: "...", ... }]

3. Inject skill advertisements into system prompt:
   systemMessage += "\n\nAvailable skills:\n- coffeeshop-counter-service: Handle coffee orders"

4. Register SkillLoaderTool (calls coffeeshop-cli to fetch full manifests)

5. allTools = MemoryTools + ExecTool + SkillLoaderTool

Runtime (Progressive Disclosure)
────────────────────────────────
6. User: "I want to order coffee"
7. Agent sees skill advertisement in system prompt
   → calls load_skill("coffeeshop-counter-service")

8. SkillLoaderTool.LoadAsync("coffeeshop-counter-service")
   → ExecTool.RunAsync("dotnet run --project ../coffeeshop-cli -- skills show coffeeshop-counter-service --json")
   → returns { frontmatter: {...}, body: "# Coffee Shop Order..." }
   → agent receives full agentic loop instructions

9. Agent follows STEP 1 — INTAKE
   → ExecTool.RunAsync("dotnet run --project ../coffeeshop-cli -- models submit Customer --json")
   → coffeeshop-cli → Python MCP server → { name: "Alice", tier: "Gold" }

10. Agent follows STEP 2 — CLASSIFY INTENT
    User: "2 lattes and a croissant"
    → INTENT = process-order

11. Agent follows STEP 3 — REVIEW
    → ExecTool.RunAsync("dotnet run --project ../coffeeshop-cli -- models show MenuItem --json")
    → prices resolved, summary shown

12. Agent follows STEP 4 — FINALIZE
    → ExecTool.RunAsync("dotnet run --project ../coffeeshop-cli -- models submit Order --json")
    → Order ORD-1004 created, status confirmed
```

**Code: ExecTool.cs** (unchanged from current cli_dotnetclaw.md)

```csharp
using System.ComponentModel;
using System.Diagnostics;

namespace DotNetClaw;

public sealed class ExecTool
{
    private static readonly string[] Blocked =
        ["rm -rf /", "mkfs", "dd if=", ":(){ :|:& };:", "shutdown", "reboot",
         "format c:", "del /f /s /q"];

    [Description("Execute a shell command and return its output. " +
                 "Use for: dotnet, git, npm, curl, system info.")]
    public async Task<string> RunAsync(
        [Description("The shell command to execute")] string command,
        CancellationToken ct = default)
    {
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

**Code: SkillLoaderTool.cs** (NEW — replaces FileAgentSkillsProvider)

```csharp
using System.ComponentModel;
using System.Text.Json;

namespace DotNetClaw;

/// <summary>
/// Loads skill manifests from coffeeshop-cli via ExecTool.
/// Replaces MAF FileAgentSkillsProvider — no local skills/ dir needed.
/// </summary>
public sealed class SkillLoaderTool
{
    private readonly ExecTool _exec;
    private readonly string _cliProject;

    public SkillLoaderTool(ExecTool exec, string cliProject)
    {
        _exec = exec;
        _cliProject = cliProject;
    }

    [Description("Load a skill's full manifest (agentic loop instructions) by name. " +
                 "Returns the skill body with step-by-step instructions the agent should follow.")]
    public async Task<string> LoadSkillAsync(
        [Description("Skill name (e.g., coffeeshop-counter-service)")] string name,
        CancellationToken ct = default)
    {
        var result = await _exec.RunAsync(
            $"dotnet run --project {_cliProject} -- skills show {name} --json", ct);

        // Extract JSON from exec output (skip "exit=0\n" prefix)
        var jsonStart = result.IndexOf('{');
        if (jsonStart < 0) return result;
        var json = result[jsonStart..];

        // Return just the body (agentic loop instructions) for the agent to follow
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("body", out var body))
            return body.GetString() ?? json;
        return json;
    }
}
```

**Code: Program.cs** (DotNetClaw — revised, replaces current lines ~42-83)

```csharp
builder.Services.AddSingleton<AIAgent>(sp =>
{
    var mind    = sp.GetRequiredService<MindLoader>();
    var memTool = sp.GetRequiredService<MemoryTool>();
    var startupLog = sp.GetRequiredService<ILoggerFactory>()
        .CreateLogger("DotNetClaw.Startup");

    // ── 1. Tools: memory + exec + skill loader ──
    var execTool = new ExecTool();
    var cliProject = "../coffeeshop-cli/src/CoffeeshopCli";
    var skillLoader = new SkillLoaderTool(execTool, cliProject);

    var tools = new List<AITool>
    {
        AIFunctionFactory.Create(memTool.AppendLogAsync),
        AIFunctionFactory.Create(memTool.AddRuleAsync),
        AIFunctionFactory.Create(memTool.SaveFactAsync),
        AIFunctionFactory.Create(execTool.RunAsync),          // shell execution
        AIFunctionFactory.Create(skillLoader.LoadSkillAsync),  // load skill from CLI
    };

    // ── 2. Discover skills at startup via CLI ──
    string? skillInstructions = null;
    try
    {
        var listResult = execTool.RunAsync(
            $"dotnet run --project {cliProject} -- skills list --json",
            default).GetAwaiter().GetResult();

        var jsonStart = listResult.IndexOf('[');
        if (jsonStart >= 0)
        {
            var json = listResult[jsonStart..];
            using var doc = JsonDocument.Parse(json);
            var skills = doc.RootElement.EnumerateArray()
                .Select(e => $"- {e.GetProperty("name").GetString()}: " +
                             $"{e.GetProperty("description").GetString()}")
                .ToList();

            if (skills.Count > 0)
            {
                skillInstructions = "\n\nAvailable skills (call load_skill to activate):\n"
                    + string.Join("\n", skills);
                startupLog.LogInformation("[Skills] Discovered {Count} skills", skills.Count);
            }
        }
    }
    catch (Exception ex)
    {
        startupLog.LogWarning(ex, "[Skills] Failed to discover skills from coffeeshop-cli");
        // Non-fatal — agent works without skills
    }

    // ── 3. Build system message ──
    var systemMessage = mind.LoadSystemMessageAsync().GetAwaiter().GetResult();
    if (skillInstructions != null)
        systemMessage += skillInstructions;

    // ── 4. Log & create agent ──
    foreach (var tool in tools.OfType<AIFunction>())
        startupLog.LogInformation("[Agent] Tool: {Name}", tool.Name);
    startupLog.LogInformation("[Agent] Total tools: {Count}", tools.Count);

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
        tools:        tools,
        instructions: systemMessage);
});
```

**What's removed vs. current cli_dotnetclaw.md Approach B:**
- `DotNetClaw/skills/` directory — gone
- `FileAgentSkillsProvider` — gone, replaced by ExecTool calling `skills list --json`
- Manual AIContext merge (`skillContext.Tools`, `skillContext.Instructions`) — gone
- MAF rc3 upgrade for skill features — no longer needed
- `.csproj` skills copy-to-output `<ItemGroup>` — gone

### Step 8: Approach A — MCP skill tools (progressive upgrade)

**Workflow:**
```
DotNetClaw (MCP Client)                 coffeeshop-cli (MCP Server)
───────────────────                     ────────────────────────
McpToolLoader.LoadAsync()
  │
  ├─ connects to coffeeshop-cli via stdio
  │   `dotnet run --project ../coffeeshop-cli -- mcp serve`
  │
  ├─ ListToolsAsync() discovers 11 tools:
  │   ├─ list_models, show_model           (ModelTools)
  │   ├─ lookup_customer, create_order...  (OrderTools)
  │   ├─ list_skills                       (SkillTools) ◄ AUTO-DISCOVERED
  │   └─ show_skill                        (SkillTools) ◄ AUTO-DISCOVERED
  │
  └─ Each tool wrapped as AITool via AIFunctionFactory
     Merged into agent's tool belt

Runtime:
  Agent calls show_skill("coffeeshop-counter-service")
    → MCP CallToolAsync → coffeeshop-cli → SkillParser → returns manifest
  Agent follows agentic loop, calls create_order(...)
    → MCP CallToolAsync → coffeeshop-cli → Python MCP server → response
```

**Code:** `DotNetClaw/Program.cs` with MCP (Approach A addition to Step 7)

```csharp
// After Step 7's tool registration, add MCP tools:

// ── MCP tools (when coffeeshop-cli MCP server is available) ──
try
{
    var mcpTools = await McpToolLoader.LoadAsync(
        sp.GetRequiredService<IConfiguration>(), startupLog, default);
    tools.AddRange(mcpTools);
    startupLog.LogInformation("[MCP] Added {Count} MCP tools", mcpTools.Count);
}
catch (Exception ex)
{
    startupLog.LogWarning(ex, "[MCP] MCP not available, using ExecTool fallback");
    // ExecTool + SkillLoaderTool still work without MCP
}
```

**Code:** `DotNetClaw/appsettings.json` MCP config:

```json
{
  "Mcp": {
    "Servers": {
      "coffeeshop": {
        "Transport": "stdio",
        "Command": "dotnet",
        "Args": [
          "run", "--project", "../coffeeshop-cli/src/CoffeeshopCli",
          "--", "mcp", "serve"
        ]
      }
    }
  }
}
```

**McpToolLoader.cs** — unchanged from current cli_dotnetclaw.md. No additional code needed because `list_skills`/`show_skill` are just more MCP tools that `ListToolsAsync()` auto-discovers.

**Progressive transition:**
| Phase | Skill loading | Order tools | Latency |
|-------|--------------|-------------|---------|
| B only | ExecTool → `skills show --json` | ExecTool → `models submit --json` | ~1-2s per command |
| A + B | MCP → `show_skill` | MCP → `create_order` | ~50ms (persistent pipe) |
| A only | MCP → `show_skill` | MCP → `create_order` | ~50ms |

---

## Phase 5 — Update Documentation

### Step 9: Update cli_dotnetclaw.md

**Changes to make:**

1. **Approach B Project Layout** — remove `DotNetClaw/skills/` directory:
```
DotNetClaw/DotNetClaw/
├── ExecTool.cs                    [NEW] shell execution tool
├── SkillLoaderTool.cs             [NEW] loads skills from coffeeshop-cli
├── DotNetClaw.csproj              [UNCHANGED] no MAF rc3 upgrade needed
├── appsettings.json               [MODIFY] add Exec config
└── Program.cs                     [MODIFY] ExecTool + SkillLoaderTool
                                   (NO FileAgentSkillsProvider, NO skills/ dir)
```

2. **Approach B Code Samples** — replace Program.cs with Step 7 version above

3. **Approach A Project Layout** — add SkillTools to coffeeshop-cli MCP tools:
```
coffeeshop-cli/src/CoffeeshopCli/Mcp/Tools/
├── ModelTools.cs
├── OrderTools.cs
└── SkillTools.cs                  [NEW] list_skills, show_skill
```

4. **Decision 6** — change from:
   > "Co-locate SKILL.md in DotNetClaw"  
   to:
   > "Host SKILL.md in coffeeshop-cli. Single source of truth. DotNetClaw fetches via ExecTool or MCP."

5. **Comparison table update:**

| Dimension | A: MCP stdio | B: ExecTool |
|-----------|-------------|-------------|
| Skill loading | MCP `show_skill` tool | ExecTool → `skills show --json` |
| Skill location | coffeeshop-cli/skills/ | coffeeshop-cli/skills/ (same) |
| Prerequisites | coffeeshop-cli MCP server | coffeeshop-cli `--json` commands |
| DotNetClaw code | McpToolLoader (auto-discovers) | ExecTool + SkillLoaderTool |

---

## Relevant Files

### coffeeshop-cli (create)
- `skills/coffeeshop-counter-service/SKILL.md` — adapted skill manifest
- `skills/coffeeshop-counter-service/assets/response-templates.md` — copy from original
- `src/CoffeeshopCli/Services/SkillParser.cs` — YAML + markdown parser
- `src/CoffeeshopCli/Services/FileSystemDiscoveryService.cs` — skills dir scanner
- `src/CoffeeshopCli/Commands/Skills/SkillsListCommand.cs` — `skills list`
- `src/CoffeeshopCli/Commands/Skills/SkillsShowCommand.cs` — `skills show <name>`
- `src/CoffeeshopCli/Mcp/Tools/SkillTools.cs` — MCP wrappers

### DotNetClaw (create)
- `DotNetClaw/SkillLoaderTool.cs` — loads skills from coffeeshop-cli via ExecTool
- `DotNetClaw/ExecTool.cs` — shell execution (same as current plan)

### DotNetClaw (modify)
- `DotNetClaw/Program.cs` — new tool wiring (Step 7 code above)

### DotNetClaw (remove)
- `DotNetClaw/skills/` — entire directory (replaced by coffeeshop-cli)

### Documentation (modify)
- `coffeeshop-cli/cli_dotnetclaw.md` — update both approaches per Step 9

## Verification

1. `dotnet run -- skills list --json` → `[{ "name": "coffeeshop-counter-service", ... }]`
2. `dotnet run -- skills show coffeeshop-counter-service --json` → full manifest with body
3. `dotnet run -- mcp serve` → MCP client discovers `list_skills` + `show_skill`
4. DotNetClaw startup logs: `[Skills] Discovered 1 skills` + `[Agent] Tool: LoadSkillAsync`
5. Slack: "I want to order coffee" → agent loads skill → follows 4-step loop → order created

## Decisions

1. **Single source of truth** — SKILL.md lives in coffeeshop-cli only
2. **Progressive consumption** — ExecTool first, MCP later
3. **FileAgentSkillsProvider removed** — replaced by ExecTool + SkillLoaderTool
4. **CLI commands as tool surface** — SKILL.md references `dotnet run -- <command> --json`
5. **Original preserved** — agent-skills-coffeeshop SKILL.md untouched
6. **OpenClaw aligned** — same skill location works for DotNetClaw and OpenClaw

## Further Considerations

1. **Skill caching:** ExecTool spawns `dotnet run` (~1-2s). Cache manifest at startup.
2. **response-templates.md:** Inlined into SKILL.md steps. No separate fetch needed.
3. **Original drift:** Keep original in agent-skills-coffeeshop for GitHub Copilot CLI; coffeeshop-cli version for CLI/agent consumers.
