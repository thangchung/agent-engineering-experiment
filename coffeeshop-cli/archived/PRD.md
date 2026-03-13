# PRD: coffeeshop-cli

> Product Requirements Document — v1.0 | 2026-03-09

---

## 1. Executive Summary

**coffeeshop-cli** is a .NET 10 / C# Terminal UI (TUI) tool built with Spectre.Console that provides a local-first interface to the coffeeshop domain. It draws architectural inspiration from the [Google Workspace CLI](https://github.com/googleworkspace/cli) (GWS CLI) — a Rust-based tool that dynamically builds its command surface from Google Discovery Documents.

Where GWS CLI fetches API discovery documents over HTTP to generate commands at runtime, coffeeshop-cli adapts the same component architecture for a smaller, self-contained domain: it discovers data models via C# reflection, loads agent skills from `SKILL.md` manifests on the filesystem, and exposes its capabilities both as an interactive TUI and as an MCP (Model Context Protocol) server for AI agents.

coffeeshop-cli is **not** a channel for DotNetClaw (the multi-channel agent runtime). It is a complementary local-first developer tool for inspecting models, invoking skills, and submitting orders against mock data.

---

## 2. Goals & Non-Goals

### Goals

- Provide a rich TUI for browsing coffeeshop domain models (Customer, MenuItem, Order, OrderItem)
- Discover and invoke agent skills defined in `SKILL.md` manifests
- Accept and validate hierarchical JSON payloads for order submission
- Expose all capabilities as MCP tools over stdio transport
- Query and browse mock customer and menu data via dedicated commands
- Support dual output: interactive TUI (default) and machine-readable JSON (`--json`)

### Non-Goals

- **No OAuth / authentication** — the domain uses mock data; skip GWS CLI's entire auth stack
- **No dynamic schema fetch** — no HTTP discovery documents; models are C# records introspected via reflection
- **No web UI** — this is a terminal-only tool
- **Not a DotNetClaw channel** — this CLI is a standalone developer tool
- **No YAML/CSV/NDJSON output** — only TUI and JSON
- **No setup wizard** — configuration is file-based with sensible defaults

---

## 3. Component Mapping: GWS CLI → coffeeshop-cli

| # | GWS CLI Component | GWS CLI Behavior | coffeeshop-cli Adaptation |
|---|---|---|---|
| 1 | **Discovery System** | HTTP fetch of Google Discovery Documents, 24h cache, two-phase parsing (index → per-service detail) | Filesystem scan: C# record types discovered via reflection for models; `SKILL.md` manifests discovered from skills directory. `IDiscoveryService` interface with `FileSystemDiscoveryService` implementation. No HTTP, no cache layer |
| 2 | **Command Structure** | `gws <service> <resource> <method>` — fully dynamic command tree generated from discovery docs at runtime | `coffeeshop-cli <noun> <verb>` — static command tree via Spectre.Console.Cli `AddBranch`/`AddCommand`. Branches: `models`, `skills`, `mcp`, `docs`. Static because the domain is small and Spectre.Console.Cli doesn't support dynamic tree generation |
| 3 | **Schema / Data Model** | `RestDescription` with resources, methods, `$ref` resolution. `gws schema` command for inspection | C# records (`Customer`, `MenuItem`, `Order`, `OrderItem`) as source of truth. `ModelRegistry` introspects via reflection (properties, types, attributes). `models show` renders schema as Spectre Tree |
| 4 | **Output Formatting** | JSON (default), NDJSON, Table, YAML, CSV. Dot-notation flattening for nested objects | Two modes: TUI (Spectre Table/Tree/Panel — default) and JSON (`--json` flag). No YAML, CSV, or NDJSON. Nested objects rendered as Spectre Trees in TUI mode |
| 5 | **Authentication** | Hierarchical credential resolution (env → file → keyring), AES-256-GCM encryption, OAuth token caching, interactive setup wizard | Not applicable — all data is mock. GitHub Copilot SDK handles its own auth externally if AI-assisted mode is enabled |
| 6 | **Agent Skills** | `generate-skills` produces `SKILL.md` files with YAML frontmatter from discovery documents | CLI *consumes* existing `SKILL.md` files (does not generate them). `SkillParser` extracts YAML frontmatter + markdown body. `SkillRunner` orchestrates the agentic loop defined in the skill manifest |
| 7 | **Helper Commands** | Per-service abstractions (e.g., `gmail+send`, `sheets+append`) that simplify complex multi-step API patterns | `OrderSubmitHandler` — accepts simplified order input, auto-looks up prices from the product catalog, calculates totals, and constructs the full `order_dto` for `create_order` |
| 8 | **Validation** | Defense-in-depth: path traversal prevention, control char stripping, URL encoding validation, API identifier format checks | Domain-scoped validation: customer ID pattern (`C-\d{4}`), order ID pattern (`ORD-\d{4}`), status enum, item_type enum, JSON structure validation. Uses DataAnnotations + `IValidator<T>` interface |
| 9 | **Error Handling** | Structured JSON errors (`code`/`message`/`reason`), contextual guidance on stderr, typed error hierarchy | `CliError` hierarchy: `ValidationError`, `DiscoveryError`, `McpError`, `SkillError`. Red Spectre Panels in TUI mode; JSON error objects `{ "error": { "type", "message", "details" } }` in `--json` mode |
| 10 | **Configuration** | `~/.config/gws/` with credentials, tokens, cache. Env var overrides. Interactive setup wizard | `~/.config/coffeeshop-cli/config.json` with discovery paths + MCP server settings. Env vars with `COFFEESHOP_` prefix. Precedence: CLI options > env vars > config file. No wizard |

