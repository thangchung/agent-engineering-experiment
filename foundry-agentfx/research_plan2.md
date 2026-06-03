# Research: GitHub Actions CI/CD for foundry-agentfx

## Context

3 services:
- `coffeeshop-mcp` → ACA (internal)
- `toolsearch-gateway` → ACA (internal)
- `claw-agent` → ACA + Foundry Hosted Agent registration (external)

Current state: single `azure-deploy.yml` does `azd up --no-prompt` (provision + deploy in one shot). Works but primitive.

---

## Source Repos Analyzed

| Repo | Type | Key Pattern |
|------|------|-------------|
| `ericchansen/foundry-agents-lifecycle` | Code-based agent (Python) | Full CI/CD: lint → test → deploy → evaluate → promote |
| `leestott/foundry-cicd` | Container-based + code agent | Reference pipeline YAML only (no actual scripts) |
| `microsoft-foundry/foundry-samples/infrastructure/infrastructure-setup-bicep` | Infra-only (Bicep) | 40+ template variants for every Azure AI config |

---

## 1. ericchansen/foundry-agents-lifecycle — Best Practices

### Architecture

```
ci.yml (PR) ─→ lint + test + dry-run (no Azure auth needed)
cd.yml (push main) ─→ deploy-dev → deploy-test → deploy-prod
                         │              │              │
                    (auto)       (approval)     (strict approval)
```

### Key Patterns

**Agent Registration via SDK (not REST):**
```python
from azure.ai.projects import AIProjectClient
from azure.ai.projects.models import PromptAgentDefinition

client = AIProjectClient(endpoint=project_endpoint, credential=DefaultAzureCredential())
agents_client = client.agents
agent = agents_client.create_version(
    agent_name=config.name,
    definition=PromptAgentDefinition(model=..., instructions=..., tools=...),
    metadata={"environment": env, "git_sha": sha}
)
```

**Environment-per-config files:**
```
config/agent-config.dev.json   → gpt-4o-mini, eval threshold 3.0
config/agent-config.test.json  → gpt-4o-mini, eval threshold 3.0
config/agent-config.prod.json  → gpt-4o, eval threshold 4.0
```

**Infra deployed every run:**
```bash
az deployment sub create \
  --template-file infra/deploy-infra.bicep \
  --parameters environment=dev pipelineSource=github \
  --parameters infra/environments/dev.parameters.json
PROJECT_ENDPOINT=$(jq -r '.projectEndpoint.value' /tmp/infra-outputs.json)
```

**RBAC Critical:** `Cognitive Services User` (not `Azure AI Developer`) required for `agents/*` CRUD.
- GUID: `a97b65f3-24c7-4388-baec-2e87135dc908`

**Project endpoint derived from Bicep output:**
```bicep
output projectEndpoint string = '${foundryResource.properties.endpoints['AI Foundry API']}api/projects/${foundryProject.name}'
```

### CD Pipeline — Each Environment Job

1. Checkout
2. Python setup + deps
3. `azure/login@v2` (OIDC federated credentials)
4. Deploy infra (Bicep subscription-level)
5. Deploy agent (`create_version()`)
6. Evaluate agent (6 evaluators: coherence, fluency, groundedness, relevance, safety, similarity)
7. Quality gate check (fail if below threshold)

### What They DON'T Do
- No container/hosted agents — purely prompt/code agents
- No ACR image push
- No Dockerfiles
- No `Foundry-Features` headers

---

## 2. leestott/foundry-cicd — Container Agent Pattern (Reference Only)

### 6-Job Pipeline (reference YAML)

```
ci-build-validate → ci-evaluate → cd-deploy-dev → cd-deploy-test → cd-deploy-prod
```

**Job 1 (CI): Build container + push to ACR**
```bash
az acr build \
  --registry $ACR_REGISTRY \
  --image "$AGENT_NAME:$GITHUB_SHA" \
  --file Dockerfile .
```
- Uses `az acr build` (cloud-side build, no local Docker daemon)
- Image tag = git SHA

