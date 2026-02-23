# Quickstart: CoffeeShop Ordering System

**Date**: 2026-02-23
**Branch**: `001-coffeeshop-ordering`

---

## Prerequisites

| Tool | Version | Check | Install |
|------|---------|-------|---------|
| .NET SDK | 10.x | `dotnet --version` | https://dotnet.microsoft.com/download |
| .NET Aspire workload | 13.x | `dotnet workload list` | `dotnet workload install aspire` |
| Node.js | 20 LTS | `node --version` | https://nodejs.org |
| GitHub Copilot CLI | latest | `gh copilot --version` | `gh extension install github/gh-copilot` |
| GitHub CLI (`gh`) | latest | `gh --version` | https://cli.github.com |

> **Copilot CLI is required for the MAF + Copilot SDK integration.** If it is
> not installed or not in PATH, the counter startup will throw
> `FileNotFoundException` with a clear message.

### Authenticate GitHub Copilot

```bash
gh auth login
gh copilot --version   # verify extension is active
```

---

## Repository Structure

```
coffeeshop/
├── apphost/          # Start here: dotnet run --project apphost/
├── servicedefaults/
├── backend/
│   ├── counter/
│   ├── barista/
│   ├── kitchen/
│   └── product-catalog/
├── frontend/         # Next.js
├── tests/
│   ├── integration/
│   └── unit/
└── CoffeeShop.sln
```

---

## Setup

### 1. Install .NET dependencies

```bash
dotnet restore CoffeeShop.sln
```

### 2. Install frontend dependencies

```bash
cd frontend
npm install
```

### 3. (Optional) Seed user secrets for Copilot SDK sessions

Only required if using the direct `CreateSessionAsync` chat-turn path
(MAF path via `AsAIAgent()` does not need this):

```bash
cd backend/counter
dotnet user-secrets set "GITHUB_TOKEN" "ghp_yourtoken"
```

---

## Run (Full Stack)

```bash
dotnet run --project apphost/
```

This single command:
1. Starts the Aspire Dashboard (http://localhost:18888 by default)
2. Starts `product-catalog` (MCP Server over HTTP/SSE)
3. Starts `counter` (Minimal API + MAF agent, wired to product-catalog)
4. Starts `barista` (Channel<T> consumer)
5. Starts `kitchen` (Channel<T> consumer)
6. Starts `frontend` (Next.js dev server via `npm run dev`, with counter URL injected)

All service discovery URLs are injected automatically by Aspire.

### Access the app

| Service | URL (example) |
|---------|--------------|
| Frontend | http://localhost:3000 (Aspire-assigned) |
| Counter API | http://localhost:5001 (Aspire-assigned) |
| product-catalog MCP | http://localhost:5002 (Aspire-assigned) |
| Aspire Dashboard | http://localhost:18888 |

> Ports are dynamically assigned by Aspire. Check the dashboard for actual
> values. Never hard-code them in application code.

---

## Run Tests

### Unit tests

```bash
# Backend
dotnet test tests/unit/

# Frontend
cd frontend && npm run test
```

### Integration tests (requires full stack)

```bash
dotnet test tests/integration/
```

Integration tests use `DistributedApplicationTestingBuilder` — they start
the full Aspire AppHost stack automatically. No manual startup required.

---

## Quick Smoke Test

With the full stack running:

```bash
# 1. Look up a customer
curl -s -X POST http://localhost:5001/api/v1/customers/lookup \
  -H "Content-Type: application/json" \
  -d '{"identifier":"alice@example.com"}' | jq .

# 2. Get the menu
curl -s http://localhost:5001/api/v1/menu | jq .items[].displayName

# 3. Place an order
curl -s -X POST http://localhost:5001/api/v1/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId":"C-1001","items":[{"type":"LATTE","quantity":1}]}' | jq .

# 4. Check order status (replace ORD-XXXX with the returned orderId)
curl -s http://localhost:5001/api/v1/orders/ORD-XXXX | jq .status

# 5. Get order history
curl -s http://localhost:5001/api/v1/customers/C-1001/orders | jq .orders[].status
```

---

## Development Notes

- **Modify code**: Save → Aspire hot-reloads .NET projects. Frontend uses Next.js Fast Refresh.
- **Telemetry**: Open the Aspire Dashboard to see distributed traces, logs, and metrics for every request.
- **Seed data**: Reset by restarting any service (state is in-memory, no persistence).
- **MAF agent instructions**: Edit `Application/Agents/` in each backend component. Agent instructions are string constants — no file or DB access.
- **CopilotKit generative UI**: Add components to `frontend/src/components/copilot/` and register via `useCopilotAction`.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| `FileNotFoundException` on counter startup | Copilot CLI not installed or not in PATH | `gh extension install github/gh-copilot` |
| Menu returns 503 | product-catalog not yet running | Wait for Aspire to start all resources; check dashboard |
| Frontend can't reach counter | Counter URL not injected | Ensure `WithReference(counter)` in AppHost `Program.cs` |
| Integration tests fail immediately | Resources not reaching Running state | Increase `WaitForResourceAsync` timeout in test setup |
