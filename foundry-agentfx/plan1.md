# Foundry Hosted Agent — Implementation Plan (v3 — Karpathy-reviewed)

Goal: `ENABLE_HOSTED_FOUNDRY=false` → current behavior unchanged. Flip `true` → `claw-api` registers as Foundry Hosted Agent. Gateway + Mcp stay Container Apps. Foundry Overview dashboard shows metrics.

---

## Work Item Checklist (incremental — verify each before next)

### Phase 1: Package bump + build verification
- [x] 1.1 Bump `Microsoft.Agents.AI` → `1.7.0` (stable — no preview available)
- [x] 1.2 Bump `Microsoft.Agents.AI.Foundry` → `1.7.0-preview.260526.1`
- [x] 1.3 Bump `Microsoft.Agents.AI.Workflows` → `1.7.0` (stable — no preview available)
- [x] 1.4 Add `Azure.AI.AgentServer.Invocations` `1.0.0-beta.4` (real invocations SDK; Foundry.Hosting is for responses protocol)
- [x] 1.5 `dotnet restore && dotnet build` — build succeeded
- [x] 1.6 Run existing tests — 42 passed (16+18+8)

### Phase 2: Code change (Program.cs only)
- [x] 2.1 Add `AddInvocationsServer()` + `AddScoped<InvocationHandler, CoffeeshopInvocationHandler>()` DI (gated by `Agent:HostedMode=foundry`)
- [x] 2.2 Add `MapInvocationsServer()` (also gated — same `isHostedMode` bool)
- [x] 2.3 `dotnet build` — succeeded
- [x] 2.4 `dotnet run` locally — web channel path unaffected (isHostedMode=false, invocations skipped)
- [x] 2.5 Created `CoffeeshopInvocationHandler.cs` — bridges Foundry invocations protocol to `CoffeeshopWorkflow`

### Phase 3: Manual hosted agent registration (validate concept)
- [ ] 3.1 Push image to ACR manually: `az acr build --registry <acr> --image claw-api:latest .`
- [ ] 3.2 Register agent via REST (one curl command) — confirm `active` status
- [ ] 3.3 Invoke agent endpoint — confirm response
- [ ] 3.4 Check Foundry Portal Overview — metrics appear
> ⚠️ Phase 3 requires live Azure infra (ACR + Foundry project). Do after `azd provision`.

### Phase 4: Bicep automation
- [x] 4.1 Add `enableHostedFoundry` param to `main.bicep` + `main.parameters.json`
- [x] 4.2 Add `enableHostedFoundry` param to `container-apps.bicep`, gate `clawApi` resource
- [x] 4.3 Create `infra/modules/foundry-hosted-agent.bicep` (deploymentScript with jq polling)
- [x] 4.4 Wire module in `main.bicep` with conditional — outputs use computed string (Bicep-safe)
- [x] 4.5 Bicep lint — no errors (warnings only, pre-existing)
- [ ] 4.6 `azd up` with `ENABLE_HOSTED_FOUNDRY=true` — requires live Azure infra

### Phase 5: End-to-end verification
- [ ] 5.1 Invoke via Foundry endpoint → get coffeeshop response
- [ ] 5.2 Foundry Portal → Overview shows metrics/traces
- [ ] 5.3 Rollback: flip flag false, `azd up` → Container App mode restored
- [x] 5.4 Update README.md with hosted agent section

---

## Package context

`Microsoft.Agents.AI.Foundry.Hosting` latest preview: `1.7.0-preview.260526.1`.
Deps: `Azure.AI.AgentServer.Responses >= 1.0.0-beta.4`, `Microsoft.Agents.AI.Foundry >= 1.7.0-preview`, `Microsoft.Agents.AI.Workflows >= 1.7.0`.

Current project: `Microsoft.Agents.AI 1.6.2` + `Microsoft.Agents.AI.Foundry 1.5.0` + `Microsoft.Agents.AI.Workflows 1.6.2`.
**Must bump all three** to `1.7.0-preview.260526.1` when adding Hosting package.

**Version pin strategy**: Pin exact `1.7.0-preview.260526.1`. After GA, switch to stable `1.7.x` wildcard.

---

## Confirmed C# API (Gap #1 FIXED)