**Job 3-5 (CD): Deploy agent with image reference**
```bash
VERSION=$(python scripts/deploy_agent.py \
  --env dev \
  --image "$ACR_REGISTRY/$AGENT_NAME:$IMAGE_TAG" \
  --foundry-endpoint $FOUNDRY_ENDPOINT_DEV \
  --agent-config agent.yaml)
```

**Promotion pattern:**
```bash
python scripts/promote_agent.py \
  --from-env dev --to-env test \
  --agent-version $VERSION
```

**Environment variables:**
| Var | Scope | Type |
|-----|-------|------|
| `AZURE_CLIENT_ID/TENANT_ID` | Global | Secret (OIDC) |
| `AZURE_SUBSCRIPTION_ID` | Global | Var |
| `ACR_REGISTRY` | Global | Var |
| `FOUNDRY_ENDPOINT_DEV/TEST/PROD` | Per-env | Var |
| `FOUNDRY_CONNECTION_STRING_DEV/TEST/PROD` | Per-env | Secret |

**Quality gates (per env):**
- Dev: hallucination ≤ 0.05, task-completion ≥ 0.90, p95 ≤ 4000ms
- Test: hallucination ≤ 0.03, task-completion ≥ 0.95, p95 ≤ 3000ms
- Prod: deploy + enable endpoint

### Key Observations
- Scripts referenced but NOT included — consumer must implement
- No Bicep files — assumes infra pre-provisioned
- Uses `AZURE_AI_PROJECTS_CONNECTION_STRING` (older pattern)
- OIDC auth throughout

---

## 3. microsoft-foundry/foundry-samples — Bicep Infrastructure

### Structure (40+ templates)

| Dir | What | API Version |
|-----|------|-------------|
| `00-basic` | Simplest: Account + Project + Model | `2025-06-01` (latest stable) |
| `01-connections` | All connection types (Search, Storage, APIM, Bing, etc) | `2025-04-01-preview` |
| `40-basic-agent-setup` | Platform-managed storage | `2025-04-01-preview` |
| `41-standard-agent-setup` | BYO Storage + CosmosDB + Search | `2025-04-01-preview` |
| `10-19` | Private network variants | Mixed |
| `20-32` | Identity + CMK variants | Mixed |

### 00-basic Pattern (What We Already Have)
```bicep
resource aiFoundry 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  kind: 'AIServices'
  properties: { allowProjectManagement: true, customSubDomainName: name }
}
resource aiProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = { parent: aiFoundry }
resource modelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = { parent: aiFoundry }
```

### 41-standard-agent-setup (Full Module Chain)

```
main.bicep
├── validate-existing-resources.bicep   # check BYO resource IDs
├── standard-dependent-resources.bicep  # CosmosDB + AI Search + Storage
├── ai-account-identity.bicep           # CognitiveServices + model deploy
├── ai-project-identity.bicep           # Project + connections (AAD auth)
├── format-project-workspace-id.bicep   # Extract GUID from internalId
├── ai-search-role-assignments.bicep    # Search Index Data Contributor
├── azure-storage-account-role-assignment.bicep  # Blob Data Contributor
├── add-project-capability-host.bicep   # ← KEY: enables Standard agent
├── blob-storage-container-role-assignments.bicep
└── cosmos-container-role-assignments.bicep
```

**Capability Host (Standard agent differentiator):**
```bicep
resource accountCapabilityHost 'Microsoft.CognitiveServices/accounts/capabilityHosts@2025-04-01-preview' = {
  parent: account
  properties: { capabilityHostKind: 'Agents' }
}
resource projectCapabilityHost 'Microsoft.CognitiveServices/accounts/projects/capabilityHosts@2025-04-01-preview' = {
  parent: project
  properties: {
    capabilityHostKind: 'Agents'
    vectorStoreConnections: [searchConnectionName]
    storageConnections: [storageConnectionName]
    threadStorageConnections: [cosmosConnectionName]
  }
}
```

