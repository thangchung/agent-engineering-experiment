# Implementation Checklist

> Derived from [PRD.md](PRD.md) and [cli_dotnetclaw_v2.md](cli_dotnetclaw_v2.md)  
> Each item includes a verification step to confirm it works.

---

## Phase 1 — Foundation + Models

### 1.1 Project Scaffolding

- [x] **Create solution file** `coffeeshop-cli.slnx` at project root  
  **Verify:** `dotnet sln coffeeshop-cli.slnx list` lists the CoffeeshopCli project

- [x] **Create project** `src/CoffeeshopCli/CoffeeshopCli.csproj` targeting `net10.0`  
  **Verify:** `dotnet build src/CoffeeshopCli/CoffeeshopCli.csproj` succeeds with 0 errors

- [x] **Add NuGet dependencies** for Phase 1: `Spectre.Console`, `Spectre.Console.Cli`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Configuration`  
  **Verify:** `dotnet restore` succeeds; `dotnet list src/CoffeeshopCli package` shows all 4 packages

- [x] **Create `Program.cs`** with `CommandApp` entry point and empty branch registrations (`models`, `skills`, `mcp`, `docs`)  
  **Verify:** `dotnet run -- --help` shows the 4 branches in usage output

- [x] **Create DI infrastructure** — `TypeRegistrar.cs` and `TypeResolver.cs` in `Infrastructure/`  
  **Verify:** `CommandApp<T>` resolves injected services without runtime errors (validated by any command test)

### 1.2 Domain Data Models (R-MOD)

- [x] **Create `Customer.cs`** record with `CustomerId`, `Name`, `Email`, `Phone`, `Tier`, `AccountCreated`  
  **Verify:** Unit test instantiates `Customer` record and asserts all properties round-trip

- [x] **Create `MenuItem.cs`** record with `ItemType`, `Name`, `Category`, `Price`  
  **Verify:** Unit test instantiates `MenuItem` and asserts `Price` is `decimal`

- [x] **Create `Order.cs`** record with `OrderId`, `CustomerId`, `Status`, `PlacedAt`, `Total`, `Items`, `Notes`  
  **Verify:** Unit test creates `Order` with nested `List<OrderItem>` and `List<OrderNote>`

- [x] **Create `OrderItem.cs`** record with `ItemType`, `Name`, `Qty`, `Price`  
  **Verify:** Covered by `Order` test above (nested creation)

- [x] **[OPTIONAL]** **Create `OrderNote.cs`** record with `Text`, `Author`, `Timestamp`  
  **Verify:** Covered by `Order` test above (nested creation)  
  **Note:** Only implement if notes are displayed/edited in commands. Not used in current SKILL.md flow.

- [x] **Create `Enums.cs`** — `ItemType` (11 values), `OrderStatus` (6 values), `CustomerTier` (3 values)  
  **Verify:** Unit test asserts `Enum.GetValues<ItemType>().Length == 11`, `Enum.GetValues<OrderStatus>().Length == 6`, `Enum.GetValues<CustomerTier>().Length == 3`

### 1.3 Model Registry (R-MOD-01..05)

- [x] **Create `ModelRegistry.cs`** — reflection-based introspection of C# record types  
  **Verify:** Unit test — `ModelRegistry.GetProperties("Order")` returns property list including `Items` with child type `OrderItem`

- [x] **Registry returns property names, CLR types, nullability, and DataAnnotation attributes**  
  **Verify:** Unit test — `GetProperties("Customer")` returns `CustomerId` with type `string` and `Required` annotation

- [x] **Enum types expanded to show all valid values**  
  **Verify:** Unit test — `GetEnumValues("ItemType")` returns 11 strings including `LATTE`, `ESPRESSO`

- [x] **Nested types represented as child nodes**  
  **Verify:** Unit test — `GetSchema("Order")` tree includes child node for `OrderItem[]` with its own properties

### 1.4 Discovery Service (R-DISC-01..02)

- [x] **Create `IDiscoveryService.cs`** with `DiscoverModels()` and `DiscoverSkills()` methods  
  **Verify:** Interface compiles; implementing class is required to fulfill both methods

- [x] **Create `FileSystemDiscoveryService.cs`** — discover model types from configured assembly via reflection  
  **Verify:** Unit test — `DiscoverModels()` returns at least 5 model types (`Customer`, `MenuItem`, `Order`, `OrderItem`, `OrderNote`)

### 1.5 Commands — models (R-CMD-01..03, R-CMD-10)

- [x] **Create `ModelsListCommand.cs`** — `models list` displays all discovered models in a Spectre Table  
  **Verify:** `dotnet run -- models list` prints a table with 5 rows (Customer, MenuItem, Order, OrderItem, OrderNote)

- [x] **Create `ModelsShowCommand.cs`** — `models show <name>` renders schema as a Spectre Tree  
  **Verify:** `dotnet run -- models show Order` prints a tree with `OrderId`, `Items → OrderItem[]`, `Notes → OrderNote[]`

- [x] **`--json` flag on `models list`** outputs JSON array  
  **Verify:** `dotnet run -- models list --json | python3 -m json.tool` parses successfully and contains 5 objects

- [x] **`--json` flag on `models show`** outputs JSON object with properties  
  **Verify:** `dotnet run -- models show Customer --json | python3 -m json.tool` parses and shows `CustomerId` property

### 1.6 Output Formatting (R-OUT-01..04) — Simplified

- [x] **Create `JsonHelper.cs`** in `Infrastructure/` with `ToJson(object data)` method  
  **Verify:** Unit test — `JsonHelper.ToJson(new { name = "test" })` returns `{"name":"test"}` with indentation  
  **Why:** DRY — centralizes `JsonSerializerOptions` configuration instead of repeating in every command

- [x] **Commands use direct formatting** — TUI via `AnsiConsole`, JSON via `JsonHelper.ToJson()`  
  **Verify:** `ModelsListCommand` uses `AnsiConsole.Write(new Table(...))` for TUI, `JsonHelper.ToJson()` for JSON mode  
  **Why:** KISS — no unnecessary abstraction layer

- [x] **Spectre Table headers and row separators present**  
  **Verify:** `dotnet run -- models list` output includes column headers (`Name`, `Properties`, etc.)

- [x] **Monetary values display with `$` prefix and 2 decimal places**  
  **Verify:** `dotnet run -- models show MenuItem --json` shows `price` as a number; TUI mode shows `$4.50` format (manual visual check)

### 1.7 Error Handling (R-ERR-01..04)

- [x] **Create `CliError.cs`** base class with `Type`, `Message`, `Details`  
  **Verify:** Unit test — `new ValidationError("bad input")` inherits from `CliError`

- [x] **Create subtypes** — `ValidationError.cs`, `DiscoveryError.cs`, `McpError.cs`, `SkillError.cs`  
  **Verify:** Unit test — each subtype sets correct `Type` string (e.g., `"validation"`, `"discovery"`)

- [x] **TUI mode errors render as red Spectre Panels**  
  **Verify:** `dotnet run -- models show NonExistentModel` displays red panel with error message

- [x] **JSON mode errors serialize as structured JSON**  
  **Verify:** `dotnet run -- models show NonExistentModel --json` outputs `{ "error": { "type": "discovery", "message": "..." } }`

### 1.8 Test Scaffold

- [x] **Create `tests/CoffeeshopCli.Tests/CoffeeshopCli.Tests.csproj`** with xUnit + reference to main project  
  **Verify:** `dotnet test` runs with 0 test failures (even if 0 tests initially)

- [x] **Create `TestFixtures.cs`** in `tests/` with shared test helpers  
  **Verify:** Unit test uses `TempDirectoryFixture.Create()` → creates temp dir, disposes after test  
  **Fixtures:**
  - `TempDirectoryFixture` — creates/cleans temp directories for filesystem tests
  - `MockConfigFixture` — generates test `config.json` files
  - `SampleSkillFixture` — provides valid SKILL.md content for parser tests  
  **Why:** DRY — avoid repeating test setup across multiple test classes

- [x] **Create `ModelRegistryTests.cs`** covering 1.3 items above  
  **Verify:** `dotnet test --filter "FullyQualifiedName~ModelRegistryTests"` — all pass

---

## Phase 2 — Skills + Submission + MCP Client

### 2.1 Skill Hosting (v2 Phase 1 — Steps 1-2)

- [x] **Create `skills/coffeeshop-counter-service/SKILL.md`** — adapted from agent-skills-coffeeshop with CLI command references  
  **Verify:** File exists; YAML frontmatter parses with `name: coffeeshop-counter-service`, `version: "3.2"`

- [x] **Response templates inlined in SKILL.md body** (not separate file)  
  **Verify:** `grep 'Hi {customer_name}' skills/coffeeshop-counter-service/SKILL.md` matches inline template text  
  **Why:** YAGNI — v2 plan states "templates inlined into step instructions, no separate fetch needed"

- [x] **SKILL.md references `dotnet run -- <command> --json`** (not MCP tool names)  
  **Verify:** `grep 'mcp__' skills/coffeeshop-counter-service/SKILL.md` returns 0 matches; `grep 'dotnet run' skills/coffeeshop-counter-service/SKILL.md` returns multiple matches

### 2.2 Skill Parser (R-SKILL-01..02, v2 Phase 2 — Step 4)

- [x] **Add `YamlDotNet` NuGet package**  
  **Verify:** `dotnet list src/CoffeeshopCli package` shows `YamlDotNet`

- [x] **Create `SkillParser.cs`** — extracts YAML frontmatter + markdown body from `---` delimiters  
  **Verify:** Unit test — parse SKILL.md content string → `Frontmatter.Name == "coffeeshop-counter-service"`, `Body` starts with `# Coffee Shop`