From `Microsoft.Agents.AI.Foundry.Hosting` NuGet + MS Learn docs:

```csharp
// DI registration — pass your agent/workflow
builder.Services.AddFoundryInvocations(agent);

// Endpoint mapping — exposes /invocations HTTP endpoint
app.MapFoundryInvocationsEndpoint();
```

Protocol declared in agent metadata: `{ "protocol": "invocations", "version": "1.0.0" }`.
No other options needed. This is the ONLY correct pattern — `MapFoundryHostingAdapter` / `MapFoundryInvocationsChannel` do NOT exist.

---

## Foundry Agent REST API (Gap #3 FIXED)

**Endpoint**: `POST {projectEndpoint}/agents?api-version=2025-11-15-preview`
**NOT a PUT**. Uses multipart/form-data with 2 parts:
- `metadata` (application/json): agent definition
- `code` (application/zip): source code zip

**Required headers**:
```
Authorization: Bearer <token>  (resource: https://ai.azure.com)
Accept: application/json
Foundry-Features: CodeAgents=V1Preview,HostedAgents=V1Preview
x-ms-agent-name: <agent-name>  (CREATE only)
x-ms-code-zip-sha256: <sha256>
```

**Auth token scope**: `https://ai.azure.com` (NOT `https://management.azure.com`)

**Two deploy paths** (pick ONE per agent — mutually exclusive):
- `code_configuration`: upload source zip, Foundry builds. Good for simple agents.
- `container_configuration`: push pre-built image to ACR, Foundry pulls. **We use this** — existing Dockerfile + multi-project solution.

**Agent metadata JSON for container-based .NET invocations** (what we actually use):
```json
{
  "description": "Claw Coffeeshop Agent — MAF concurrent workflow",
  "definition": {
    "kind": "hosted",
    "protocol_versions": [{ "protocol": "invocations", "version": "1.0.0" }],
    "cpu": "1",
    "memory": "2Gi",
    "container_configuration": {
      "image": "<acr>/claw-api:latest",
      "acr_credential": "managed_identity"
    },
    "environment_variables": {
      "ASPNETCORE_HTTP_PORTS": "8080",
      "Agent__HostedMode": "foundry",
      "Foundry__Model": "gpt-4o-mini",
      "Foundry__Endpoint": "<project-endpoint>",
      "Services__ToolSearchGateway__Url": "<gateway-fqdn>",
      "Toolbox__McpEndpoint": "<project-endpoint>/toolboxes/foundry-iq/mcp?api-version=v1",
      "APPLICATIONINSIGHTS_CONNECTION_STRING": "<conn>",
      "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT": "true"
    }
  }
}
```

> ⚠️ `code_configuration` section (below) shown for reference only — NOT used in this plan:
> ```json
> "code_configuration": { "runtime": "dotnet_10", "entry_point": ["dotnet", "Claw.Api.dll"], "dependency_resolution": "bundled" }
> ```

**Invoke**: `POST {projectEndpoint}/agents/{name}/endpoint/protocols/invocations?api-version=v1`

---

## 6 file changes

### 1. `src/Claw.Api/Claw.Api.csproj`

Add package + bump existing:
```xml
<PackageReference Include="Microsoft.Agents.AI"                  Version="1.7.0-preview.260526.1" />
<PackageReference Include="Microsoft.Agents.AI.Foundry"          Version="1.7.0-preview.260526.1" />
<PackageReference Include="Microsoft.Agents.AI.Workflows"        Version="1.7.0-preview.260526.1" />
<PackageReference Include="Microsoft.Agents.AI.Foundry.Hosting"  Version="1.7.0-preview.260526.1" />
```

### 2. `src/Claw.Api/Program.cs` (Gap #4 FIXED — single approach)

**Single approach**: Gate BOTH DI + endpoint together. `MapFoundryInvocationsEndpoint()` requires `AddFoundryInvocations()` services to be registered — calling endpoint without DI = runtime exception.