### Connection Pattern (All Types Use Same Shape)
```bicep
resource conn 'Microsoft.CognitiveServices/accounts/connections@2025-04-01-preview' = {
  parent: project
  properties: {
    category: 'CognitiveSearch'  // or 'AzureStorageAccount', 'CosmosDB', 'AppInsights'
    target: endpoint_url
    authType: 'AAD'  // Standard = AAD; Basic = ApiKey
    isSharedToAll: true
    metadata: { ApiType: 'Azure', ResourceId: resource.id }
  }
}
```

### Role Assignments (Critical for CI/CD Service Principal)

| Role | GUID | Purpose |
|------|------|---------|
| Cognitive Services User | `a97b65f3-24c7-4388-baec-2e87135dc908` | Agent CRUD |
| Search Index Data Contributor | `8ebe5a00-799e-43f5-93ac-243d3dce84a7` | Search indexing |
| Search Service Contributor | `7ca78c08-252a-4471-8644-bb5ff32d4ba0` | Search management |
| Storage Blob Data Contributor | `ba92f5b4-2d11-453d-a403-e96b0029c9fe` | Storage access |
| AcrPull | `7f951dda-4ed3-4680-a7ca-43fe172d538d` | Container image pull |

---

## 4. How This Maps to Our Project

### Current State (foundry-agentfx)

```yaml
# azure-deploy.yml — single job, no gates
jobs:
  build-and-deploy:
    - Build + test
    - azd auth login (OIDC)
    - azd up (provision + deploy)
    # Runs postdeploy hook → register-agent.sh
```

Problems:
1. No separation of CI vs CD
2. No quality gates / evaluation
3. No multi-environment support
4. Provision + deploy coupled together
5. Agent registration via raw REST (`az rest`) — fragile vs SDK approach
6. No promotion pattern (dev → test → prod)

### Proposed Architecture (from research)

```
┌─────────────────────────────────────────────────────────┐
│ CI (PR)                                                  │
│  ✓ Build .NET solution                                   │
│  ✓ Run unit + integration tests                          │
│  ✓ Bicep lint (az bicep build — offline, no auth)        │
│  NO Azure auth needed                                    │
└─────────────────────────────────────────────────────────┘
         │ merge to main
         ▼
┌─────────────────────────────────────────────────────────┐
│ CD — Deploy Dev (auto)                                   │
│  1. OIDC login                                           │
│  2. Set azd env vars (from GH secrets/vars)              │
│  3. azd provision (idempotent — no-op when unchanged)    │
│  4. azd deploy (ACR build + push + ACA update, all 3)   │
│  5. postdeploy hook → register-agent.sh (auto)           │
│  6. Smoke test (curl /health on claw-agent ACA)            │
└─────────────────────────────────────────────────────────┘
         │ (approval gate — future)
         ▼
┌─────────────────────────────────────────────────────────┐
│ CD — Deploy Prod (approval required — future)            │
│  Same steps, different AZURE_ENV_NAME + params           │
└─────────────────────────────────────────────────────────┘
```

### Key Design Decisions

| Decision | Recommendation | Rationale |
|----------|---------------|-----------|
| Agent registration | Keep `az rest` (not Python SDK) | We're .NET shop, no Python in container apps; shell script simpler |
| Image build | `az acr build` (cloud-side) | No Docker daemon in GH Actions runner; same as leestott |
| Multi-env | Start with dev only | YAGNI — add test/prod when needed |
| Quality gates | Skip for now | No eval dataset yet; add later |
| Infra provisioning | Keep `azd provision` | Already works; Bicep templates proven |
| Promotion | Not needed yet | Single env for now |

---

## 5. Design Critique

### ✅ Good (keep)
- `azd` handles image naming, ACR push, Container App updates — don't reinvent
- OIDC federation (no stored secrets) — already in place
- `register-agent.sh` graceful region-unsupported handling
- Single repo monorepo — simple CI (one build, one test)

### ⚠️ Concerns
1. **Coupling**: `azd up` = provision + deploy atomic. Split: `azd provision` (infra changes only) + `azd deploy` (every push). `azd deploy` already handles ACR build + push + ACA update internally — don't duplicate with raw `az acr build`.

2. **Agent registration via REST vs SDK**: `az rest` fragile (manual JSON body, headers). Python SDK (`AIProjectClient.agents.create_version()`) handles all this. No .NET equivalent exists. Keep `az rest` for now, monitor for .NET SDK.