---

## 4. Detailed Requirements

### 4.1 Discovery System (R-DISC)

| ID | Requirement | Priority |
|---|---|---|
| R-DISC-01 | `IDiscoveryService` interface with methods: `DiscoverModels()`, `DiscoverSkills()` | P0 |
| R-DISC-02 | `FileSystemDiscoveryService` discovers C# model types from a configured assembly via reflection | P0 |
| R-DISC-03 | `FileSystemDiscoveryService` scans a configured directory for `SKILL.md` files | P0 |
| R-DISC-04 | Discovery results are cached in-memory for the lifetime of the CLI process | P1 |
| R-DISC-05 | Discovery paths are configurable via config file (`models_assembly`, `skills_directory`) and env vars (`COFFEESHOP_MODELS_ASSEMBLY`, `COFFEESHOP_SKILLS_DIR`) | P1 |

### 4.2 Command Structure (R-CMD)

| ID | Requirement | Priority |
|---|---|---|
| R-CMD-01 | Root command tree: `models`, `skills`, `mcp`, `docs` branches registered via `AddBranch` | P0 |
| R-CMD-02 | `models list` — list all discovered data models as a Spectre Table | P0 |
| R-CMD-02a | `models query <model> [--email <email>] [--customer-id <id>]` — query specific customers by email or ID | P0 |
| R-CMD-02b | `models browse <model>` — list all customers or menu items with filtered output | P0 |
| R-CMD-03 | `models show <name>` — display model schema (properties, types, validation rules) as a Spectre Tree | P0 |
| R-CMD-04 | `models submit <name>` — accept JSON from stdin or `--file` and validate against the model | P0 |
| R-CMD-05 | `skills list` — list all discovered skills as a Spectre Table | P0 |
| R-CMD-06 | `skills show <name>` — display skill details (frontmatter + rendered markdown) in a Spectre Panel | P0 |
| R-CMD-07 | `skills invoke <name>` — run the skill's agentic loop interactively | P1 |
| R-CMD-08 | `mcp serve` — start MCP stdio server | P1 |
| R-CMD-09 | `docs browse` — interactive TUI for browsing domain documentation | P2 |
| R-CMD-10 | All commands support `--json` flag to switch from TUI to JSON output | P0 |

### 4.3 Schema / Data Model (R-MOD)

| ID | Requirement | Priority |
|---|---|---|
| R-MOD-01 | `ModelRegistry` class that introspects C# record types via reflection | P0 |
| R-MOD-02 | Registry returns property names, CLR types, nullability, and DataAnnotation attributes | P0 |
| R-MOD-03 | Enum types (e.g., `OrderStatus`, `ItemType`) are expanded to show all valid values | P0 |
| R-MOD-04 | Nested types (e.g., `Order` → `OrderItem[]`) are represented as child nodes in the tree | P0 |
| R-MOD-05 | `models show` renders the schema as an indented Spectre Tree with type annotations | P0 |

### 4.4 Output Formatting (R-OUT)