Before `app.Run()`:
```csharp
var isHostedMode = string.Equals(builder.Configuration["Agent:HostedMode"], "foundry", StringComparison.OrdinalIgnoreCase);

// DI: only register Foundry invocations handler when hosted
if (isHostedMode)
{
    builder.Services.AddFoundryInvocations(coffeeshopWorkflow);
}

var app = builder.Build();

// Endpoints: always map web channel; only map /invocations when DI registered
app.MapWebChannel();
if (isHostedMode)
{
    app.MapFoundryInvocationsEndpoint();
}
```

**Why gate both**: `MapFoundryInvocationsEndpoint()` resolves services from DI. Without `AddFoundryInvocations()` registered → `InvalidOperationException` at first request. Gating both is correct.

**No `apphost.cs` change needed for local dev** — `Agent:HostedMode` only set via Foundry agent env vars (deployed metadata). Aspire never sets it → hosted path skipped → zero side effects.

### 3. `infra/main.bicep`

Add param:
```bicep
@description('Deploy claw-api as Foundry Hosted Agent. false = Container App (default).')
param enableHostedFoundry bool = false
```

Pass to container-apps:
```bicep
module containerApps 'modules/container-apps.bicep' = if (skipContainerApps != 'true') {
  params: {
    ...
    enableHostedFoundry: enableHostedFoundry
  }
}
```

Add new module:
```bicep
module foundryHostedAgent 'modules/foundry-hosted-agent.bicep' = if (skipContainerApps != 'true' && enableHostedFoundry) {
  name: 'foundry-hosted-agent'
  scope: rg
  params: {
    location: location
    tags: tags
    foundryProjectEndpoint: aiProject.outputs.AZURE_AI_PROJECT_ENDPOINT
    containerRegistryLoginServer: containerRegistry.outputs.loginServer
    foundryModel: modelDeploymentName
    appInsightsConnectionString: aiProject.outputs.APPLICATIONINSIGHTS_CONNECTION_STRING
    gatewayUrl: containerApps.outputs.toolsearchGatewayUrl
    scriptIdentityId: managedIdentity.outputs.id        // explicit identity ref
    scriptIdentityPrincipalId: managedIdentity.outputs.principalId
  }
  dependsOn: [containerApps]
}
```