3. **Image tagging**: `azd deploy` uses timestamp-based tags (`azd-deploy-{epoch}`). Git SHA = deterministic, reproducible. Consider switching via `azd env set IMAGE_TAG`.

4. **No rollback**: If agent registration fails, Container Apps already updated. Acceptable — postdeploy hook already exits 0 on region-unsupported.

### 🚫 Over-engineering to avoid
- Don't add evaluation pipelines yet (no eval dataset)
- Don't add test/prod environments yet (no need)
- Don't replace `azd` with raw `az` commands (azd abstracts complexity)
- Don't add Python just for agent SDK (keep .NET + shell)

---

## 6. Recommended Implementation Plan

### Phase 1: Split CI from CD

**`.github/workflows/ci.yml`** — PRs only (no Azure auth needed):
```yaml
name: CI
on:
  pull_request:
    branches: [main]
jobs:
  validate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
          dotnet-quality: 'preview'
      - run: dotnet restore foundry-agentfx.slnx
      - run: dotnet build foundry-agentfx.slnx --no-restore -c Release
      - run: dotnet test foundry-agentfx.slnx --no-build -c Release
      - uses: azure/cli@v2
        with:
          inlineScript: az bicep build --file infra/main.bicep  # offline lint, no auth
```

**`.github/workflows/cd.yml`** — Push to main:
```yaml
name: Deploy
on:
  push:
    branches: [main]
  workflow_dispatch:
permissions:
  id-token: write
  contents: read
env:
  AZURE_CLIENT_ID: ${{ vars.AZURE_CLIENT_ID }}
  AZURE_TENANT_ID: ${{ vars.AZURE_TENANT_ID }}
  AZURE_SUBSCRIPTION_ID: ${{ vars.AZURE_SUBSCRIPTION_ID }}
  AZURE_ENV_NAME: ${{ vars.AZURE_ENV_NAME || 'dev' }}
  AZURE_LOCATION: ${{ vars.AZURE_LOCATION || 'westus' }}
jobs:
  deploy-dev:
    runs-on: ubuntu-latest
    environment: dev
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
          dotnet-quality: 'preview'
      - name: Build and test
        run: |
          dotnet restore foundry-agentfx.slnx
          dotnet build foundry-agentfx.slnx --no-restore -c Release
          dotnet test foundry-agentfx.slnx --no-build -c Release
      - uses: Azure/setup-azd@v2
      - name: OIDC Login
        run: |
          azd auth login \
            --client-id "$AZURE_CLIENT_ID" \
            --federated-credential-provider github \
            --tenant-id "$AZURE_TENANT_ID"
      - name: Set azd environment
        run: |
          azd env new "$AZURE_ENV_NAME" --no-prompt 2>/dev/null || azd env select "$AZURE_ENV_NAME"
          azd env set AZURE_LOCATION "$AZURE_LOCATION"
          azd env set AZURE_SUBSCRIPTION_ID "$AZURE_SUBSCRIPTION_ID"
          azd env set ENABLE_HOSTED_FOUNDRY "${{ vars.ENABLE_HOSTED_FOUNDRY || 'true' }}"
          # Secrets (only set when non-empty)
          [ -n "${{ secrets.SLACK_BOT_TOKEN }}" ] && azd env set SLACK_BOT_TOKEN "${{ secrets.SLACK_BOT_TOKEN }}"
          [ -n "${{ secrets.SLACK_APP_TOKEN }}" ] && azd env set SLACK_APP_TOKEN "${{ secrets.SLACK_APP_TOKEN }}"
          [ -n "${{ secrets.SLACK_SIGNING_SECRET }}" ] && azd env set SLACK_SIGNING_SECRET "${{ secrets.SLACK_SIGNING_SECRET }}"
          [ -n "${{ secrets.BRAVE_SEARCH_API_KEY }}" ] && azd env set BRAVE_SEARCH_API_KEY "${{ secrets.BRAVE_SEARCH_API_KEY }}"
      - name: Provision infrastructure
        run: azd provision --no-prompt
      - name: Deploy services
        run: azd deploy --no-prompt
        # postdeploy hook auto-runs register-agent.sh
      - name: Smoke test
        run: |
          eval "$(azd env get-values | sed 's/^/export /')"
          CLAW_FQDN=$(az containerapp show -n claw-agent -g "rg-${AZURE_ENV_NAME}" \
            --query 'properties.configuration.ingress.fqdn' -o tsv)
          curl -sf "https://${CLAW_FQDN}/health" --max-time 30 || exit 1
          echo "✓ claw-agent healthy"
```