| ID | Requirement | Priority |
|---|---|---|
| R-OUT-01 | Default output uses Spectre.Console components: `Table`, `Tree`, `Panel`, `Markup` | P0 |
| R-OUT-02 | `--json` flag on any command outputs `System.Text.Json`-serialized JSON to stdout | P0 |
| R-OUT-03 | TUI tables include column headers and row separators for readability | P1 |
| R-OUT-04 | Monetary values display with 2 decimal places and `$` prefix | P1 |
| R-OUT-05 | Status values are color-coded in TUI mode (green=completed, yellow=preparing, red=cancelled) | P2 |

### 4.5 Agent Skills (R-SKILL)

| ID | Requirement | Priority |
|---|---|---|
| R-SKILL-01 | `SkillParser` extracts YAML frontmatter (name, description, license, metadata, allowed-tools) from `SKILL.md` | P0 |
| R-SKILL-02 | `SkillParser` extracts the markdown body (everything after the closing `---`) | P0 |
| R-SKILL-03 | `skills list` displays name, description, version, and loop-type from frontmatter | P0 |
| R-SKILL-04 | `skills show <name>` renders the full skill manifest in a Spectre Panel | P0 |
| R-SKILL-05 | `SkillRunner` executes the agentic loop steps programmatically (Direct mode — no LLM) | P1 |
| R-SKILL-06 | `SkillRunner` maintains internal state variables (`CUSTOMER`, `INTENT`, `ORDER`) as defined in the skill | P1 |
| R-SKILL-07 | AI-assisted mode: `SkillRunner` delegates step execution to an LLM via Copilot SDK or MAF | P3 |

### 4.6 Helper Commands (R-HELP)

| ID | Requirement | Priority |
|---|---|---|
| R-HELP-01 | `OrderSubmitHandler` accepts simplified input: `{ "customer_id": "C-1001", "items": [{ "item_type": "LATTE", "qty": 2 }] }` | P1 |
| R-HELP-02 | Handler auto-resolves `name` and `price` from the product catalog for each item | P1 |
| R-HELP-03 | Handler calculates `total` as `sum(price * qty)` across all items | P1 |
| R-HELP-04 | Handler constructs the full `order_dto` and calls `create_order` on the Orders MCP server | P1 |
| R-HELP-05 | Validation errors (unknown item_type, unknown customer_id) return descriptive error messages | P1 |

### 4.7 Validation (R-VAL)

| ID | Requirement | Priority |
|---|---|---|
| R-VAL-01 | Customer ID must match pattern `C-\d{4}` | P0 |
| R-VAL-02 | Order ID must match pattern `ORD-\d{4}` | P0 |
| R-VAL-03 | Order status must be one of: `pending`, `confirmed`, `preparing`, `ready`, `completed`, `cancelled` | P0 |
| R-VAL-04 | Item type must be one of the 11 valid `ItemType` enum values | P0 |
| R-VAL-05 | JSON payloads for `models submit` are validated against the target model's schema before processing | P0 |
| R-VAL-06 | `IValidator<T>` interface with per-model implementations for extensible validation | P1 |
| R-VAL-07 | Validation errors are collected (not fail-fast) and returned as a list | P1 |

### 4.8 Error Handling (R-ERR)

| ID | Requirement | Priority |
|---|---|---|
| R-ERR-01 | `CliError` base class with `Type`, `Message`, and optional `Details` dictionary | P0 |
| R-ERR-02 | Subtypes: `ValidationError`, `DiscoveryError`, `McpError`, `SkillError` | P0 |
| R-ERR-03 | TUI mode: errors render as red Spectre Panels with contextual guidance | P0 |
| R-ERR-04 | JSON mode: errors serialize as `{ "error": { "type": "...", "message": "...", "details": {} } }` | P0 |
| R-ERR-05 | Non-zero exit codes: 1 = validation error, 2 = discovery error, 3 = MCP error, 4 = skill error | P1 |

### 4.9 Configuration (R-CFG)

| ID | Requirement | Priority |
|---|---|---|
| R-CFG-01 | Config file at `~/.config/coffeeshop-cli/config.json` | P1 |
| R-CFG-02 | Config schema: `{ "discovery": { "models_assembly": "...", "skills_directory": "..." }, "mcp": { "servers": { ... } } }` | P1 |
| R-CFG-03 | Env var overrides with `COFFEESHOP_` prefix (e.g., `COFFEESHOP_SKILLS_DIR`) | P1 |
| R-CFG-04 | Precedence order: CLI options > env vars > config file > built-in defaults | P1 |
| R-CFG-05 | MCP server entries specify: name, command, args, env vars (matching `.vscode/mcp.json` format) | P1 |