Update outputs (Gap #6 FIXED — use conditional `?` for safe property access):
```bicep
output CLAW_API_URL string = skipContainerApps != 'true'
  ? (enableHostedFoundry
      ? '${aiProject.outputs.AZURE_AI_PROJECT_ENDPOINT}/agents/claw-api/endpoint/protocols/invocations?api-version=v1'
      : containerApps!.outputs.clawApiUrl)
  : ''
```
Note: Don't reference `foundryHostedAgent.outputs` in ternary — Bicep evaluates both branches. Use computed string instead.

### 4. `infra/modules/container-apps.bicep`

```bicep
param enableHostedFoundry bool = false

// coffeeshopMcp — unchanged
// toolsearchGateway — unchanged

resource clawApi 'Microsoft.App/containerApps@2024-03-01' = if (!enableHostedFoundry) {
  // ... entire existing block, unchanged
}

// Safe conditional output (Gap #6 FIXED)
output clawApiUrl string = !enableHostedFoundry ? 'https://${clawApi.properties.latestRevisionFqdn}' : ''
output toolsearchGatewayUrl string = 'https://${toolsearchGateway.properties.latestRevisionFqdn}'
```

### 5. `infra/modules/foundry-hosted-agent.bicep` — NEW FILE (Gap #2, #3 FIXED)

> **Karpathy note**: This Bicep approach is the AUTOMATION step (Phase 4).
> First validate manually in Phase 3 with a single curl command.
> Only proceed to this after manual registration works.

```bicep
param location string
param tags object
param foundryProjectEndpoint string
param containerRegistryLoginServer string
param foundryModel string = 'gpt-4o-mini'
param appInsightsConnectionString string
param gatewayUrl string
param agentName string = 'claw-api'
param scriptIdentityId string          // pre-existing UserAssigned MI resource ID
param scriptIdentityPrincipalId string // for RBAC if needed

resource registerAgent 'Microsoft.Resources/deploymentScripts@2023-08-01' = {
  name: 'register-foundry-hosted-agent'
  location: location
  tags: tags
  kind: 'AzureCLI'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${scriptIdentityId}': {}
    }
  }
  properties: {
    azCliVersion: '2.80.0'
    timeout: 'PT10M'
    retentionInterval: 'PT1H'
    cleanupPreference: 'OnSuccess'
    scriptContent: '''
      set -e
      TOKEN=$(az account get-access-token --resource https://ai.azure.com --query accessToken -o tsv)

      cat > /tmp/metadata.json <<EOF
      {
        "description": "Claw Coffeeshop Agent — MAF concurrent workflow",
        "definition": {
          "kind": "hosted",
          "protocol_versions": [{"protocol": "invocations", "version": "1.0.0"}],
          "cpu": "1",
          "memory": "2Gi",
          "container_configuration": {
            "image": "${ACR}/claw-api:latest",
            "acr_credential": "managed_identity"
          },
          "environment_variables": {
            "ASPNETCORE_HTTP_PORTS": "8080",
            "Agent__HostedMode": "foundry",
            "Foundry__Model": "${MODEL}",
            "Foundry__Endpoint": "${FOUNDRY_ENDPOINT}",
            "Services__ToolSearchGateway__Url": "${GATEWAY}",
            "Toolbox__McpEndpoint": "${FOUNDRY_ENDPOINT}/toolboxes/foundry-iq/mcp?api-version=v1",
            "APPLICATIONINSIGHTS_CONNECTION_STRING": "${AI_CONN}",
            "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT": "true"
          }
        }
      }
      EOF

      # Create agent (POST multipart — metadata only, no zip for container deploy)
      HTTP_CODE=$(curl -s -o /tmp/response.json -w "%{http_code}" -X POST \
        "${FOUNDRY_ENDPOINT}/agents?api-version=2025-11-15-preview" \
        -H "Authorization: Bearer $TOKEN" \
        -H "Accept: application/json" \
        -H "Foundry-Features: CodeAgents=V1Preview,HostedAgents=V1Preview" \
        -H "x-ms-agent-name: ${AGENT_NAME}" \
        -F "metadata=@/tmp/metadata.json;type=application/json")

      if [ "$HTTP_CODE" -ge 400 ]; then
        echo "ERROR: Registration failed (HTTP $HTTP_CODE)"
        cat /tmp/response.json
        exit 1
      fi
      echo "Registered. Polling..."

      # Poll until active (uses jq — available in AzureCLI image)
      for i in $(seq 1 60); do
        STATUS=$(curl -s \
          -H "Authorization: Bearer $TOKEN" \
          -H "Foundry-Features: CodeAgents=V1Preview,HostedAgents=V1Preview" \
          "${FOUNDRY_ENDPOINT}/agents/${AGENT_NAME}/versions/1?api-version=2025-11-15-preview" | \
          jq -r '.status // "unknown"')
        echo "Poll $i: $STATUS"
        [ "$STATUS" = "active" ] && break
        [ "$STATUS" = "failed" ] && { echo "FAILED"; jq . /tmp/response.json; exit 1; }
        sleep 10
      done

      [ "$STATUS" != "active" ] && echo "Timeout" && exit 1
      echo "Agent active!"
    '''
    environmentVariables: [
      { name: 'FOUNDRY_ENDPOINT', value: foundryProjectEndpoint }
      { name: 'AGENT_NAME',       value: agentName }
      { name: 'ACR',              value: containerRegistryLoginServer }
      { name: 'MODEL',            value: foundryModel }
      { name: 'GATEWAY',          value: gatewayUrl }
      { name: 'AI_CONN',          value: appInsightsConnectionString }
    ]
  }
}

output hostedAgentName string = agentName
output hostedAgentUrl string = '${foundryProjectEndpoint}/agents/${agentName}/endpoint/protocols/invocations?api-version=v1'
```

### 6. `infra/main.parameters.json`

```json
"enableHostedFoundry": {
  "value": "${ENABLE_HOSTED_FOUNDRY=false}"
}
```

---

## Identity & RBAC (Gap #2 FIXED)

The deploymentScript identity needs:
1. **Foundry Project Manager** role on the AI project (to create agents + assign agent identity roles)
2. **AcrPull** on the Container Registry (so Foundry can pull claw-api image)

In `main.bicep`, either:
- Reuse existing `managedIdentity` module (if it has Foundry Project Manager)
- Or create dedicated identity in `foundry-hosted-agent.bicep`:

```bicep
resource scriptIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-foundry-hosted-deploy'
  location: location
  tags: tags
}

resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(scriptIdentity.id, aiProjectResourceId, 'FoundryProjectManager')
  scope: aiProjectResource
  properties: {
    principalId: scriptIdentity.properties.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '<foundry-project-manager-role-id>')
    principalType: 'ServicePrincipal'
  }
}
```

Role ID for **Foundry Project Manager**: look up via `az role definition list --name "Foundry Project Manager" --query "[0].id"` (was recently renamed from "Azure AI Project Manager").

---

## Toolbox endpoint for hosted mode (Gap #10 FIXED)

When running as Foundry Hosted Agent, Toolbox MCP endpoint format changes:
- Local/Container App: `https://{search}.search.windows.net/knowledgebases/{kb}/mcp`
- Hosted Agent: `{projectEndpoint}/toolboxes/{name}/mcp?api-version=v1`

**Resolution**: Already included `Toolbox__McpEndpoint` in container_configuration env vars above. The `FoundryBackendRegistrar` reads from config — no code change needed.

---

## Manual validation script (Phase 3 — do this BEFORE Bicep)

```bash
# 1. Build + push image
az acr build --registry <your-acr> --image claw-api:latest --file src/Claw.Api/Dockerfile .

# 2. Get token
TOKEN=$(az account get-access-token --resource https://ai.azure.com --query accessToken -o tsv)
ENDPOINT="https://<account>.services.ai.azure.com/api/projects/<project>"

# 3. Write metadata.json locally (use container_configuration)
cat > /tmp/metadata.json << 'EOF'
{
  "description": "Claw Coffeeshop Agent",
  "definition": {
    "kind": "hosted",
    "protocol_versions": [{"protocol": "invocations", "version": "1.0.0"}],
    "cpu": "1",
    "memory": "2Gi",
    "container_configuration": {
      "image": "<acr>.azurecr.io/claw-api:latest",
      "acr_credential": "managed_identity"
    },
    "environment_variables": {
      "ASPNETCORE_HTTP_PORTS": "8080",
      "Agent__HostedMode": "foundry",
      "Foundry__Model": "gpt-4o-mini"
    }
  }
}
EOF

# 4. Register
curl -X POST "$ENDPOINT/agents?api-version=2025-11-15-preview" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/json" \
  -H "Foundry-Features: CodeAgents=V1Preview,HostedAgents=V1Preview" \
  -H "x-ms-agent-name: claw-api" \
  -F "metadata=@/tmp/metadata.json;type=application/json"

# 5. Poll
curl -s -H "Authorization: Bearer $TOKEN" \
  -H "Foundry-Features: CodeAgents=V1Preview,HostedAgents=V1Preview" \
  "$ENDPOINT/agents/claw-api/versions/1?api-version=2025-11-15-preview" | jq .status

# 6. Invoke
curl -X POST "$ENDPOINT/agents/claw-api/endpoint/protocols/invocations?api-version=v1" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Foundry-Features: CodeAgents=V1Preview,HostedAgents=V1Preview" \
  -d '{"input":"I want a latte"}'
```

> **Karpathy rule**: If step 4-6 fails, STOP. Fix before automating in Bicep.

---

## Karpathy principles applied

| Principle | How applied |
|-----------|------------|
| Start simple | Phase 1-2 = just code changes + build. No infra yet. |
| Verify each step | Each phase has explicit verification gate |
| Don't be a hero | Use `container_configuration` (existing Dockerfile) not source deploy |
| One change at a time | Package bump → code → manual test → automate |
| Become one with data | Phase 3 manual curl = understand API response shape first |

---

## Dockerfile verified (Gap #9 FIXED)

Existing `src/Claw.Api/Dockerfile`:
- ✅ Target: `net10.0`
- ✅ Exposes port 8080
- ✅ Entry: `dotnet Claw.Api.dll`
- ✅ Multi-stage build with publish output
- ⚠️ Note: For container_configuration deploy, Foundry pulls from ACR directly. Dockerfile is fine as-is.

---

## Usage (Gap #11 FIXED — correct ordering)

```bash
# Default — current behavior, no change
azd env set ENABLE_HOSTED_FOUNDRY false
azd up

# Activate Foundry Hosted Agent — MUST use `azd up` (provision + deploy together)
# First time: provision creates infra + registers agent
azd env set ENABLE_HOSTED_FOUNDRY true
azd up       # builds image → pushes ACR → provisions infra → deploymentScript registers agent

# Subsequent code changes only:
azd deploy   # rebuild + push image
azd provision  # re-register agent (content-addressable versioning — new version only if changed)
```

---

## Rollback plan (Gap #12 FIXED)

```bash
# Revert to Container App mode:
azd env set ENABLE_HOSTED_FOUNDRY false
azd up
# Result: claw-api Container App recreated, hosted agent left orphaned (manual delete optional)

# Delete orphaned hosted agent:
TOKEN=$(az account get-access-token --resource https://ai.azure.com --query accessToken -o tsv)
curl -X DELETE "${ENDPOINT}/agents/claw-api?api-version=2025-11-15-preview" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Foundry-Features: CodeAgents=V1Preview,HostedAgents=V1Preview"
```

---

## Verification steps

1. `curl -H "Authorization: Bearer $TOKEN" -H "Foundry-Features: CodeAgents=V1Preview,HostedAgents=V1Preview" "${ENDPOINT}/agents/claw-api/versions/1?api-version=2025-11-15-preview"` → status: `active`
2. Foundry Portal → Overview → Running agents = 1
3. Invoke: `curl -X POST "${ENDPOINT}/agents/claw-api/endpoint/protocols/invocations?api-version=v1" -H "Authorization: Bearer $TOKEN" -H "Foundry-Features: CodeAgents=V1Preview,HostedAgents=V1Preview" -d '{"input":"I want a latte"}'` → order response
4. Foundry Overview → Token usage + Agent run volume populate
5. Aspire local dev (`azd env set ENABLE_HOSTED_FOUNDRY false && dotnet run`) still works

---

## Risk / watch out

- `1.7.0-preview` bump may break existing code — run `dotnet build` immediately after bump, fix API changes
- `deploymentScript` needs network access to `foundryProjectEndpoint` — if private endpoint enabled, add VNet config or use `azd ai agent` CLI instead
- Foundry container_configuration requires ACR to allow Foundry platform managed identity pull (AcrPull role)
- Preview API (`2025-11-15-preview`) may change before GA — pin api-version, monitor release notes
- `Foundry-Features` header required on all mutating calls during preview

---

## Gap resolution summary

| # | Gap | Resolution |
|---|-----|-----------|
| 1 | Unknown C# API | Confirmed: `AddFoundryInvocations()` + `MapFoundryInvocationsEndpoint()` |
| 2 | Missing managed identity | Explicit `scriptIdentityId` param from main.bicep |
| 3 | Unknown REST API path | `POST /agents` multipart, api `2025-11-15-preview`, token `https://ai.azure.com` |
| 4 | Conflicting Program.cs | Gate BOTH DI + endpoint with same condition (DI required for endpoint) |
| 5 | apphost.cs undocumented | Not needed — env var only set in deployed metadata |
| 6 | Bicep conditional output | Use computed string, avoid conditional module output refs |
| 7 | File count mismatch | Fixed: 6 throughout |
| 8 | Version pin strategy | Pin exact preview, wildcard at GA |
| 9 | Dockerfile unverified | Verified: net10.0, port 8080, `dotnet Claw.Api.dll` |
| 10 | Toolbox endpoint mismatch | `Toolbox__McpEndpoint` in container_configuration env vars |
| 11 | Deploy ordering | `azd up` first time; manual Phase 3 validates before automation |
| 12 | No rollback | Rollback commands added |
| K1 | code_config vs container_config mixed | Fixed: use `container_configuration` only (have Dockerfile) |
| K2 | DI + endpoint mismatch | Fixed: gate both with same `isHostedMode` bool |
| K3 | python3 in deploymentScript | Fixed: use `jq` (available in AzureCLI image) |
| K4 | No incremental verification | Fixed: 5-phase checklist, verify each before next |
| K5 | Over-engineered first step | Fixed: Phase 3 manual curl validates before Bicep automation |