- [x] **Frontmatter deserialization** handles `name`, `description`, `license`, `compatibility`, `metadata` (author, version, category, loop-type)  
  **Verify:** Unit test — all frontmatter fields populated correctly from sample YAML

- [x] **Body extraction** returns everything after closing `---`  
  **Verify:** Unit test — `Body` does not contain `---` delimiters and is non-empty

### 2.3 Skill Discovery (R-DISC-03, v2 Phase 1 — Step 3)

- [x] **`FileSystemDiscoveryService.DiscoverSkills()`** scans configured `skills/` directory for `*/SKILL.md` files  
  **Verify:** Unit test with temp dir — create `skills/test-skill/SKILL.md`, call `DiscoverSkills()`, assert 1 result with correct name

- [ ] **[DEFER]** **Discovery caches results in-memory** (R-DISC-04, Priority P1)  
  **Verify:** Unit test — call `DiscoverSkills()` twice, second call doesn't re-scan filesystem (mock filesystem to verify)  
  **Why:** YAGNI — CLI processes are short-lived; add only if profiling shows filesystem scanning is slow

- [x] **Discovery paths configurable** via `config.json` (`skills_directory`) and env var `COFFEESHOP_SKILLS_DIR` (R-DISC-05)  
  **Verify:** Set `COFFEESHOP_SKILLS_DIR=/tmp/test-skills`, place a SKILL.md there, run `dotnet run -- skills list --json` — skill appears