### 4.10 MCP Server & Client (R-MCP)

| ID | Requirement | Priority |
|---|---|---|
| R-MCP-01 | `mcp serve` starts an MCP server over stdio transport using `ModelContextProtocol` NuGet package | P1 |
| R-MCP-02 | MCP server exposes tools that mirror CLI commands: model listing, model show, skill listing, order submission | P1 |
| R-MCP-03 | Data is provided by `SampleDataStore` — static in-memory store with 11 menu items and 1 customer (Alice Smith, C-1001) | P1 |
| R-MCP-04 | `OrderSubmitHandler` uses `SampleDataStore` directly for price lookup and order construction (non-async) | P1 |
| R-MCP-05 | MCP tool definitions include proper annotations (`readOnlyHint` for read-only tools) | P2 |

---

## 5. Architecture Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                        Program.cs                           │
│                     (CommandApp entry)                       │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  config.AddBranch("models", ...)                            │
│  config.AddBranch("skills", ...)                            │
│  config.AddBranch("mcp", ...)                               │
│  config.AddBranch("docs", ...)                              │
│                                                             │
└──────┬──────────┬──────────┬──────────┬─────────────────────┘
       │          │          │          │
       ▼          ▼          ▼          ▼
  ┌─────────┐ ┌─────────┐ ┌────────┐ ┌────────┐
  │ models  │ │ skills  │ │  mcp   │ │  docs  │
  │ ──────  │ │ ──────  │ │ ────── │ │ ────── │
  │ list    │ │ list    │ │ serve  │ │ browse │
  │ show    │ │ show    │ └───┬────┘ └────────┘
  │ submit  │ │ invoke  │     │
  └────┬────┘ └────┬────┘     │
       │           │          │
       ▼           ▼          ▼
  ┌──────────────────────────────────────────┐
  │              Service Layer               │
  │                                          │
  │  ┌──────────────┐  ┌──────────────────┐  │
  │  │ ModelRegistry │  │   SkillParser    │  │
  │  │ (reflection)  │  │ (YAML + MD)     │  │
  │  └──────────────┘  └──────────────────┘  │
  │                                          │
  │  ┌──────────────────────────────────┐    │
  │  │      IDiscoveryService           │    │
  │  │  └─ FileSystemDiscoveryService   │    │
  │  └──────────────────────────────────┘    │
  │                                          │
  │  ┌──────────────────────────────────┐    │
  │  │      SampleDataStore             │    │
  │  │  (11 menu items, customers)      │    │
  │  │  Static hardcoded data source    │    │
  │  └──────────────────────────────────┘    │
  │                                          │
  │  ┌──────────────────────────────────┐    │
  │  │      OrderSubmitHandler          │    │
  │  │  (price lookup, total calc,      │    │
  │  │   order_dto construction)        │    │
  │  └──────────────────────────────────┘    │
  │                                          │
  │  ┌──────────────────────────────────┐    │
  │  │      SkillRunner                 │    │
  │  │  (agentic loop orchestration)    │    │
  │  └──────────────────────────────────┘
  │                                          │
  │  ┌──────────────────────────────────┐    │
  │  │      MCP Server                  │◄───┼──── External AI Agents (stdio)
  │  │  (exposes tools)                 │    │
  │  └──────────────────────────────────┘    │
  └──────────────────────────────────────────┘
```

---

## 6. Domain Data Models

These models are defined as C# records. Sample data for Customer and MenuItem is stored in `SampleDataStore` — a static class with 11 menu items (from product_catalogs.py schema) and 1 customer for testing.

### 6.1 Customer

Source: `orders.py` — `_DEFAULT_CUSTOMERS`

```csharp
public record Customer
{
    public required string CustomerId { get; init; }    // Pattern: "C-\d{4}"
    public required string Name { get; init; }
    public required string Email { get; init; }
    public required string Phone { get; init; }
    public required CustomerTier Tier { get; init; }
    public required DateOnly AccountCreated { get; init; }
}

public enum CustomerTier
{
    Standard,
    Silver,
    Gold
}
```

### 6.2 MenuItem

Source: `product_catalogs.py` — `_DEFAULT_CATALOG`

```csharp
public record MenuItem
{
    public required ItemType ItemType { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }    // "Beverages", "Food", "Others"
    public required decimal Price { get; init; }
}

