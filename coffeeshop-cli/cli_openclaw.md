# Plan: Integrate coffeeshop-counter-service as an OpenClaw Skill

## TL;DR

Adapt the existing `coffeeshop-counter-service` SKILL.md (from `agent-skills-coffeeshop/skills/coffeeshop/`) for OpenClaw. The skill already defines the full 4-step agentic loop for coffeeshop ordering — it just needs its `allowed-tools` and `Setup` section rewritten to use OpenClaw's `exec` tool (calling coffeeshop-cli with `--json`) instead of VS Code/Copilot MCP tool calls. This is the fastest path: reuse the existing domain logic, adapt the tool surface.

## Background

- **Existing skill**: `agent-skills-coffeeshop/skills/coffeeshop/SKILL.md` is a fully-developed `coffeeshop-counter-service` skill with:
  - YAML frontmatter: name, description, license, metadata (v3.1, agentic loop-type)
  - `allowed-tools: mcp__orders__* mcp__product_items__* Read` — targets VS Code MCP tool naming
  - 4-step agentic loop: INTAKE → CLASSIFY INTENT → REVIEW & CONFIRM → FINALIZE
  - Internal state management (`CUSTOMER`, `INTENT`, `ORDER`)
  - References `assets/response-templates.md` for phrasing
- **Problem**: The skill references `mcp__orders__*` and `mcp__product_items__*` tools (VS Code Copilot MCP naming convention). OpenClaw doesn't have MCP client support built in — it uses `exec` to run CLIs.
- **Solution**: Rewrite the tool calls to use coffeeshop-cli via `exec`. The CLI's `--json` output gives structured data the agent can parse. The agentic loop logic stays identical.

## Steps

### Phase 1 — Adapt the Skill for OpenClaw

1. **Create OpenClaw-adapted skill directory**
   - `coffeeshop-cli/skills/coffeeshop-counter-service/SKILL.md` — inside the coffeeshop-cli repo
   - Copy `assets/response-templates.md` from the original skill (referenced by the agentic loop)

2. **Rewrite YAML frontmatter** for OpenClaw:
   - Keep: `name: coffeeshop-counter-service`, `description`, `license: MIT`, `metadata` (author, version, category, loop-type)
   - Add OpenClaw gating: `metadata` → single-line JSON with `"openclaw": { "requires": { "bins": ["dotnet"] } }`
   - Remove: `allowed-tools: mcp__orders__* mcp__product_items__* Read` (OpenClaw uses `exec`, not MCP tool names)
   - Add: `user-invocable: true` for `/coffeeshop-counter-service` slash command

3. **Rewrite the Setup section** — replace MCP server references with coffeeshop-cli exec commands:
   - Replace `lookup_customer(email?, customer_id?)` → `exec: dotnet run --project {baseDir}/.. -- skills invoke ... --json` OR more practically, translate each MCP tool call into the equivalent coffeeshop-cli command
   - Since coffeeshop-cli acts as an MCP *client* to the Python servers, the agent should call coffeeshop-cli which proxies to the upstream MCP servers
   - Document the exec pattern: `exec { command: "dotnet run -- <command> --json", cwd: "{baseDir}/../" }`

4. **Rewrite tool calls in the Agentic Loop steps** — for each step:
   - **STEP 1 (INTAKE)**: `lookup_customer` → `exec: dotnet run -- skills invoke coffeeshop-counter-service` (or if exposing individual MCP tools via CLI: a direct JSON submission)
   - **STEP 2 (CLASSIFY INTENT)**: `get_menu`, `open_order_form` → `exec: dotnet run -- models list --json` for menu browsing; order form is TUI-only so the agent constructs order JSON directly
   - **STEP 3 (REVIEW & CONFIRM)**: `get_items_prices`, `create_order` → `exec: dotnet run -- models submit Order --json`
   - **STEP 4 (FINALIZE)**: `update_order` → exec call to update order status
   - Key change: replace `open_order_form` (MCP Apps UI, HTML form) with agent-driven order construction since OpenClaw has no MCP Apps UI

5. **Keep the agentic loop structure intact** — the 4-step flow, internal state variables, routing logic, and response templates are all reusable as-is. Only the tool invocation mechanism changes.

### Phase 2 — OpenClaw Configuration

6. **Install the skill into OpenClaw** — pick one:
   - Symlink/copy `coffeeshop-cli/skills/coffeeshop-counter-service/` into `~/.openclaw/skills/coffeeshop-counter-service/`
   - Or add `coffeeshop-cli/skills` to `skills.load.extraDirs` in `~/.openclaw/openclaw.json`

7. **Configure exec approvals** (if in allowlist mode):
   - Add `dotnet` binary path to exec approvals allowlist
   - Or set `tools.exec.security` to be permissive enough for `dotnet run`

8. **Optional skill config** in `openclaw.json`:
   ```json
   {
     "skills": {
       "entries": {
         "coffeeshop-counter-service": {
           "enabled": true,
           "env": {
             "COFFEESHOP_CLI_DIR": "/path/to/coffeeshop-cli"
           }
         }
       }
     }
   }
   ```

9. **Ensure upstream MCP servers are running** — the Python MCP servers (`orders.py`, `product_catalogs.py`) from `agent-skills-coffeeshop/mcp/` must be reachable. coffeeshop-cli connects to them as an MCP client. Configure via `~/.config/coffeeshop-cli/config.json` (R-CFG-05 in PRD).

### Phase 3 — Validation & Testing

