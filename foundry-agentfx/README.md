# foundry-agentfx

Coffeeshop AI agent. **4 services** wired via Aspire:

| Service | Role | Host |
|---------|------|------|
| **Coffeeshop.Mcp** | MCP tool server (menu, orders, customers) | ACA |
| **ToolSearch.Gateway** | Hides all tools behind `search_tools` + `call_tool` | ACA |
| **Claw.Api** | AI brain — MAF + Foundry provider, `/invocations` endpoint | Foundry Hosted Agent |
| **Claw.Slack** | Thin Slack adapter — Socket Mode → Foundry → reply | ACA |

```
Slack ──► Claw.Slack ──► Foundry Hosted Agent (claw-api)
Browser ──► /api/chat ─────────────────────────────┘
                              │
                    ToolSearch.Gateway (public FQDN)
                         │           │
                  Coffeeshop.Mcp   Foundry IQ / Toolbox (optional)
```

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- `dotnet workload install aspire`
- [Azure CLI](https://docs.microsoft.com/cli/azure/install-azure-cli) + [azd](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd)

---

## Local dev (Aspire)

```bash
# Wire secrets (one-time)
dotnet user-secrets set "Parameters:agent-provider"                "foundry"
dotnet user-secrets set "Parameters:foundry-endpoint"              "<AZURE_AI_PROJECT_ENDPOINT>"
dotnet user-secrets set "Parameters:foundry-model"                 "gpt-5.4-mini"
dotnet user-secrets set "Parameters:foundry-iq-endpoint"           "<AZURE_AI_SEARCH_ENDPOINT>"
dotnet user-secrets set "Parameters:foundry-iq-kb-name"            "coffeeshop-kb"
dotnet user-secrets set "Parameters:appinsights-connection-string" "<APPINSIGHTS_CONNECTION_STRING>"
dotnet user-secrets set "Parameters:slack-bot-token"               "xoxb-..."   # optional
dotnet user-secrets set "Parameters:slack-app-token"               "xapp-..."   # optional
dotnet user-secrets set "Parameters:slack-signing-secret"          "..."        # optional
dotnet user-secrets set "Parameters:brave-search-api-key"          "<key>"      # optional

# Run all 4 services
dotnet aspire run
```

`claw-api` runs on :5000 with `/invocations` endpoint (gated by `Agent__HostedMode=foundry`).  
`claw-slack` runs on :5003 and calls `claw-api` via Aspire service discovery.

Test invocations locally:
```bash
curl -X POST http://localhost:5000/invocations \
  -H "Content-Type: application/json" \
  -d '{"input":"list the menu"}'
```

---

## Cloud deployment

### Manual deploy (step-by-step)

```bash
az login && azd auth login
azd env new <env-name>
azd env set AZURE_LOCATION eastus2   # must support Foundry Hosted Agents

# Slack tokens
azd env set SLACK_BOT_TOKEN     "xoxb-..."
azd env set SLACK_APP_TOKEN     "xapp-..."
azd env set SLACK_SIGNING_SECRET "..."

# Optional
azd env set BRAVE_SEARCH_API_KEY "<key>"

# Step 1: provision infra only (creates ACR, Foundry project, App Insights, etc.)
azd provision

# Step 2: build claw-api image and push to ACR
#   Must happen BEFORE azd deploy — the postdeploy hook registers claw-api from this image
ACR=$(azd env get-values | grep ^AZURE_CONTAINER_REGISTRY_NAME | cut -d= -f2 | tr -d '"')
az acr build \
  --registry "$ACR" \
  --image "foundry-agentfx/claw-api-<env-name>:latest" \
  --file src/Claw.Api/Dockerfile \
  .

# Step 3: deploy ACA services + run postdeploy hook (registers claw-api as Foundry Hosted Agent)
azd deploy
```

**Why this order?**
- `azd provision` creates the ACR (you need it before you can push)
- `azd deploy` builds+pushes claw-slack/coffeeshop/gateway images, then runs `register-agent.sh`
- `register-agent.sh` looks for the claw-api image in ACR — it must already be there

`claw-api` is NOT deployed by azd as a Container App. It lives on Foundry's compute. `azd deploy` only handles claw-slack, coffeeshop-mcp, toolsearch-gateway.

After deploy, `postdeploy` hook (`register-agent.sh`) automatically:
1. Finds latest `claw-api` image in ACR
2. Registers it as Foundry Hosted Agent (injects `Agent__Provider=foundry`, `Agent__HostedMode=foundry`, gateway URL)

> **CI/CD handles all of this automatically** — see GitHub Actions section below.
> CI/CD handles this automatically on subsequent pushes.

### Invoke agent (cloud)

```bash
TOKEN=$(az account get-access-token --resource https://ai.azure.com --query accessToken -o tsv)
ENDPOINT=$(azd env get-values | grep AZURE_AI_PROJECT_ENDPOINT | cut -d= -f2 | tr -d '"')

curl -X POST "$ENDPOINT/agents/claw-api/endpoint/protocols/invocations?api-version=v1" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Foundry-Features: HostedAgents=V1Preview" \
  -d '{"input":"I want a large oat milk latte"}'
```

### Check agent status

```bash
az rest --method GET \
  --url "$ENDPOINT/agents/claw-api?api-version=2025-11-15-preview" \
  --resource https://ai.azure.com \
  --query status -o tsv
```

---

## GitHub Actions CI/CD

Workflow: `.github/workflows/azure-deploy.yml` — triggers on push to `main`.

**4 jobs (mirrors the manual order):**
1. `build-and-test` — dotnet build + test all projects
2. `provision` — `azd provision` (creates infra; outputs ACR name + Foundry endpoint)
3. `build-claw-api-image` — `az acr build` pushes claw-api image to ACR
4. `deploy` — `azd deploy` (builds+pushes claw-slack/coffeeshop/gateway; postdeploy hook registers claw-api) → polls agent status → smoke test

### Required repo variables (Settings → Actions → Variables)

| Variable | Value |
|----------|-------|
| `AZURE_CLIENT_ID` | Service principal client ID (OIDC) |
| `AZURE_TENANT_ID` | Azure AD tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Subscription ID |
| `AZURE_ENV_NAME` | azd env name (e.g. `prod`) |
| `AZURE_LOCATION` | Region (e.g. `eastus2`) |

### Required secrets (Settings → Actions → Secrets)

| Secret | Purpose |
|--------|---------|
| `SLACK_BOT_TOKEN` | Slack bot token (`xoxb-...`) |
| `SLACK_APP_TOKEN` | Slack app-level token (`xapp-...`) |
| `SLACK_SIGNING_SECRET` | Slack signing secret |
| `BRAVE_SEARCH_API_KEY` | _(optional)_ enables `web_search` tool |

### OIDC setup (one-time)

```bash
SP=$(az ad sp create-for-rbac --name "foundry-agentfx-gh" --role Contributor \
  --scopes /subscriptions/<sub-id> --json-auth)
CLIENT_ID=$(echo $SP | jq -r .clientId)

az ad app federated-credential create --id $CLIENT_ID --parameters '{
  "name": "foundry-agentfx-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<owner>/<repo>:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

---

## Project structure

```
foundry-agentfx/
├── apphost.cs                        # Aspire AppHost (4 services)
├── azure.yaml                        # azd service definitions (claw-slack, coffeeshop-mcp, toolsearch-gateway)
├── infra/
│   ├── main.bicep                    # Subscription-scoped entry; wires all modules
│   ├── modules/
│   │   └── container-apps.bicep     # ACA env + 3 services + RBAC for claw-slack
│   ├── hooks/
│   │   ├── postprovision.sh         # Seeds coffeeshop-kb + Foundry Toolbox
│   │   └── register-agent.sh        # Registers claw-api as Foundry Hosted Agent
│   └── core/ai/ai-project.bicep     # Foundry project + App Insights (auto-injects connection string)
└── src/
    ├── Claw.Api/                     # Foundry Hosted Agent: MAF workflow + /invocations endpoint
    ├── Claw.Slack/                   # Slack adapter: FoundryAgentClient → Foundry invocations
    ├── Coffeeshop.Mcp/              # MCP tool server
    ├── ToolSearch.Gateway/          # Tool-search gateway
    ├── Claw.Core/                   # Shared runtime interfaces
    ├── Coffeeshop.Models/           # Domain types
    └── ServiceDefaults/             # Aspire + OTel defaults
```