public enum ItemType
{
    CAPPUCCINO,
    COFFEE_BLACK,
    COFFEE_WITH_ROOM,
    ESPRESSO,
    ESPRESSO_DOUBLE,
    LATTE,
    CAKEPOP,
    CROISSANT,
    MUFFIN,
    CROISSANT_CHOCOLATE,
    CHICKEN_MEATBALLS
}
```

### 6.3 Order

Source: `orders.py` — `_DEFAULT_ORDERS`

```csharp
public record Order
{
    public required string OrderId { get; init; }        // Pattern: "ORD-\d{4}"
    public required string CustomerId { get; init; }     // Pattern: "C-\d{4}"
    public required OrderStatus Status { get; init; }
    public required DateTime PlacedAt { get; init; }
    public required decimal Total { get; init; }
    public required List<OrderItem> Items { get; init; }
    public required List<OrderNote> Notes { get; init; }
}

public enum OrderStatus
{
    Pending,
    Confirmed,
    Preparing,
    Ready,
    Completed,
    Cancelled
}
```

### 6.4 OrderItem

Source: `orders.py` — order item dicts within `_DEFAULT_ORDERS`

```csharp
public record OrderItem
{
    public required ItemType ItemType { get; init; }
    public required string Name { get; init; }
    public required int Qty { get; init; }
    public required decimal Price { get; init; }    // Unit price
}
```

### 6.5 OrderNote

Source: `orders.py` — `update_order` tool's note structure

```csharp
public record OrderNote
{
    public required string Text { get; init; }
    public required string Author { get; init; }
    public required DateTime Timestamp { get; init; }
}
```

---

## 7. MCP Tool Inventory

### 7.1 Orders Server (8 tools)

Source: `agent-skills-coffeeshop/mcp/orders.py`

| Tool | Signature | Read-Only | Description |
|---|---|---|---|
| `lookup_customer` | `(email?: string, customer_id?: string) → dict` | Yes | Look up customer by email or ID |
| `get_order` | `(order_id: string) → dict` | Yes | Get full order details |
| `order_history` | `(customer_id: string) → dict` | Yes | List all orders for a customer |
| `get_menu` | `() → dict` | Yes | Get all menu items with prices |
| `get_form_context` | `() → dict` | Yes | Get current customer context for order form |
| `open_order_form` | `(customer_id: string) → dict` | Yes | Open interactive order form for a customer |
| `create_order` | `(customer_id: string, order_dto: dict) → dict` | No | Create a new order |
| `update_order` | `(order_id: string, status?: string, add_note?: string) → dict` | No | Update order status and/or add note |

*Note: `reset_state` is a test-only tool, excluded from CLI exposure.*

### 7.2 Product Catalog Server (3 tools)

Source: `agent-skills-coffeeshop/mcp/product_catalogs.py`

| Tool | Signature | Read-Only | Description |
|---|---|---|---|
| `get_item_types` | `() → dict` | Yes | Get all item types with names, categories, prices |
| `get_items_prices` | `(item_types: string[]) → dict` | Yes | Get prices for specific item types |
| `reset_state` | `() → dict` | No | Reset catalog to defaults (test-only, excluded from CLI) |

### 7.3 Total: 11 tools across 2 servers

- **Exposed via CLI**: 9 tools (excluding both `reset_state` tools)
- **Read-only**: 7 tools
- **Mutating**: 2 tools (`create_order`, `update_order`)

---

## 8. Project Structure

```
coffeeshop-cli/
├── coffeeshop-cli.slnx                  # Solution file
├── CLAUDE.md                            # Claude Code project guidance
├── PRD.md                               # This document
│
├── src/
│   └── CoffeeshopCli/
│       ├── CoffeeshopCli.csproj         # Project file
│       ├── Program.cs                   # Entry point, CommandApp config
│       │
│       ├── Commands/                    # Spectre.Console.Cli commands
│       │   ├── Models/
│       │   │   ├── ModelsListCommand.cs
│       │   │   ├── ModelsShowCommand.cs
│       │   │   └── ModelsSubmitCommand.cs
│       │   ├── Skills/
│       │   │   ├── SkillsListCommand.cs
│       │   │   ├── SkillsShowCommand.cs
│       │   │   └── SkillsInvokeCommand.cs
│       │   ├── Mcp/
│       │   │   └── McpServeCommand.cs
│       │   └── Docs/
│       │       └── DocsBrowseCommand.cs
│       │
│       ├── Models/                      # Domain data models (C# records)
│       │   ├── Customer.cs
│       │   ├── MenuItem.cs
│       │   ├── Order.cs
│       │   ├── OrderItem.cs
│       │   ├── OrderNote.cs
│       │   └── Enums.cs                 # ItemType, OrderStatus, CustomerTier
│       │
│       ├── Services/                    # Business logic
│       │   ├── IDiscoveryService.cs
│       │   ├── FileSystemDiscoveryService.cs
│       │   ├── ModelRegistry.cs
│       │   ├── SkillParser.cs
│       │   ├── SkillRunner.cs
│       │   └── OrderSubmitHandler.cs
│       │
│       ├── Mcp/                         # MCP server + client
│       │   ├── McpServerHost.cs
│       │   ├── McpClientFactory.cs
│       │   └── Tools/                   # MCP tool definitions
│       │       ├── ModelTools.cs
│       │       ├── SkillTools.cs
│       │       └── OrderTools.cs
│       │
│       ├── Validation/                  # Validators
│       │   ├── IValidator.cs
│       │   ├── CustomerValidator.cs
│       │   ├── OrderValidator.cs
│       │   └── MenuItemValidator.cs
│       │
│       ├── Errors/                      # Error hierarchy
│       │   ├── CliError.cs
│       │   ├── ValidationError.cs
│       │   ├── DiscoveryError.cs
│       │   ├── McpError.cs
│       │   └── SkillError.cs
│       │
│       ├── Output/                      # Output formatting
│       │   ├── IOutputFormatter.cs
│       │   ├── TuiFormatter.cs
│       │   └── JsonFormatter.cs
│       │
│       ├── Configuration/               # Config loading
│       │   ├── CliConfig.cs
│       │   └── ConfigLoader.cs
│       │
│       └── Infrastructure/              # DI, type registration
│           ├── TypeRegistrar.cs
│           └── TypeResolver.cs
│
└── tests/
    └── CoffeeshopCli.Tests/
        ├── CoffeeshopCli.Tests.csproj
        ├── Services/
        │   ├── ModelRegistryTests.cs
        │   ├── SkillParserTests.cs
        │   └── OrderSubmitHandlerTests.cs
        ├── Validation/
        │   └── ValidatorTests.cs
        └── Commands/
            └── CommandIntegrationTests.cs