### 2.4 Commands — skills (R-CMD-05..06, v2 Steps 4-5)

- [x] **Create `SkillsListCommand.cs`** — `skills list` displays skills in Spectre Table  
  **Verify:** `dotnet run -- skills list` shows table with `coffeeshop-counter-service` row including name, description, version, loop-type

- [x] **`skills list --json`** outputs JSON array  
  **Verify:** `dotnet run -- skills list --json | python3 -c "import json,sys; d=json.load(sys.stdin); assert d[0]['name']=='coffeeshop-counter-service'"` exits 0

- [x] **Create `SkillsShowCommand.cs`** — `skills show <name>` renders full manifest in Spectre Panel  
  **Verify:** `dotnet run -- skills show coffeeshop-counter-service` displays panel with frontmatter and agentic loop body

- [x] **`skills show <name> --json`** outputs JSON with `frontmatter` and `body` fields  
  **Verify:** `dotnet run -- skills show coffeeshop-counter-service --json | python3 -c "import json,sys; d=json.load(sys.stdin); assert 'body' in d and 'frontmatter' in d"` exits 0

- [x] **`skills show` with unknown name returns error**  
  **Verify:** `dotnet run -- skills show nonexistent` exits with code 1 and displays error message

### 2.4a Skills Init Command & User Profile Directory (R-CMD-06a, R-CFG-05)

- [x] **Create `SkillsCopier.cs`** — utility for copying skills from project to user profile
  **Verify:** Unit test — `GetUserProfileSkillsPath()` returns `~/coffeeshop-cli/skills`; `CopySkills()` copies directory structure correctly
  **Methods:**
  - `GetUserProfileSkillsPath()` — returns `~/coffeeshop-cli/skills/`
  - `CopySkills(sourceDir, destDir, force)` — copies skills recursively; returns `CopyResult` with counts
  - `ValidateSkillDirectory(path)` — checks if directory contains valid skills
  - `CopyDirectory(source, dest, overwrite)` — private recursive copy helper