### Phase 2 (future): Multi-env + promotion

### Verification Criteria

| Step | Action | Verify |
|------|--------|--------|
| 1 | Create `.github/workflows/ci.yml` | PR triggers build+test+lint, passes green, no Azure auth needed |
| 2 | Create `.github/workflows/cd.yml` | Push to main → provision + deploy + smoke test passes |
| 3 | Delete `.github/workflows/azure-deploy.yml` | No duplicate workflow runs on push to main |
| 4 | Smoke test step | `curl /health` returns 200 after deploy |
| 5 | postdeploy hook | Agent registered (or gracefully skipped if region unsupported) |
| 6 | Secrets handling | Slack tokens passed to azd env, Container App gets them |

### Phase 3 (future): Multi-env + promotion

Only when project needs it:
- Add `environment: production` job with approval gate
- Separate parameter files per env
- Agent version promotion

---

## 7. Secrets & Variables Needed (GitHub)

### Repository Variables (non-secret)
| Name | Value |
|------|-------|
| `AZURE_CLIENT_ID` | Service principal app ID (OIDC federation) |
| `AZURE_TENANT_ID` | Entra ID tenant |
| `AZURE_SUBSCRIPTION_ID` | Target subscription |
| `AZURE_ENV_NAME` | `dev` (or `prod`) |
| `AZURE_LOCATION` | `westus` |
| `ENABLE_HOSTED_FOUNDRY` | `true` or `false` — controls Foundry Hosted Agent registration |

### Repository Secrets
| Name | Value |
|------|-------|
| `SLACK_BOT_TOKEN` | `xoxb-...` |
| `SLACK_APP_TOKEN` | `xapp-...` |
| `SLACK_SIGNING_SECRET` | Slack signing secret |
| `BRAVE_SEARCH_API_KEY` | Brave API key (optional) |

### OIDC Federation Setup (required)
```bash
# Create app registration + federated credential for GitHub Actions
az ad app create --display-name "foundry-agentfx-cicd"
az ad sp create --id <app-id>
# Add federated credential for repo:main branch (CD only — CI needs no auth)
az ad app federated-credential create --id <app-id> --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:thangchung/agent-engineering-experiment:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'
# Assign roles — subscription-level needed because azd creates RG via sub-scope deployment
az role assignment create --assignee <sp-id> --role "Contributor" --scope /subscriptions/<sub-id>
az role assignment create --assignee <sp-id> --role "Cognitive Services User" --scope /subscriptions/<sub-id>
```

---

## 8. Key Findings Summary

1. **SDK > REST for agent CRUD** — both reference repos use `azure-ai-projects` Python SDK. Our `az rest` approach works but is fragile. Monitor for .NET SDK support.

2. **OIDC everywhere** — no stored client secrets. `azure/login@v2` with federated credentials.

3. **`az acr build` for container images** — cloud-side build, no Docker daemon needed. `azd` already does this internally.

4. **`Cognitive Services User` role** — required for agent CRUD. `Azure AI Developer` insufficient.

5. **Separation of concerns** — CI (offline validation) vs CD (requires Azure). Our current workflow combines both.

6. **Quality gates optional but valuable** — evaluation datasets + threshold checks before promotion. Not needed for MVP.

7. **Bicep patterns well-established** — our infra already follows `00-basic` + connections pattern from foundry-samples. Could upgrade to `2025-06-01` API version (latest stable).

8. **Multi-env via GitHub Environments** — built-in approval gates, environment-scoped secrets/vars. Low effort to add later.