```

---

## 9. NuGet Dependencies

| Package | Purpose | Phase |
|---|---|---|
| `Spectre.Console` | TUI rendering (Table, Tree, Panel, Markup, Status) | 1 |
| `Spectre.Console.Cli` | Command routing, argument parsing, `AddBranch`/`AddCommand` | 1 |
| `System.Text.Json` | JSON serialization for `--json` output (included in .NET SDK) | 1 |
| `YamlDotNet` | Parse YAML frontmatter from `SKILL.md` files | 2 |
| `ModelContextProtocol` | MCP stdio server implementation | 3 |
| `Microsoft.Extensions.DependencyInjection` | DI container for `ITypeRegistrar`/`ITypeResolver` | 1 |
| `Microsoft.Extensions.Configuration` | Config file + env var loading | 1 |
| **Optional** | | |
| `GitHub.Copilot.SDK` | AI-assisted skill execution (Phase 4) | 4 |
| `Microsoft.Agents.AI` | MAF agent orchestration (Phase 4) | 4 |

---

## 10. Phased Delivery

### Phase 1 — Foundation + Models

**Goal:** Scaffold the project, implement model discovery, and build the `models` command branch.

- Project scaffolding: solution, csproj, `Program.cs` with `CommandApp`
- Domain data models as C# records (Section 6)
- `ModelRegistry` with reflection-based introspection
- `IDiscoveryService` + `FileSystemDiscoveryService` (model discovery only)
- Commands: `models list`, `models show <name>`
- Output: TUI (Spectre Table/Tree) + `--json` flag
- Error hierarchy: `CliError`, `ValidationError`, `DiscoveryError`
- DI infrastructure: `TypeRegistrar`, `TypeResolver`
- Unit tests for `ModelRegistry`

**Delivers:** R-DISC-01/02, R-CMD-01/02/03/10, R-MOD-01..05, R-OUT-01..04, R-ERR-01..04

### Phase 2 — Skills + Submission + MCP Client

**Goal:** Add skill discovery, JSON submission with validation, and MCP client connectivity.

- `SkillParser` for YAML frontmatter + markdown extraction
- Skill discovery in `FileSystemDiscoveryService`
- Commands: `skills list`, `skills show <name>`, `models submit <name>`
- `OrderSubmitHandler` with price lookup via MCP client
- `McpClientFactory` connecting to Python MCP servers
- Validators: `CustomerValidator`, `OrderValidator`, `MenuItemValidator`
- Configuration: `config.json`, env vars, precedence chain
- Unit tests for `SkillParser`, validators, `OrderSubmitHandler`

**Delivers:** R-DISC-03..05, R-CMD-04/05/06, R-SKILL-01..04, R-HELP-01..05, R-VAL-01..07, R-CFG-01..05

### Phase 3 — MCP Server + Skill Invocation + Docs

**Goal:** Expose tools via MCP server, implement programmatic skill invocation, and add docs browsing.

- `mcp serve` command with MCP stdio server
- MCP tool definitions mirroring CLI commands
- `SkillRunner` for direct-mode agentic loop execution
- Commands: `skills invoke <name>`, `mcp serve`, `docs browse`
- Interactive TUI for `docs browse` (model/skill exploration)
- Exit code conventions
- Integration tests

**Delivers:** R-CMD-07/08/09, R-SKILL-05/06, R-MCP-01..05, R-ERR-05

### Phase 4 — AI-Assisted Mode (Optional)

**Goal:** Enable LLM-driven skill execution via Copilot SDK or MAF.

- AI-assisted `SkillRunner` mode: delegates step execution to LLM
- GitHub Copilot SDK integration for natural language order intake
- MAF integration for multi-agent orchestration
- `--ai` flag on `skills invoke` to toggle mode

**Delivers:** R-SKILL-07

---

## 11. Acceptance Criteria

| # | Criterion | Phase |
|---|---|---|
| AC-01 | `dotnet run -- models list` displays all 5 model types (Customer, MenuItem, Order, OrderItem, OrderNote) in a Spectre Table | 1 |
| AC-02 | `dotnet run -- models show Order` renders a tree showing all properties including nested `OrderItem[]` and `OrderNote[]` | 1 |
| AC-03 | `dotnet run -- models list --json` outputs valid JSON array of model descriptors | 1 |
| AC-04 | `dotnet run -- skills list` displays `coffeeshop-counter-service` with name, description, version | 2 |
| AC-05 | `dotnet run -- models submit Order --file order.json` validates and submits an order via MCP client, returning the created order | 2 |
| AC-06 | Invalid customer ID `X-9999` in submit returns a `ValidationError` with descriptive message | 2 |
| AC-07 | `dotnet run -- mcp serve` starts an MCP server that responds to `tools/list` with at least 5 tools | 3 |
| AC-08 | `dotnet run -- skills invoke coffeeshop-counter-service` walks through the 4-step agentic loop interactively | 3 |
| AC-09 | `dotnet run -- docs browse` opens an interactive TUI for exploring models and skills | 3 |
| AC-10 | All commands return appropriate exit codes (0 = success, 1-4 = typed errors) | 3 |

---

## 12. Data Flow Diagrams

### 12.1 Model Discovery

```
Program.cs startup
       │
       ▼