10. **Verify skill discovery**: restart OpenClaw gateway, confirm `coffeeshop-counter-service` appears in session skill list

11. **Test the full agentic loop**:
    - "I'm customer C-1001, I'd like to order 2 lattes" → agent runs INTAKE (lookup_customer) → CLASSIFY (process-order) → REVIEW (create_order, show summary) → FINALIZE (update_order)
    - "What's my order status? Order ORD-1001" → agent runs INTAKE → CLASSIFY (order-status) → displays data, loops back

12. **Test informational intents**: "What menu items are available?" → agent runs INTAKE → CLASSIFY (item-types) → fetches catalog → displays items

## Relevant Files

- `agent-skills-coffeeshop/skills/coffeeshop/SKILL.md` — **source**: existing skill to adapt from (keep as reference, don't modify)
- `agent-skills-coffeeshop/skills/coffeeshop/assets/response-templates.md` — **copy**: response phrasing templates used by the agentic loop
- `coffeeshop-cli/skills/coffeeshop-counter-service/SKILL.md` — **create**: the OpenClaw-adapted skill manifest
- `coffeeshop-cli/skills/coffeeshop-counter-service/assets/response-templates.md` — **create**: copy from original
- `~/.openclaw/openclaw.json` — **modify**: add skill config, extraDirs if needed
- `~/.config/coffeeshop-cli/config.json` — **modify**: configure MCP server connection to Python servers

## Verification

1. `openclaw gateway --verbose` — confirm `coffeeshop-counter-service` loads (check for gating errors)
2. Chat: "I'm customer C-1001, what's on the menu?" → agent identifies customer via exec, shows menu items
3. Chat: "I'd like 2 lattes and a croissant" → agent creates order via exec, shows summary, asks for confirmation
4. Chat: "Yes, confirm" → agent finalizes order, shows pickup estimate
5. Chat: "What's the status of order ORD-1001?" → agent fetches order, shows status
6. `/coffeeshop-counter-service` → slash command activates the skill context

## Decisions

- **Adapt existing skill, not create from scratch**: The `coffeeshop-counter-service` SKILL.md has a battle-tested 4-step agentic loop with proper state management. Rewriting only the tool surface (MCP → exec) preserves all domain logic.
- **`exec` with `--json` as tool surface**: OpenClaw doesn't support MCP client connections natively. The `exec` tool calling coffeeshop-cli (which itself is an MCP client) is the bridge.
- **Drop `open_order_form`**: The interactive HTML form (`ui://orders/order_form.html`) is a VS Code MCP Apps UI feature. OpenClaw's agent will construct order JSON directly from the conversation — this is actually more natural for chat.
- **Keep `coffeeshop-counter-service` as skill name**: Consistent with PRD naming, CLAUDE.md examples, and the original skill manifest.
- **Gating on `dotnet`**: `requires.bins: ["dotnet"]` ensures skill only loads when .NET SDK is present.
- **Excluded**: `docs browse` (TUI-only), `mcp serve` as standalone (future enhancement)

## Further Considerations

1. **Two Python MCP servers must be running**: coffeeshop-cli acts as MCP client to `orders.py` and `product_catalogs.py`. These need to be started separately (or coffeeshop-cli needs to auto-start them). Document this in the skill setup instructions.
2. **Pre-built binary**: If coffeeshop-cli is published as `coffeeshop-cli` on PATH, exec commands simplify. The skill could check for either `coffeeshop-cli` or `dotnet` via `requires.anyBins`.
3. **Future: native MCP plugin**: If OpenClaw adds MCP client support (or via a plugin), the skill could be rewritten to call MCP tools directly instead of shelling out. This would eliminate the exec overhead and match the original SKILL.md's approach.

## Summary of Integration Paths Evaluated

### Option 1: Skill-based (Recommended ✓)
- **Approach**: Write a `SKILL.md` that wraps coffeeshop-cli via the `exec` tool
- **Pros**: Lightweight, no TypeScript, reuses existing agentic loop logic, works across all OpenClaw channels
- **Cons**: Shell subprocess overhead per command; limited by `exec` tool constraints
- **Effort**: Medium — adapt existing skill, copy response templates, configure OpenClaw
- **Result**: Fastest path to integration; leverages existing `coffeeshop-counter-service` domain knowledge

### Option 2: MCP tool via exec (Alternative)
- **Approach**: Start `coffeeshop-cli mcp serve` in background, call via OpenClaw's exec
- **Pros**: More efficient than per-command exec; JSON-RPC over stdio
- **Cons**: Requires background process management; OpenClaw lacks native MCP client support
- **Effort**: High — would require custom setup; not recommended without OpenClaw plugin
- **Result**: More robust but requires more infrastructure

### Option 3: OpenClaw Plugin (TypeScript)
- **Approach**: Write a TypeScript plugin that registers typed agent tools wrapping coffeeshop-cli
- **Pros**: Typed schemas, integrated tool policy, cleaner agent prompts
- **Cons**: Heavy — requires TypeScript, plugin bundling, npm publishing
- **Effort**: High — plugin SDK, testing, documentation
- **Result**: Over-engineered for wrapping a CLI; useful if OpenClaw needs persistent tools beyond coffeeshop-cli

**Recommendation**: Use Option 1 (Skill). It leverages the existing `coffeeshop-counter-service` agentic loop without modification, integrates instantly with OpenClaw's skill discovery, and works across any channel. The exec overhead is acceptable for an interactive ordering skill.