- [x] **Create `SkillsInitCommand.cs`** — `skills init [--force]` copies skills to user profile
  **Verify:** `dotnet run -- skills init` creates `~/coffeeshop-cli/skills/`, copies 1 skill, displays success table
  **Behavior:**
  - Without `--force`: skips existing skills, displays "Skills skipped" count
  - With `--force`: overwrites existing skills
  - Returns exit code 0 on success, 1 on error

- [x] **Update `ConfigLoader.cs`** — add user profile path to skills directory precedence
  **Verify:** Unit test — with user profile directory present, `ConfigLoader.Load()` prefers `~/coffeeshop-cli/skills/` over `./skills/`
  **Changes:**
  - Add `GetUserProfileSkillsDirectory()` helper method
  - Update skills directory resolution: CLI option > env var > config file > user profile (if exists) > `./skills/`

- [x] **Register `SkillsInitCommand` in Program.cs**
  **Verify:** `dotnet run -- skills --help` shows `init` subcommand

- [x] **Create `SkillsCopierTests.cs`** — 8 unit tests
  **Verify:** `dotnet test --filter "FullyQualifiedName~SkillsCopierTests"` — all 8 tests pass
  **Tests:**
  1. `GetUserProfileSkillsPath_ReturnsValidPath` — path resolution
  2. `CopySkills_WithValidSource_CreatesDestinationStructure` — basic copy
  3. `CopySkills_WithExistingSkill_SkipsWhenForceIsFalse` — skip behavior
  4. `CopySkills_WithExistingSkill_OverwritesWhenForceIsTrue` — force flag
  5. `CopySkills_WithAssetsFolder_CopiesRecursively` — nested directories
  6. `CopySkills_WithMultipleSkills_CopiesAll` — multiple skills
  7. `CopySkills_WithMissingSkillMd_SkipsDirectory` — validation
  8. `ValidateSkillDirectory_WithValidDirectory_ReturnsTrue` — validation method

- [x] **Create `SkillsInitCommandTests.cs`** — 3 integration tests
  **Verify:** `dotnet test --filter "FullyQualifiedName~SkillsInitCommandTests"` — all 3 tests pass
  **Tests:**
  1. `Execute_WithValidSource_ReturnsSuccessExitCode` — happy path
  2. `Execute_WithMissingSource_ReturnsErrorExitCode` — error handling
  3. `Execute_WithForceFlag_OverwritesExisting` — force flag behavior

- [x] **Manual verification**
  **Verify:** All commands automatically discover from user profile after `skills init`
  - `dotnet run -- skills init` → success, creates `~/coffeeshop-cli/skills/`
  - `dotnet run -- skills list` → discovers from user profile
  - `dotnet run -- skills show coffeeshop-counter-service` → loads from user profile
  - `dotnet run -- skills init` (second time) → skips existing, suggests `--force`
  - `dotnet run -- skills init --force` → overwrites successfully

**Architecture Note:** No changes to `SkillsListCommand`, `SkillsShowCommand`, or `SkillsInvokeCommand` required. All commands use `IDiscoveryService` via DI, which automatically resolves skills from the user profile location after ConfigLoader update.

### 2.5 Commands — models query & browse (R-CMD-02a, R-CMD-02b)

- [x] **Create `ModelsQueryCommand.cs`** — `models query <model> [--email <email>] [--customer-id <id>]` for customer lookup
  **Verify:** `dotnet run -- models query Customer --email alice@example.com --json` returns filtered customer data (customer_id, name, email, tier)

- [x] **Create `ModelsBrowseCommand.cs`** — `models browse <model>` lists all customers or menu items with filtered output
  **Verify:** `dotnet run -- models browse Customer --json` returns list of customers; `dotnet run -- models browse MenuItem --json` returns menu items with prices

### 2.6 Commands — models submit (R-CMD-04)

- [x] **Create `ModelsSubmitCommand.cs`** — `models submit <name>` accepts JSON from `--file` or stdin  
  **Verify:** `echo '{"customer_id":"C-1001"}' | dotnet run -- models submit Customer` succeeds