FileSystemDiscoveryService.DiscoverModels()
       │
       ▼
Scan assembly for types with [DomainModel] attribute
  (or by convention: records in Models/ namespace)
       │
       ▼
ModelRegistry.Register(type) for each discovered type
       │
       ▼
ModelRegistry stores: { "Customer" → PropertyInfo[], "Order" → PropertyInfo[], ... }
       │
       ▼
Ready for `models list` / `models show` commands
```

### 12.2 Skill Invocation (Direct Mode)

```
User: `skills invoke coffeeshop-counter-service`
       │
       ▼
SkillsInvokeCommand loads skill via IDiscoveryService
       │
       ▼
SkillParser extracts frontmatter + agentic loop steps
       │
       ▼
SkillRunner initializes state: CUSTOMER=null, INTENT=null, ORDER=null
       │
       ▼
┌─── STEP 1: INTAKE ───────────────────────────────┐
│  Prompt user for identifier (email/customer_id)   │
│  Call: MCP Client → orders → lookup_customer       │
│  Store CUSTOMER                                    │
└──────────────────────────────────────────┬────────┘
                                           │
┌─── STEP 2: CLASSIFY INTENT ──────────────▼────────┐
│  Prompt user for what they need                    │
│  Classify: order-status | account | item-types |   │
│            process-order                           │
│  IF informational → display data, loop back        │
│  IF actionable → proceed                          │
└──────────────────────────────────────────┬────────┘
                                           │
