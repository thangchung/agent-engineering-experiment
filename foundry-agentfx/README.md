# foundry-agentfx

Coffeeshop AI agent monorepo. Three services wired together via Aspire:

- **Coffeeshop.Mcp** — MCP server (menu, ordering, customer lookup)
- **ToolSearch.Gateway** — Tool-Search-Tool gateway (hides all backend tools behind `search_tools` + `call_tool`)
- **Claw.Api** — Agent host (Microsoft Agent Framework, Foundry or Copilot provider, Slack + Web channels)

```
User ─► Claw.Api ──► ToolSearch.Gateway ──► Coffeeshop.Mcp
                           └──────────────► Foundry IQ (optional)
                           └──────────────► Foundry Toolbox (optional)
```

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [.NET Aspire workload](https://learn.microsoft.com/dotnet/aspire/fundamentals/setup-tooling): `dotnet workload install aspire`
- [Azure CLI](https://docs.microsoft.com/cli/azure/install-azure-cli) + [azd](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd) (cloud deployment)
- GitHub account with Copilot access (local provider)

> **macOS/Linux note:** `postprovision` hook uses `sh`. No PowerShell needed.

---

## Cloud Deployment with `azd`

```bash
az login && azd auth login
azd init                              # set env name
azd env set AZURE_LOCATION eastus2   # must support Foundry: eastus2, westus, westus3, canadacentral, swedencentral, francecentral, norwayeast, australiaeast, and others. 
# check https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/hosted-agents#region-availability
```

### Local dev (infra only)

Provision cloud infra only, run all 3 services locally via Aspire:

```bash
azd env set SKIP_CONTAINER_APPS true
azd provision   # creates Foundry, ACR, AI Search, Key Vault, monitoring — no Container Apps

# Wire outputs into Aspire user-secrets
FOUNDRY_ENDPOINT=$(azd env get-values | grep FOUNDRY_PROJECT_ENDPOINT | cut -d= -f2)
SEARCH_ENDPOINT=$(azd env get-values | grep AZURE_AI_SEARCH_SERVICE_ENDPOINT | cut -d= -f2)
APP_INSIGHTS=$(azd env get-values | grep APPLICATIONINSIGHTS_CONNECTION_STRING | cut -d= -f2)

dotnet user-secrets set "Parameters:agent-provider"                "foundry"
dotnet user-secrets set "Parameters:foundry-endpoint"              "$FOUNDRY_ENDPOINT"
dotnet user-secrets set "Parameters:foundry-iq-endpoint"           "$SEARCH_ENDPOINT"
dotnet user-secrets set "Parameters:foundry-iq-kb-name"            "coffeeshop-kb"
dotnet user-secrets set "Parameters:toolbox-endpoint"              "${SEARCH_ENDPOINT}/knowledgebases/coffeeshop-kb/mcp?api-version=2025-11-01-preview"
dotnet user-secrets set "Parameters:appinsights-connection-string" "$APP_INSIGHTS"
dotnet user-secrets set "Parameters:slack-bot-token"               "xoxb-..."   # optional
dotnet user-secrets set "Parameters:brave-search-api-key"          "<key>"      # optional

dotnet aspire run
```

### Standard mode (Container Apps)

```bash
# Optional overrides before provision
azd env set BRAVE_SEARCH_API_KEY "<key>"

# Optional: Slack integration
azd env set SLACK_BOT_TOKEN    "xoxb-..."
azd env set SLACK_APP_TOKEN    "xapp-..."
azd env set SLACK_SIGNING_SECRET "<signing-secret>"

azd up   # provision infra + build images + deploy all 3 Container Apps
```

`postprovision` hook auto-seeds `coffeeshop-kb` knowledge base + registers Foundry Toolbox.

---

### Foundry Hosted Agent mode (optional)

`claw-api` deploys as a Foundry Hosted Agent. After `azd deploy --service claw-api`, a `postdeploy` hook automatically calls the Foundry API to register the agent using your local credentials — no deploymentScript, no timeout issues.

```bash
# 1. Set flag + provision infra
azd env set ENABLE_HOSTED_FOUNDRY true
azd env set SKIP_CONTAINER_APPS false

azd env set BRAVE_SEARCH_API_KEY "..."

azd env set SLACK_BOT_TOKEN    "xoxb-..."
azd env set SLACK_APP_TOKEN    "xapp-..."
azd env set SLACK_SIGNING_SECRET "<signing-secret>"

azd provision

# 2. Grant yourself "Foundry Project Manager" on the AI project
AI_PROJECT=$(az resource list -g rg-<env> --resource-type Microsoft.CognitiveServices/accounts/projects --query "[0].id" -o tsv)
MY_OID=$(az ad signed-in-user show --query id -o tsv)
az role assignment create --assignee $MY_OID --role "Foundry Project Manager" --scope $AI_PROJECT

# 3. Deploy all services — claw-api postdeploy hook registers agent automatically
azd deploy

# 4. (Optional) Check agent status
az rest --method GET \
  --url "$(azd env get-values | grep AZURE_AI_PROJECT_ENDPOINT | cut -d= -f2 | tr -d '"')/agents/claw-api?api-version=2025-11-15-preview" \
  --resource https://ai.azure.com \
  --query status -o tsv
```

Visible in Foundry Portal → Overview (token usage, agent runs, traces).

**Invoke:**

```bash
TOKEN=$(az account get-access-token --resource https://ai.azure.com --query accessToken -o tsv)
ENDPOINT=$(azd env get-values | grep FOUNDRY_PROJECT_ENDPOINT | cut -d= -f2 | tr -d '"')

curl -X POST "$ENDPOINT/agents/claw-api/endpoint/protocols/invocations?api-version=v1" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Foundry-Features: CodeAgents=V1Preview,HostedAgents=V1Preview" \
  -d '{"input":"I want a large oat milk latte"}'
```

**Rollback to Container Apps:**

```bash
azd env set ENABLE_HOSTED_FOUNDRY false && azd up
```

---

## GitHub Actions CI/CD

The workflow at `.github/workflows/azure-deploy.yml` builds, tests, provisions, and deploys on every push to `main`.

### Required repository variables

Set these under **Settings → Secrets and variables → Actions → Variables** in your GitHub repo:

| Variable | Description |
|----------|-------------|
| `AZURE_CLIENT_ID` | Client ID of the service principal / managed identity used for OIDC login |
| `AZURE_TENANT_ID` | Azure AD tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID |
| `AZURE_ENV_NAME` | azd environment name (e.g. `prod`). Defaults to `prod` if not set |
| `AZURE_LOCATION` | Azure region (e.g. `eastus2`). Defaults to `eastus2` if not set |

Set these under **Settings → Secrets and variables → Actions → Secrets**:

| Secret | Description |
|--------|-------------|
| `BRAVE_SEARCH_API_KEY` | _(optional)_ Brave Search API key — enables the `web_search` tool |

### Setting up OIDC federated credentials

The workflow uses [OpenID Connect (OIDC)](https://docs.github.com/en/actions/security-for-github-actions/security-hardening-your-deployments/about-security-hardening-with-openid-connect) — no long-lived secrets needed.

```bash
# 1. Create a service principal (or use existing)
SP=$(az ad sp create-for-rbac --name "foundry-agentfx-gh" --role Contributor \
  --scopes /subscriptions/<subscription-id> --json-auth)

# 2. Note the clientId, tenantId, subscriptionId from the output
CLIENT_ID=$(echo $SP | jq -r .clientId)

# 3. Add federated credential for GitHub Actions
az ad app federated-credential create \
  --id $CLIENT_ID \
  --parameters '{
    "name": "foundry-agentfx-main",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:<owner>/<repo>:ref:refs/heads/main",
    "audiences": ["api://AzureADTokenExchange"]
  }'
```

Then set `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` as repo variables.

### What the workflow does

1. **Build & test** — `dotnet build` + `dotnet test` against `foundry-agentfx.slnx`
2. **OIDC login** — authenticates to Azure via federated credentials (no stored secrets)
3. **Provision** — `azd up` runs Bicep templates (idempotent; no-op if infra unchanged)
4. **Deploy** — builds Docker images, pushes to ACR, updates Container App revisions
5. **Post-provision hook** — `infra/hooks/postprovision.sh` runs `create-search-indexes.py` to seed the knowledge base

> **Slack tokens**: Set them via `azd env set SLACK_BOT_TOKEN ...` before deploying. The `claw-api` Container App reads them from azd env at provision time.

---

## Architecture

```
┌─────────────┐     POST /api/chat      ┌────────────────────┐
│   Browser   │ ──────────────────────► │                    │
│   (SSE)     │ ◄────────────────────── │    Claw.Api        │
└─────────────┘     streaming text      │  (CoffeeshopWork-  │
                                        │   flow: MAF        │
┌─────────────┐     Slack socket mode   │   concurrent       │
│    Slack    │ ◄──────────────────────►│   ordering+audit)  │
└─────────────┘                         └────────┬───────────┘
                                                 │ search_tools / call_tool
                                                 ▼
                                        ┌────────────────────┐
                                        │  ToolSearch.       │
                                        │  Gateway           │
                                        │  (WeightedTool-    │
                                        │   Searcher +       │
                                        │   ToolRegistry)    │
                                        └──┬──────────┬──────┘
                                           │          │
                              HTTP MCP     │          │  HTTP REST
                                           ▼          ▼
                                  ┌──────────────┐  ┌──────────────┐
                                  │ Coffeeshop   │  │ Foundry IQ / │
                                  │ .Mcp         │  │ Toolbox      │
                                  │ (menu/order/ │  │ (optional)   │
                                  │  customer)   │  └──────────────┘
                                  └──────────────┘
```

---

## Project Structure

```
foundry-agentfx/
├── apphost.cs                   # Aspire AppHost (single-file)
├── appsettings.json             # AppHost parameter defaults
├── appsettings.Development.json # Local dev overrides (gitignored)
├── azure.yaml                   # azd service definitions
├── infra/
│   ├── main.bicep               # Subscription-scoped entry point
│   ├── main.parameters.json     # azd parameter bindings
│   ├── core/
│   │   ├── ai/
│   │   │   ├── ai-project.bicep # CognitiveServices/accounts + project + monitoring
│   │   │   └── connection.bicep # Reusable Foundry project connection module
│   │   ├── search/
│   │   │   └── azure-ai-search.bicep # AI Search + RBAC + KB MCP connection
│   │   ├── storage/
│   │   │   └── storage.bicep    # Storage account + RBAC + Foundry connection
│   │   └── monitor/
│   │       ├── applicationinsights.bicep
│   │       └── loganalytics.bicep
│   ├── modules/
│   │   ├── container-apps.bicep # Container Apps environment + 3 services (gated by enableHostedFoundry)
│   │   └── register-agent.sh          # postdeploy hook: registers claw-api as Foundry Hosted Agent
│   │   ├── container-registry.bicep
│   │   └── keyvault.bicep       # Slack tokens (bot, app, signing-secret)
│   ├── hooks/
│   │   └── postprovision.sh     # Runs search index + toolbox scripts
│   ├── create-search-indexes.py # Seeds coffeeshop-kb knowledge base (company info)
│   └── create-toolbox.py        # Creates Foundry Toolbox (preview API)
├── data/
│   └── index-data/
│       └── coffeeshop-exported.jsonl # KB seed data: FAQ, hours, policies, loyalty, careers
└── src/
    ├── Coffeeshop.Mcp/          # MCP tool server
    ├── ToolSearch.Gateway/      # Tool-search gateway
    ├── Claw.Api/                # Agent + channels
    │   └── Agents/
    │       ├── CoffeeshopWorkflow.cs      # MAF concurrent workflow (ordering + audit)
    │       └── CoffeeshopInvocationHandler.cs # Foundry Hosted Agent invocations bridge
    ├── Claw.Core/               # IToolSearchClient
    ├── Coffeeshop.Models/       # Shared record types
    └── ServiceDefaults/         # Aspire + OTel defaults
```