- [x] **JSON validated against target model schema before processing** (R-VAL-05)  
  **Verify:** `echo '{"bad_field":true}' | dotnet run -- models submit Customer` returns validation error

### 2.7 Validation (R-VAL-01..07)

- [x] **Create `IValidator.cs`** interface with per-model implementations  
  **Verify:** Unit test — `CustomerValidator`, `OrderValidator`, `MenuItemValidator` all implement `IValidator<T>`

- [x] **Create `ValidationHelpers.cs`** with shared validation patterns  
  **Verify:** Unit test — `ValidationHelpers.ValidateCustomerId("C-1001")` returns success; `"X-9999"` returns error  
  **Methods:**
  - `ValidateCustomerId(string)` — checks `C-\d{4}` pattern
  - `ValidateOrderId(string)` — checks `ORD-\d{4}` pattern
  - `ValidateEnum<TEnum>(string)` — checks enum membership  
  **Why:** DRY — avoid repeating regex patterns across multiple validators

- [x] **Customer ID pattern `C-\d{4}`**  
  **Verify:** Unit test — `CustomerValidator.Validate(new { CustomerId = "X-9999" })` returns error; `"C-1001"` passes

- [x] **Order ID pattern `ORD-\d{4}`**  
  **Verify:** Unit test — `OrderValidator` rejects `"BAD-1"`, accepts `"ORD-1001"`

- [x] **OrderStatus enum validation**  
  **Verify:** Unit test — rejects `"unknown"`, accepts all 6 valid values

- [x] **ItemType enum validation** (11 valid values)  
  **Verify:** Unit test — rejects `"TEA"`, accepts `"LATTE"` and all 10 others

- [x] **Validation errors collected (not fail-fast)**  
  **Verify:** Unit test — submit JSON with 3 invalid fields → error list contains 3 errors

### 2.8 Data Store (R-MCP-03) — SampleDataStore

- [x] **Create `SampleDataStore.cs`** — static in-memory data store with hardcoded menu items and customers
  **Verify:** `SampleDataStore.Menu.Count == 11`; all ItemType enums have MenuItem entries
  **Contents:**
  - 11 menu items matching product_catalogs.py: CAPPUCCINO, COFFEE_BLACK, COFFEE_WITH_ROOM, ESPRESSO, ESPRESSO_DOUBLE, LATTE, CAKEPOP, CROISSANT, MUFFIN, CROISSANT_CHOCOLATE, CHICKEN_MEATBALLS
  - 1 customer: Alice Smith (C-1001, alice@example.com, Gold tier)
  - Helper methods: `GetCustomerByEmail(email)`, `GetCustomerById(id)`, `GetMenuItemByType(itemType)` (all case-insensitive)

### 2.9 Order Submit Handler (R-MCP-04, R-HELP-01..05)

- [x] **Update `OrderSubmitHandler.cs`** — no longer async, uses `SampleDataStore` directly
  **Verify:** Unit test — input `{"customer_id":"C-1001","items":[{"item_type":"LATTE","qty":2}]}` → price lookup from SampleDataStore → total = $9.00
  **Changes from previous:** Constructor now `public OrderSubmitHandler()` (no parameters); uses `SampleDataStore` methods

- [x] **Auto-resolves `name` and `price` from SampleDataStore**  
  **Verify:** Unit test — LATTE finds MenuItem with `price=4.50`; handler sets `name="Latte"`

- [x] **Unknown item_type returns descriptive error**  
  **Verify:** Unit test — invalid ItemType → error includes "unknown item type"

- [x] **Unknown customer_id returns descriptive error**  
  **Verify:** Unit test — invalid customer ID → error includes "customer not found"

### 2.10 Removed: MCP Client (R-MCP-03 previous implementation)

- [x] **Delete `IMcpClient.cs`, `InMemoryMcpClient.cs`, `McpClientFactory.cs`, `McpClientWrapper.cs`**
  **Rationale:** Over-engineered for static data. Consolidated into `SampleDataStore`.
  **Verify:** `grep -r "IMcpClient\|McpClientFactory\|McpClientWrapper" src/` returns no matches

- [x] **Update `Program.cs`** — remove IMcpClient service registration block
  **Verify:** `dotnet build` succeeds

### 2.11 Configuration (R-CFG-01..04)