┌─── STEP 3: REVIEW & CONFIRM ────────────▼─────────┐
│  Call: MCP Client → product_catalogs →             │
│        get_items_prices                            │
│  Call: MCP Client → orders → create_order          │
│  Display order summary                             │
│  Prompt user for confirmation                      │
└──────────────────────────────────────────┬────────┘
                                           │
┌─── STEP 4: FINALIZE ────────────────────▼─────────┐
│  Call: MCP Client → orders → update_order          │
│        (status="confirmed")                        │
│  Display confirmation + pickup estimate            │
└───────────────────────────────────────────────────┘
```

### 12.3 MCP Serving

```
External AI Agent (e.g., GitHub Copilot, Claude)
       │
       │ stdio (JSON-RPC)
       ▼
coffeeshop-cli mcp serve
       │
       ▼
McpServerHost receives tool call
       │
       ▼
Route to appropriate handler:
  ├── ModelTools.ListModels()      → ModelRegistry
  ├── ModelTools.ShowModel(name)   → ModelRegistry
  ├── OrderTools.SubmitOrder(json) → OrderSubmitHandler → MCP Client → orders.py
  └── SkillTools.ListSkills()      → IDiscoveryService
       │
       ▼
Serialize response → JSON-RPC → stdout → Agent
```

### 12.4 JSON Submission

```
User: `models submit Order --file order.json`
       │
       ▼
ModelsSubmitCommand reads JSON from file/stdin
       │
       ▼
Deserialize to Order record
       │
       ▼
OrderValidator.Validate(order)
  ├── Check CustomerId matches C-\d{4}
  ├── Check OrderId matches ORD-\d{4}
  ├── Check Status is valid enum
  ├── Check each Item has valid ItemType
  └── Collect all errors
       │
       ▼
IF validation errors → render ValidationError (Panel or JSON)
       │
       ▼
IF valid → OrderSubmitHandler
  ├── MCP Client → product_catalogs → get_items_prices (verify prices)
  ├── Calculate total
  ├── MCP Client → orders → create_order
  └── Return created order
       │
       ▼
Render result (Spectre Panel or JSON)
```

---

## Appendix A: Key Design Decisions

### A.1 Static vs Dynamic Command Tree

**Decision:** Static command tree.

GWS CLI dynamically generates commands from discovery documents. coffeeshop-cli uses a static tree because:
- The coffeeshop domain has a small, stable set of models and commands
- Spectre.Console.Cli's `AddBranch`/`AddCommand` API is designed for compile-time registration
- Dynamic generation adds complexity without proportional value for this domain size

### A.2 C# Records as Model Source of Truth

**Decision:** Models are C# records introspected via reflection.

GWS CLI parses JSON `RestDescription` documents for schema information. coffeeshop-cli uses C# records because:
- No separate schema files to maintain or keep in sync
- DataAnnotation attributes provide validation metadata alongside the type definition
- Reflection-based introspection is a one-time cost at startup

### A.3 Skill Invocation: Direct Mode First

**Decision:** Phase 1-3 implement programmatic (non-LLM) skill execution.

The `SkillRunner` follows `SKILL.md` steps as a state machine, prompting the user at each step and calling MCP tools directly. AI-assisted mode (using Copilot SDK or MAF to delegate step interpretation to an LLM) is deferred to Phase 4 because:
- Direct mode provides deterministic, testable behavior
- It validates the full pipeline (discovery → parsing → MCP calls → output) before adding AI variability
- AI-assisted mode is an enhancement, not a prerequisite

### A.4 MCP Dual Role

**Decision:** CLI is both MCP client and MCP server.

- **Client:** calls the Python MCP servers (`orders.py`, `product_catalogs.py`) to execute domain operations
- **Server:** exposes its own tools over stdio so external AI agents can use the CLI as a tool provider

This mirrors the GWS CLI pattern where the tool is both a consumer and producer of API capabilities.

### A.5 No Authentication

**Decision:** Skip the entire GWS CLI auth stack.

GWS CLI's auth system (hierarchical credential resolution, AES-256-GCM encryption, OAuth token caching, setup wizard) is its most complex component. coffeeshop-cli operates on mock data with no real accounts, so authentication is not needed. If auth is ever required, the Copilot SDK handles its own credential flow externally.
