# Research Report: MCP Apps Applicability to CoffeeShop

**Date:** February 23, 2026  
**Scope:** Deep analysis of [CopilotKit/mcp-apps-demo](https://github.com/CopilotKit/mcp-apps-demo) and feasibility evaluation for the CoffeeShop ordering application.

---

## 1. What Is `mcp-apps-demo`?

The `CopilotKit/mcp-apps-demo` repository is a reference implementation of **MCP Apps** — an extension to the Model Context Protocol (MCP) standard defined by [SEP-1865](https://github.com/modelcontextprotocol/modelcontextprotocol/pull/1865) and implemented in [`modelcontextprotocol/ext-apps`](https://github.com/modelcontextprotocol/ext-apps). It demonstrates four interactive demo apps (Airline Booking, Hotel Booking, Investment Simulator, Kanban Board) that render **rich, interactive HTML UIs inside the AI chat sidebar** as sandboxed iframes.

### Key Concept

Traditional MCP tools return text/JSON. MCP Apps let MCP tools **declare an associated UI resource** (`ui://` URI). When the AI calls that tool, the host (CopilotKit) fetches the HTML resource from the MCP server and renders it inline in the chat. The UI can then communicate bidirectionally with the MCP server via JSON-RPC over `postMessage`.

---

## 2. Architecture Deep Dive

### 2.1. System Components

```
┌─────────────────────────────────────────────────────┐
│                 Next.js Frontend                     │
│  ┌─────────────────────────────────────────────────┐ │
│  │  page.tsx                                       │ │
│  │  • CopilotKitProvider (runtimeUrl="/api/copilotkit") │
│  │  • CopilotSidebar / CopilotPopup               │ │
│  │  • Demo landing page with prompt pills          │ │
│  └─────────────────────────────────────────────────┘ │
│  ┌─────────────────────────────────────────────────┐ │
│  │  api/copilotkit/[[...slug]]/route.ts            │ │
│  │  • BuiltInAgent with system prompt              │ │
│  │  • MCPAppsMiddleware (connects to MCP server)   │ │
│  │  • CopilotRuntime + InMemoryAgentRunner         │ │
│  │  • createCopilotEndpoint (Hono)                 │ │
│  └─────────────────────────────────────────────────┘ │
└──────────────────┬──────────────────────────────────┘
                   │ HTTP (JSON-RPC / postMessage proxy)
                   ▼
┌─────────────────────────────────────────────────────┐
│             MCP Server (Express, port 3001)          │
│  • McpServer from @modelcontextprotocol/sdk          │
│  • StreamableHTTPServerTransport                     │
│  • InMemoryEventStore (for resumability)             │
│  • Tools: search-flights, search-hotels, etc.        │
│  • Resources: ui://flights/flights-app.html, etc.    │
│  • Apps: Self-contained HTML (built by Vite)         │
│  • Session management per MCP connection             │
└─────────────────────────────────────────────────────┘
```

### 2.2. Tool Registration Pattern

Each tool with a UI resource declares a `_meta` field with the `ui/resourceUri`:

```typescript
server.registerTool("search-flights", {
  inputSchema: { origin, destination, departureDate, passengers },
  _meta: { "ui/resourceUri": "ui://flights/flights-app.html" }
}, handler);

server.registerResource("flights-app", "ui://flights/flights-app.html", {
  mimeType: "text/html+mcp"  // Magic MIME type marking it as an MCP App
}, () => ({ contents: [{ text: htmlContent }] }));
```

### 2.3. Flow Sequence

1. User sends "Book a flight from JFK to LAX" in CopilotKit chat
2. `BuiltInAgent` (LLM) decides to call `search-flights` tool
3. `MCPAppsMiddleware` intercepts the tool call, forwards to MCP server
4. MCP server executes `search-flights`, returns `structuredContent` + text summary
5. Middleware sees `_meta.ui/resourceUri`, fetches the HTML resource via `resources/read`
6. Middleware emits an `ACTIVITY_SNAPSHOT` event (type `"mcp-apps"`) with the HTML content
7. CopilotKit frontend renders the HTML in a sandboxed iframe inside the chat
8. User interacts with the wizard UI inside the iframe
9. When the UI needs server data (e.g., select-flight), it sends JSON-RPC via `postMessage`
10. CopilotKit proxies the request back through the middleware to the MCP server

### 2.4. Agent Configuration

The demo uses `BuiltInAgent` — a CopilotKit-native agent that directly calls an LLM (OpenAI, Anthropic, or Google) with a system prompt. No external agent framework involved:

```typescript
const agent = new BuiltInAgent({
  model: "openai/gpt-5.2",
  prompt: "You are an AI assistant with access to 4 interactive apps...",
}).use(new MCPAppsMiddleware({
  mcpServers: [
    { type: "http", url: "http://localhost:3001/mcp" }
  ],
}));

const runtime = new CopilotRuntime({
  agents: { default: agent },
  runner: new InMemoryAgentRunner(),
});
```

### 2.5. Frontend — Minimal

The frontend has **zero custom UI components** for the apps themselves. The `page.tsx` only has:
- `CopilotKitProvider` with `runtimeUrl="/api/copilotkit"`
- `CopilotSidebar` / `CopilotPopup` — standard CopilotKit chat components
- Demo landing cards with prompt pills (pure presentation)

All interactive app UIs are **served by the MCP server** and rendered in iframes. The frontend has **no `useCopilotAction` or `useCopilotReadable` calls** — the entire UI is MCP-driven.

### 2.6. MCP Server — Self-Contained

The MCP server is a standalone Express app using `@modelcontextprotocol/sdk` with:
- `StreamableHTTPServerTransport` for session-based HTTP communication
- In-memory data stores (flights, hotels, portfolios, boards)
- Tools with `structuredContent` (structured data returned alongside text)
- Resources serving single-file HTML apps (built by Vite + `vite-plugin-singlefile`)
- CORS enabled for cross-origin iframe communication

### 2.7. Dependencies

```json
{
  "@ag-ui/client": "^0.0.42",
  "@ag-ui/encoder": "^0.0.42",
  "@ag-ui/mcp-apps-middleware": "^0.0.1",
  "@copilotkitnext/agent": "1.51.0-next.4",
  "@copilotkitnext/react": "1.51.0-next.4",
  "@copilotkitnext/runtime": "1.51.0-next.4",
  "hono": "^4.11.3",
  "next": "16.1.1"
}
```

---

## 3. CoffeeShop Current Architecture

```
┌─────────────────────────────────────────────────────┐
│           Next.js Frontend (port 3000)               │
│  page.tsx:                                           │
│  • useCopilotReadable  (customer, menuItems, order)  │
│  • useCopilotAction    (setCustomer, updateMenu,     │
│                         updateOrderSummary)           │
│  • React components: CustomerPanel, MenuGrid,        │
│                       OrderSummaryPanel               │
│  • CopilotSidebar (chat UI)                          │
│                                                      │
│  api/copilotkit/route.ts:                            │
│  • CopilotRuntime + ExperimentalEmptyAdapter         │
│  • HttpAgent → counter backend AG-UI endpoint        │
│  • deduplicateAssistantDeltas transformer            │
│                                                      │
│  api/copilotkit/runtime.ts:                          │
│  • Shared singleton CopilotRuntime                   │
│  • HttpAgent("http://localhost:5000/api/v1/copilotkit") │
└──────────────────┬──────────────────────────────────┘
                   │ AG-UI Protocol (SSE streaming)
                   ▼
┌─────────────────────────────────────────────────────┐
│        .NET Counter Backend (port 5000)              │
│  GitHubCopilotAgent (MAF):                           │
│  • Tools: lookup_customer, get_menu, place_order     │
│  • System prompt: CounterAgent.AgentInstructions     │
│  • SessionPersistingAgent wrapper                    │
│  • GitHub Copilot SDK → LLM (GitHub Copilot CLI)     │
│  MapAGUI("/api/v1/copilotkit", agent)                │
│                                                      │
│  Domain: Orders, Customers, Menu Items               │
│  Workers: BaristaWorker, KitchenWorker               │
│  Stores: InMemoryOrderStore, InMemoryCustomerStore   │
└─────────────────────────────────────────────────────┘
```

### Key Differences from mcp-apps-demo

| Aspect | mcp-apps-demo | CoffeeShop |
|--------|---------------|------------|
| **Agent type** | `BuiltInAgent` (CopilotKit-native, calls LLM directly) | `GitHubCopilotAgent` (MAF, .NET, GitHub Copilot SDK) |
| **LLM** | OpenAI / Anthropic / Google (API key in frontend) | GitHub Copilot CLI (no API key needed) |
| **Agent location** | In the Next.js runtime (JavaScript) | In a separate .NET backend process |
| **Communication** | CopilotKit ↔ BuiltInAgent (in-process) | Next.js ↔ .NET via AG-UI protocol (SSE) |
| **Frontend UI** | Zero custom components, all via MCP Apps (iframes) | Rich React components (CustomerPanel, MenuGrid, OrderSummaryPanel) |
| **State management** | No frontend state; UI is in MCP server | `useCopilotReadable` + `useCopilotAction` drive React state |
| **Tool hosting** | MCP server (TypeScript, `@modelcontextprotocol/sdk`) | .NET backend (`AIFunctionFactory.Create`, MAF) |
| **Runtime adapter** | `InMemoryAgentRunner` + `createCopilotEndpoint` (Hono) | `ExperimentalEmptyAdapter` + `copilotRuntimeNextJSAppRouterEndpoint` |

---

## 4. Can MCP Apps Be Used with CoffeeShop?

### 4.1. Technical Feasibility Assessment

#### What MCPAppsMiddleware Requires

The `MCPAppsMiddleware` is an AG-UI middleware that:
1. Connects to one or more MCP servers via HTTP or SSE
2. Discovers tools with `ui/resourceUri` metadata
3. Injects those tools into the agent's tool list
4. Intercepts tool calls, executes them on the MCP server
5. Emits `ACTIVITY_SNAPSHOT` events with the UI HTML
6. Proxies `postMessage` calls from the iframe back to the MCP server

#### The Core Problem: Agent Architecture Mismatch

MCPAppsMiddleware uses `.use()` on an AG-UI agent (like `BuiltInAgent`). In CoffeeShop, the agent lives in the **.NET backend** — not in the Next.js runtime. The CopilotKit runtime on the Next.js side only knows about the backend as an `HttpAgent` endpoint.

```
mcp-apps-demo:  CopilotRuntime → BuiltInAgent.use(MCPAppsMiddleware) → MCP Server
                                     ↑ middleware runs HERE

CoffeeShop:     CopilotRuntime → HttpAgent → .NET backend (GitHubCopilotAgent)
                                  ↑ no agent to attach middleware to
```

**The middleware cannot be `.use()`-d on an `HttpAgent`** — `HttpAgent` is a pass-through transport, not a pluggable agent with middleware support.

### 4.2. Integration Approaches

#### Approach A: Hybrid — Add a BuiltInAgent with MCP Apps alongside the HttpAgent

**Concept:** Keep the existing .NET `GitHubCopilotAgent` via `HttpAgent` for coffee ordering, but add a second `BuiltInAgent` with `MCPAppsMiddleware` for MCP Apps-powered UI features.

**Pros:**
- No changes to the .NET backend
- Can add MCP Apps features independently
- MCP Apps UI would render in the CopilotKit sidebar

**Cons:**
- Requires a separate LLM API key (OpenAI / Anthropic) for the BuiltInAgent
- Two agents means routing logic — which agent handles which request
- Defeats the purpose of the single-conversation CoffeeShop counter agent
- Violates constitution principle: GitHub Copilot SDK is the LLM

**Verdict: ❌ Not recommended** — breaks the single-agent, single-LLM architecture.

---

#### Approach B: Build MCP Server for CoffeeShop + MCPAppsMiddleware on a BuiltInAgent

**Concept:** Extract CoffeeShop domain logic into an MCP server (TypeScript or .NET) that exposes tools with UI resources. Replace the .NET backend entirely. Use `BuiltInAgent` with `MCPAppsMiddleware`.

**Pros:**
- Full MCP Apps experience: interactive order forms, menu browsing, order history as rich HTML UIs
- Eliminates the GitHub Copilot SDK dependency and the `deduplicateAssistantDeltas` workaround
- Simpler architecture: frontend ↔ BuiltInAgent ↔ MCP Server

**Cons:**
- Requires rewriting the entire .NET backend as an MCP server
- Loses MAF (Microsoft Agents Framework) and GitHub Copilot SDK integration — which is the project's raison d'être
- Loses .NET Aspire orchestration, service discovery, health checks
- Loses the BaristaWorker/KitchenWorker channel-based dispatch pattern
- Requires a commercial LLM API key (no longer free via GitHub Copilot)
- Complete rewrite — not practical

**Verdict: ❌ Not recommended** — eliminates the core .NET/MAF/GitHub Copilot architecture.

---

#### Approach C: Build MCP Server as a sidecar to the .NET backend

**Concept:** Create a TypeScript MCP server that wraps the existing .NET REST endpoints (`GET /api/v1/menu`, `POST /api/v1/orders`, etc.) and adds MCP resource declarations for each tool's UI. Use `MCPAppsMiddleware` on the CopilotKit side.

**Challenge:** Still requires a `BuiltInAgent` or compatible agent to attach `MCPAppsMiddleware` to. `HttpAgent` does not support `.use()`. And the LLM would need to be duplicated — the .NET `GitHubCopilotAgent` has the LLM, but the MCP Apps flow needs the `BuiltInAgent` to also have an LLM.

**Verdict: ❌ Impractical** — creates two competing LLM engines with no coordination.

---

#### Approach D: Implement MCP Apps natively in the .NET backend (AG-UI level)

**Concept:** Instead of using `MCPAppsMiddleware` (which is a JavaScript/TypeScript construct), implement the MCP Apps protocol behavior directly in the .NET AG-UI layer. The `GitHubCopilotAgent` would emit `ACTIVITY_SNAPSHOT` events with HTML content through the AG-UI stream.

**How it would work:**
1. .NET backend hosts HTML app files (e.g., `menu-app.html`, `order-app.html`)
2. When `get_menu` or `place_order` tools are called, the agent returns an AG-UI `ACTIVITY_SNAPSHOT` event containing the `mcp-apps` activity type and the HTML resource
3. CopilotKit's frontend renderer picks up the `mcp-apps` activity and renders the iframe
4. The iframe communicates back to the .NET backend via the CopilotKit proxy

**Pros:**
- Preserves the entire .NET architecture
- No LLM duplication
- Adds rich interactive UI without changing agent or tool logic

**Cons:**
- Requires understanding and implementing the AG-UI `ACTIVITY_SNAPSHOT` protocol manually in C#
- The iframe↔server bidirectional JSON-RPC communication needs a proxy endpoint
- `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore.MapAGUI` would need to support the `ACTIVITY_SNAPSHOT` event type, or we'd need custom SSE event injection
- CopilotKit's frontend renderer for `mcp-apps` activity type is tied to the `MCPAppsMiddleware` protocol — may not work without the middleware's proxied request infrastructure
- No existing .NET MCP Apps SDK; would need to build from scratch

**Verdict: ⚠️ Technically possible but very high effort** — requires AG-UI protocol-level knowledge and custom infrastructure.

---

#### Approach E: Use CopilotKit's existing generative UI (`useCopilotAction` with `render`)

**Concept:** Instead of MCP Apps iframes, use CopilotKit's native generative UI support — `useCopilotAction` with a `render` function that returns React components.

**How it would work:**
```tsx
useCopilotAction({
  name: "updateMenu",
  render: ({ items }) => <MenuGrid items={items} />,
  handler: ({ items }) => setMenuItems(items),
});
```

**Pros:**
- Already partially implemented (CoffeeShop has `useCopilotAction` for `setCustomer`, `updateMenu`, `updateOrderSummary`)
- No architecture changes needed
- React components are more capable than sandboxed iframes
- Full access to frontend state, routing, etc.
- No MCP server needed

**Cons:**
- Not "MCP Apps" — it's CopilotKit's native generative UI
- Components live in the frontend, not served by a server
- Requires frontend code changes for each new UI

**Verdict: ✅ Already the correct approach for CoffeeShop** — this is what we're doing.

---

### 4.3. Compatibility Summary

| Component | MCP Apps Compatible? | Notes |
|-----------|---------------------|-------|
| `GitHubCopilotAgent` (.NET) | ❌ No | No MCP Apps middleware exists for MAF/.NET |
| `HttpAgent` (frontend proxy) | ❌ No | Does not support `.use()` middleware chain |
| `ExperimentalEmptyAdapter` | N/A | Adapter is LLM-agnostic; not relevant |
| `useCopilotAction` pattern | ✅ Already better | Native generative UI achieves same result without iframes |
| `deduplicateAssistantDeltas` | Irrelevant | MCP Apps solves a different problem |
| .NET Aspire + service discovery | ❌ No direct analogue | MCP server is standalone Express, no Aspire |
| AG-UI SSE streaming | ⚠️ Partial | `ACTIVITY_SNAPSHOT` could theoretically be injected into the SSE stream |

---

## 5. What CoffeeShop Already Has vs. What MCP Apps Provides

The CoffeeShop application already implements the **functional equivalent** of what MCP Apps provides, using CopilotKit's native capabilities:

| MCP Apps Feature | CoffeeShop Equivalent |
|------------------|-----------------------|
| Interactive menu browsing in chat | `updateMenu` action → `MenuGrid` component |
| Order form with selections | `updateOrderSummary` action → `OrderSummaryPanel` |
| Customer identification display | `setCustomer` action → `CustomerPanel` |
| State persistence in conversation | `useCopilotReadable` exports state to agent context |
| Bidirectional agent↔UI communication | `useCopilotAction` handlers update React state |

**CoffeeShop's approach is actually more integrated** because:
1. React components have full access to the application state
2. Components are styled consistently with the application
3. No sandboxed iframe limitations (clipboard, storage, navigation)
4. Components can trigger other frontend actions directly
5. TypeScript types are shared between agent and UI

---

## 6. When MCP Apps *Would* Make Sense for CoffeeShop

MCP Apps would be valuable if:

1. **Third-party MCP servers** provided tools CoffeeShop wanted to consume (e.g., a payment processing MCP server with a checkout UI, or a delivery tracking MCP server with a map UI) — these could be added via a `BuiltInAgent` sidecar
2. **CoffeeShop were rewritten as a pure TypeScript stack** without .NET — then `BuiltInAgent` + `MCPAppsMiddleware` would be the natural architecture
3. **The .NET AG-UI ecosystem released native MCP Apps support** — Microsoft would need to add `ACTIVITY_SNAPSHOT` support to `MapAGUI` and provide a .NET MCP Apps SDK
4. **CopilotKit added middleware support to HttpAgent** — allowing `MCPAppsMiddleware` to be attached to HTTP-based agent proxies

---

## 7. Detailed Technical Notes

### 7.1. MCPAppsMiddleware Internals

The `@ag-ui/mcp-apps-middleware` package (v0.0.3, 78.1 kB unpacked) does the following:

1. **On agent initialization**: Connects to configured MCP servers, discovers all tools that have `_meta.ui/resourceUri`, and injects them into the agent's available tools
2. **On tool call**: If the called tool has a `ui/resourceUri`, the middleware:
   - Calls the tool on the MCP server
   - Reads the resource HTML from the MCP server
   - Emits an `ACTIVITY_SNAPSHOT` with type `"mcp-apps"` containing: `result`, `resourceUri`, `serverHash`/`serverId`, `toolInput`
   - Sets `replace: true` so the activity snapshot replaces any previous version
3. **On proxied request**: Handles `forwardedProps.__proxiedMCPRequest` from the frontend, forwarding JSON-RPC calls from the iframe to the correct MCP server

### 7.2. MCP Server Resource MIME Type

The magic MIME type `text/html+mcp` signals to the host that this is an MCP App (not a regular HTML document). Host clients use this to decide whether to render the resource in an iframe.

### 7.3. MCP Apps HTML Structure

Each app is a **self-contained HTML file** (built by Vite + `vite-plugin-singlefile`):
- Inline CSS, JavaScript, SVG icons — no external dependencies
- Uses `window.parent.postMessage()` for JSON-RPC communication with the host
- Receives tool call results via `message` event listener
- Has a glassmorphism design system with CSS variables
- Fully accessible (keyboard navigation, ARIA labels)

### 7.4. Session Management

The MCP server maintains per-connection sessions via `StreamableHTTPServerTransport`:
- Each MCP connection gets a unique `Mcp-Session-Id` header
- In-memory event store enables resumability (reconnection without losing state)
- Session cleanup on transport close

### 7.5. CopilotKit Version Differences

The mcp-apps-demo uses:
- `@copilotkitnext/*@1.51.0-next.4` (pre-release)
- `BuiltInAgent` from `@copilotkitnext/agent`
- `createCopilotEndpoint` (Hono-based) instead of `copilotRuntimeNextJSAppRouterEndpoint`
- `InMemoryAgentRunner` instead of `ExperimentalEmptyAdapter`

CoffeeShop uses:
- `@copilotkit/*@1.51.4` (release)
- `ExperimentalEmptyAdapter` (no local LLM, LLM is in .NET backend)
- `copilotRuntimeNextJSAppRouterEndpoint` (Next.js App Router)
- `HttpAgent` from `@ag-ui/client` (AG-UI proxy to .NET)

---

## 8. Conclusions and Recommendations

### 8.1. Primary Finding

**MCP Apps cannot be directly integrated into the CoffeeShop application** in its current architecture. The fundamental blocker is the agent architecture:

- MCP Apps require `MCPAppsMiddleware` to be attached to an agent that runs in the JavaScript runtime
- CoffeeShop's agent (`GitHubCopilotAgent`) runs in a .NET process, accessed via `HttpAgent` (a pass-through proxy)
- `HttpAgent` does not support middleware chains (`.use()` is not available)

### 8.2. Recommendation

**Continue with the current `useCopilotAction` + React component approach.** It provides:
- Better integration with the application's React state
- No iframe sandboxing limitations
- Consistent styling with the application
- Full TypeScript type safety between agent and UI
- No additional MCP server infrastructure required

### 8.3. Future Watch

Monitor these developments for potential MCP Apps integration:
1. **`@ag-ui/mcp-apps-middleware` support for `HttpAgent`** — if CopilotKit adds middleware support to remote agent proxies
2. **.NET MCP Apps SDK** — if Microsoft releases MCP Apps support for MAF/Agents
3. **CopilotKit's `copilotRuntimeNextJSAppRouterEndpoint` middleware pipeline** — if the Next.js endpoint handler gets middleware support independent of agent type

### 8.4. What We Learned That's Useful

Even though MCP Apps don't fit CoffeeShop's architecture, the research reveals useful patterns:

1. **`structuredContent` in tool results** — MCP server tools return both text (for the LLM) and structured data (for the UI). CoffeeShop's agents already do this implicitly via `useCopilotAction`, but the pattern validates our approach.

2. **Activity Snapshots** — AG-UI's `ACTIVITY_SNAPSHOT` event type is the standard way to deliver generative UI content. If CoffeeShop ever needs to emit custom UI from the .NET backend (beyond what `useCopilotAction` supports), this is the mechanism to explore.

3. **Version alignment matters** — mcp-apps-demo uses `@copilotkitnext` (next channel), while CoffeeShop uses `@copilotkit` (stable). The `BuiltInAgent`, `InMemoryAgentRunner`, and `createCopilotEndpoint` APIs are only available in the next channel. This is worth watching for future upgrades.

---

## Appendix A: mcp-apps-demo File Structure

```
mcp-apps-demo/
├── src/app/
│   ├── page.tsx                          # Landing page + CopilotSidebar
│   └── api/copilotkit/[[...slug]]/
│       └── route.ts                      # BuiltInAgent + MCPAppsMiddleware + CopilotRuntime
├── mcp-server/
│   ├── server.ts                         # MCP server: tools, resources, Express, sessions
│   ├── src/
│   │   ├── flights.ts                    # 15 airports, 6 airlines, booking logic
│   │   ├── hotels.ts                     # 10 cities, 30 hotels, room selection
│   │   ├── stocks.ts                     # 18 stocks, portfolio management
│   │   └── kanban.ts                     # Board templates, card CRUD, drag-drop
│   └── apps/
│       ├── flights-app.html              # Self-contained airline booking wizard
│       ├── hotels-app.html               # Self-contained hotel booking wizard
│       ├── trading-app.html              # Self-contained investment simulator
│       └── kanban-app.html               # Self-contained kanban board
├── package.json                          # @copilotkitnext/*, @ag-ui/mcp-apps-middleware
├── next.config.ts                        # reactStrictMode: false
└── Dockerfile                            # Dual-container deployment
```

## Appendix B: Key Package Versions

| Package | mcp-apps-demo | CoffeeShop |
|---------|---------------|------------|
| `@copilotkit/runtime` | N/A (uses `@copilotkitnext/runtime@1.51.0-next.4`) | `1.51.4` |
| `@copilotkit/react-core` | N/A (uses `@copilotkitnext/react@1.51.0-next.4`) | `1.51.4` |
| `@ag-ui/client` | `^0.0.42` | `^0.0.42` |
| `@ag-ui/mcp-apps-middleware` | `^0.0.1` | Not installed |
| `next` | `16.1.1` | `^16.0.8` |
| `react` | `19.2.3` | `^19.2.1` |
| `hono` | `^4.11.3` | Not used |
| `@modelcontextprotocol/sdk` | In mcp-server | Not used |