- [x] **Create `CliConfig.cs` and `ConfigLoader.cs`** — loads from `~/.config/coffeeshop-cli/config.json`  
  **Verify:** Unit test — create temp config file, `ConfigLoader.Load(path)` returns `CliConfig` with correct values

- [x] **Env var overrides with `COFFEESHOP_` prefix**  
  **Verify:** Set `COFFEESHOP_SKILLS_DIR=/tmp/test`, assert `ConfigLoader` returns `/tmp/test` for `skills_directory`

- [x] **Precedence: CLI options > env vars > config file > defaults**  
  **Verify:** Unit test — set all 3 sources for `skills_directory`, assert CLI option wins

### 2.12 Skill Parser Tests

- [x] **Create `SkillParserTests.cs`**  
  **Verify:** `dotnet test --filter "FullyQualifiedName~SkillParserTests"` — all pass

- [x] **Create `ValidatorTests.cs`**  
  **Verify:** `dotnet test --filter "FullyQualifiedName~ValidatorTests"` — all pass

- [x] **Create `OrderSubmitHandlerTests.cs`** — updated to use new `OrderSubmitHandler()` constructor (no parameters)
  **Verify:** `dotnet test --filter "FullyQualifiedName~OrderSubmitHandlerTests"` — all 6 tests pass (including 2 new tests for multi-item orders and order metadata)

---

## Phase 3 — MCP Server + Skill Invocation + Docs

### 3.1 MCP Server (R-MCP-01..02, v2 Phase 3 — Step 6)

- [x] **Add `ModelContextProtocol` NuGet package**  
  **Verify:** `dotnet list src/CoffeeshopCli package` shows `ModelContextProtocol`

- [x] **Create `McpServerHost.cs`** — starts MCP stdio server with tool registration  
  **Verify:** `echo '{"jsonrpc":"2.0","method":"initialize","params":{"capabilities":{}},"id":1}' | dotnet run -- mcp serve` returns valid JSON-RPC init response

- [x] **Create `McpServeCommand.cs`** — `mcp serve` command wiring  
  **Verify:** `dotnet run -- mcp serve` starts without error (manual — use Ctrl-C to exit)

- [x] **Create `ModelTools.cs`** — MCP tools for `list_models`, `show_model`  
  **Verify:** MCP client's `ListToolsAsync()` includes `list_models` and `show_model`; calling `list_models` returns 5 models

- [x] **Create `OrderTools.cs`** — MCP tools wrapping `OrderSubmitHandler` and MCP client passthrough  
  **Verify:** MCP client calls `create_order` tool — coffeeshop-cli proxies to Python orders server and returns order

- [x] **Create `SkillTools.cs`** — MCP tools for `list_skills`, `show_skill`  
  **Verify:** MCP client calls `list_skills` → returns JSON array with `coffeeshop-counter-service`; `show_skill` returns frontmatter + body

- [x] **MCP server exposes at least 5 tools total** (AC-07)  
  **Verify:** MCP client `ListToolsAsync()` → assert `count >= 5`

- [x] **Read-only tools annotated with `readOnlyHint`** (R-MCP-05)  
  **Verify:** `list_models`, `show_model`, `list_skills`, `show_skill` tool annotations include `readOnlyHint: true`

### 3.2 Skill Invocation — Direct Mode (R-SKILL-05..06, R-CMD-07)

- [x] **Create `SkillRunner.cs`** — executes agentic loop steps as state machine  
  **Verify:** Unit test — mock MCP client, run `SkillRunner` with scripted inputs → state transitions: INTAKE → CLASSIFY → REVIEW → FINALIZE

- [x] **SkillRunner maintains `CUSTOMER`, `INTENT`, `ORDER` state variables**  
  **Verify:** Unit test — after INTAKE step, `state["CUSTOMER"]` is populated; after CLASSIFY, `state["INTENT"]` is set

- [x] **Create `SkillsInvokeCommand.cs`** — `skills invoke <name>` runs the loop interactively  
  **Verify:** `dotnet run -- skills invoke coffeeshop-counter-service` prompts for customer identifier, classifies intent, walks through steps (manual interactive test)

### 3.3 Docs Browse (R-CMD-09)

- [x] **Create `DocsBrowseCommand.cs`** — `docs browse` interactive TUI  
  **Verify:** `dotnet run -- docs browse` opens interactive view showing models and skills (manual test — arrow keys navigate)

### 3.4 Exit Codes (R-ERR-05)

- [x] **Exit code 0 for success**  
  **Verify:** `dotnet run -- models list; echo $?` → `0`

- [x] **Exit code 1 for validation error**  
  **Verify:** `echo '{}' | dotnet run -- models submit Customer; echo $?` → `1`

- [x] **Exit code 2 for discovery error**  
  **Verify:** `dotnet run -- models show NonExistent; echo $?` → `2`

- [x] **Exit code 3 for MCP error** (when Python servers unreachable)  
  **Verify:** Stop Python MCP servers, run `dotnet run -- models submit Order --file order.json; echo $?` → `3`

- [x] **Exit code 4 for skill error**  
  **Verify:** `dotnet run -- skills invoke nonexistent; echo $?` → `4`

### 3.5 Integration Tests

- [x] **Create `CommandIntegrationTests.cs`** — end-to-end tests for key commands  
  **Verify:** `dotnet test --filter "FullyQualifiedName~CommandIntegrationTests"` — all pass

---

## Phase 4 — DotNetClaw Integration (v2 Phase 4)

### 4.1 Approach B — ExecTool (Steps 7)

- [x] **Create `DotNetClaw/ExecTool.cs`** — shell execution with safety blocklist  
  **Verify:** Unit test — `RunAsync("echo hello")` returns `"exit=0\nhello"`; `RunAsync("rm -rf /")` returns `"Blocked by safety filter."`

- [x] **Create `DotNetClaw/SkillLoaderTool.cs`** — loads skills from coffeeshop-cli via ExecTool  
  **Verify:** Integration test — `LoadSkillAsync("coffeeshop-counter-service")` returns body containing `# Coffee Shop Order Submission Skill`

- [x] **Update `DotNetClaw/Program.cs`** — register ExecTool + SkillLoaderTool, remove FileAgentSkillsProvider  
  **Verify:** DotNetClaw startup logs show `[Agent] Tool: RunAsync` and `[Agent] Tool: LoadSkillAsync`

- [x] **Startup skill discovery** via `skills list --json`  
  **Verify:** DotNetClaw startup logs show `[Skills] Discovered 1 skills`

- [x] **Skill advertisement injected into system prompt**  
  **Verify:** DotNetClaw startup logs (debug level) include `"Available skills (call load_skill to activate)"`

- [x] **Remove `DotNetClaw/skills/` directory** (no longer needed)  
  **Verify:** `ls DotNetClaw/DotNetClaw/skills/` → directory does not exist

- [x] **No FileAgentSkillsProvider references in DotNetClaw codebase**  
  **Verify:** `grep -r "FileAgentSkillsProvider" DotNetClaw/` returns 0 matches

- [x] **No MAF rc3 upgrade required**  
  **Verify:** `DotNetClaw.csproj` still references `Microsoft.Agents.AI v1.0.0-rc2`

### 4.2 Approach B — End-to-End Verification

- [x] **Agent can load skill at runtime** — user says "I want to order coffee" → agent calls `LoadSkillAsync`  
  **Verify:** In Slack/Teams, send "I want to order coffee" → agent responds with SKILL.md step 1 (greeting + customer lookup)

- [x] **Agent follows agentic loop** — walks through INTAKE → CLASSIFY → REVIEW → FINALIZE  
  **Verify:** Complete a full order flow in chat: identify customer → choose items → confirm order → order created with OrderId

- [x] **ExecTool calls coffeeshop-cli commands** — agent uses `RunAsync("dotnet run -- models submit...")` during skill execution  
  **Verify:** DotNetClaw logs show ExecTool invocations with coffeeshop-cli commands during the order flow

### 4.3 Approach A — MCP (Step 8, progressive upgrade)

- [ ] **Add MCP config to `DotNetClaw/appsettings.json`** — coffeeshop server entry  
  **Verify:** Config file contains `Mcp.Servers.coffeeshop` with `Command: "dotnet"` and args pointing to coffeeshop-cli

- [ ] **McpToolLoader auto-discovers `list_skills` and `show_skill`**  
  **Verify:** DotNetClaw startup logs show `[MCP] Added N MCP tools` where N includes skill tools

- [ ] **MCP fallback** — if MCP server unavailable, ExecTool still works  
  **Verify:** Stop coffeeshop-cli MCP server, DotNetClaw startup logs show `[MCP] MCP not available, using ExecTool fallback`; agent still functions via ExecTool

---

## Phase 5 — Documentation + Cleanup

### 5.1 Documentation Updates (v2 Step 9)

- [ ] **Update `cli_dotnetclaw.md`** — reflect new architecture (SKILL.md in coffeeshop-cli, no FileAgentSkillsProvider)  
  **Verify:** `cli_dotnetclaw.md` contains "Host SKILL.md in coffeeshop-cli" and does NOT contain "FileAgentSkillsProvider"

- [ ] **Approach B project layout** — no `DotNetClaw/skills/` directory in docs  
  **Verify:** `cli_dotnetclaw.md` Approach B layout shows `ExecTool.cs` + `SkillLoaderTool.cs`, no `skills/` folder

- [ ] **Comparison table updated** — both approaches show `coffeeshop-cli/skills/` as skill location  
  **Verify:** Manual review of the comparison table in `cli_dotnetclaw.md`

### 5.2 Final Verification — Acceptance Criteria (PRD §11)

- [ ] **AC-01:** `dotnet run -- models list` displays all 5 model types in a Spectre Table  
  **Verify:** Run command, visually confirm 5 rows: Customer, MenuItem, Order, OrderItem, OrderNote

- [ ] **AC-02:** `dotnet run -- models show Order` renders tree with nested `OrderItem[]` and `OrderNote[]`  
  **Verify:** Run command, confirm tree has child nodes for Items and Notes

- [ ] **AC-03:** `dotnet run -- models list --json` outputs valid JSON array  
  **Verify:** `dotnet run -- models list --json | python3 -m json.tool` exits 0

- [ ] **AC-04:** `dotnet run -- skills list` displays `coffeeshop-counter-service`  
  **Verify:** Run command, confirm table row with name and version 3.2

- [ ] **AC-05:** `dotnet run -- models submit Order --file order.json` validates and submits via MCP client  
  **Verify:** Create `order.json` with valid order, run command, confirm order created with OrderId

- [ ] **AC-06:** Invalid customer ID `X-9999` returns `ValidationError`  
  **Verify:** `echo '{"customer_id":"X-9999"}' | dotnet run -- models submit Customer` shows validation error

- [ ] **AC-07:** `mcp serve` responds to `tools/list` with ≥5 tools  
  **Verify:** MCP client `ListToolsAsync()` returns ≥5 tools

- [ ] **AC-08:** `skills invoke coffeeshop-counter-service` walks through 4-step agentic loop  
  **Verify:** Interactive manual test — complete all 4 steps

- [ ] **AC-09:** `docs browse` opens interactive TUI  
  **Verify:** Run command, confirm navigation works

- [ ] **AC-10:** All commands return appropriate exit codes (0=success, 1-4=typed errors)  
  **Verify:** Spot-check each exit code per §3.4 above

---

## Summary

| Phase | Items | Covers |
|-------|-------|--------|
| 1 — Foundation + Models | 28 | R-DISC-01/02, R-CMD-01/02/03/10, R-MOD-01..05, R-OUT-01..04, R-ERR-01..04 |
| 2 — Skills + Submission | 40 | R-DISC-03..05, R-CMD-04/05/06/06a, R-SKILL-01..04, R-HELP-01..05, R-VAL-01..07, R-CFG-01..06 (incl. user profile directory + 11 tests) |
| 3 — MCP Server + Invocation | 16 | R-CMD-07/08/09, R-SKILL-05/06, R-MCP-01..05, R-ERR-05 |
| 4 — DotNetClaw Integration | 14 | v2 Phases 4 (Approach A + B) |
| 5 — Documentation + ACs | 13 | v2 Phase 5, PRD §11 acceptance criteria |
| **Total** | **111** | **Mandatory: 107** / **Optional: 2** / **Deferred: 2** |

### Applied Principles

- **KISS:** Removed `IOutputFormatter` abstraction — commands handle output directly
- **YAGNI:** Removed `response-templates.md` file (inlined), deferred caching, marked `OrderNote` as optional
- **DRY:** Added shared helpers: `JsonHelper`, `TestFixtures`, `ValidationHelpers`, `McpClientWrapper`
- **Boy Scout Rule:** Centralized error handling, validation patterns, JSON serialization configuration
