# Entra ID Setup — Web → ExecutorAgent → CheckerAgent / McpServer (OBO chain)

Not implemented. Scripts only — run yourself.

## Topology

```
Web (App Reg) --OBO--> ExecutorAgent (Agent Identity Blueprint)
ExecutorAgent --OBO--> CheckerAgent (Agent Identity Blueprint)
ExecutorAgent --OBO--> McpServer (App Reg)
CheckerAgent    --OBO--> McpServer (App Reg)
```

- Web + McpServer = normal App Registrations → classic OBO (exposed API + `access_as_user` scope + delegated grant).
- ExecutorAgent + CheckerAgent = Agent Identity (Blueprint + BlueprintPrincipal + AgentIdentity) → OBO via Blueprint exposed as API + `oauth2PermissionGrants` per Agent Identity (not classic consent).
- Build downstream-first: McpServer → CheckerAgent → ExecutorAgent → Web.
- Admin consent required after: `az ad app permission admin-consent --id <webAppId>`.
- Runtime: Web→ExecutorAgent = classic OBO grant (`urn:ietf:params:oauth:grant-type:jwt-bearer`). ExecutorAgent→CheckerAgent/McpServer and CheckerAgent→McpServer = two-step `fmi_path` exchange (NOT RFC 8693), using Blueprint's credential (secret or FIC).

## Prerequisites

| Requirement | Notes |
|---|---|
| Entra role | **Agent Identity Developer**, **Agent Identity Administrator**, or **Application Administrator** |
| Tenant | Agent Identity (Blueprint) feature must be enabled for the tenant |
| Access | Interactive sign-in (browser) — `az cli` tokens are rejected by Agent Identity APIs (403) |

Pick one language track below — **PowerShell** or **Python** — and follow it end-to-end (setup, then cleanup if needed). Don't mix tracks; each creates the same four apps independently.

---

## PowerShell

### 1. Setup

1. **Install prerequisites** (once):
   ```powershell
   Install-Module Microsoft.Graph.Applications -Scope CurrentUser -Force
   ```
2. **Save the script** below as `setup-entra-obo-chain.ps1`.
3. **Run it**:
   ```powershell
   ./setup-entra-obo-chain.ps1
   ```
   A browser window opens for `Connect-MgGraph` — sign in with an account holding the role above.
4. **Watch the console output** — it prints `appId`/`spId`/`scopeId` for McpServer, CheckerAgent, ExecutorAgent, Web as each is created. Save these (script keeps them in variables for you, but note them for later use in app config/appsettings).
5. **Grant admin consent** (one-time, after script finishes):
   ```powershell
   az ad app permission admin-consent --id <webAppId>
   ```
6. **Verify** in Entra portal → App registrations → search each app/blueprint name → check "Expose an API" tab shows `access_as_user` + pre-authorized apps as wired.
7. *(Optional, production)* Uncomment/run the FIC section at the bottom of the script — replace `<your-oidc-issuer>` and `<ns>:<sa>` placeholders with your actual workload identity federation subject before running.

```powershell
# ============================================================
# setup-entra-obo-chain.ps1
# Install-Module Microsoft.Graph.Applications -Scope CurrentUser -Force
#
# Order: McpServer -> CheckerAgent -> ExecutorAgent -> Web -> wire grants
# ============================================================

Connect-MgGraph -Scopes @(
    "Application.ReadWrite.All",
    "AppRoleAssignment.ReadWrite.All",
    "DelegatedPermissionGrant.ReadWrite.All",
    "AgentIdentityBlueprint.Create",
    "AgentIdentityBlueprint.ReadWrite.All",
    "AgentIdentityBlueprintPrincipal.Create",
    "AgentIdentity.Create.All",
    "AgentIdentity.ReadWrite.All",
    "User.Read"
)
$graph = "https://graph.microsoft.com/v1.0"
$me = (Get-MgContext).Account
$userId = (Invoke-MgGraphRequest -Method GET -Uri "$graph/users/$me").id
$expiry = (Get-Date).AddYears(10).ToString("yyyy-MM-ddTHH:mm:ssZ")

function Expose-Api {
    param([string]$ObjId, [string]$AppId, [string]$Label)
    $scopeId = [guid]::NewGuid().ToString()
    Invoke-MgGraphRequest -Method PATCH -Uri "$graph/applications/$ObjId" -Body (@{
        identifierUris = @("api://$AppId")
        api = @{
            requestedAccessTokenVersion = 2
            oauth2PermissionScopes = @(@{
                id = $scopeId
                value = "access_as_user"
                type = "User"
                isEnabled = $true
                adminConsentDescription = "Allow calling app to access $Label on behalf of user"
                adminConsentDisplayName = "Access $Label"
                userConsentDescription = "Allow calling app to access $Label on your behalf"
                userConsentDisplayName = "Access $Label"
            })
        }
        optionalClaims = @{
            accessToken = @(@{ name = "idtyp"; essential = $false; additionalProperties = @("include_user_token") })
        }
    } | ConvertTo-Json -Depth 10)
    return $scopeId
}

function Set-PreAuthorized {
    param([string]$ObjId, [array]$CallerAppIdsAndScopeIds)  # array of @{ appId=..; scopeIds=@(..) }
    # NOTE: built as a raw JSON string, not via ConvertTo-Json — ConvertTo-Json silently
    # collapses/drops arrays nested inside array elements (array-of-array-of-object),
    # even with -Depth set high enough. This bites exactly this shape: an array of
    # caller objects where each object itself has an array property (permissionIds).
    $items = foreach ($c in $CallerAppIdsAndScopeIds) {
        $permIds = ($c.scopeIds | ForEach-Object { "`"$_`"" }) -join ","
        "{ `"appId`": `"$($c.appId)`", `"permissionIds`": [$permIds] }"
    }
    $body = "{ `"api`": { `"preAuthorizedApplications`": [$($items -join ',')] } }"
    Invoke-MgGraphRequest -Method PATCH -Uri "$graph/applications/$ObjId" -Body $body -ContentType "application/json"
}

function Grant-Delegated {
    param([string]$ClientSpId, [string]$ResourceSpId, [string]$Scope = "access_as_user")
    Invoke-MgGraphRequest -Method POST -Uri "$graph/oauth2PermissionGrants" -Body (@{
        clientId = $ClientSpId
        consentType = "AllPrincipals"
        resourceId = $ResourceSpId
        scope = $Scope
        expiryTime = $expiry
    } | ConvertTo-Json)
}

# ------------------------------------------------------------
# 1. McpServer (App Registration, downstream-most API)
# ------------------------------------------------------------
$mcpApp = Invoke-MgGraphRequest -Method POST -Uri "$graph/applications" -Body (@{ displayName = "McpServer" } | ConvertTo-Json)
$mcpAppId = $mcpApp.appId; $mcpObjId = $mcpApp.id
$mcpSpId = (Invoke-MgGraphRequest -Method POST -Uri "$graph/servicePrincipals" -Body (@{ appId = $mcpAppId } | ConvertTo-Json)).id
$mcpScopeId = Expose-Api -ObjId $mcpObjId -AppId $mcpAppId -Label "McpServer"
Invoke-MgGraphRequest -Method POST -Uri "$graph/applications/$mcpObjId/addPassword" -Body (@{ passwordCredential = @{ displayName = "mcp-secret" } } | ConvertTo-Json -Depth 5) | Out-Null
Write-Host "McpServer appId=$mcpAppId scopeId=$mcpScopeId"

# ------------------------------------------------------------
# 2. CheckerAgent (Agent Identity Blueprint)
# ------------------------------------------------------------
$checkBlueprint = Invoke-MgGraphRequest -Method POST -Uri "$graph/applications/microsoft.graph.agentIdentityBlueprint" -Body (@{
    displayName = "CheckerAgentBlueprint"
    "sponsors@odata.bind" = @("$graph/users/$userId")
} | ConvertTo-Json -Depth 5)
$checkAppId = $checkBlueprint.appId; $checkObjId = $checkBlueprint.id
Invoke-MgGraphRequest -Method POST -Uri "$graph/servicePrincipals/microsoft.graph.agentIdentityBlueprintPrincipal" -Body (@{ appId = $checkAppId } | ConvertTo-Json) | Out-Null
$checkAgentSpId = (Invoke-MgGraphRequest -Method POST -Uri "$graph/servicePrincipals/microsoft.graph.agentIdentity" -Body (@{
    displayName = "CheckerAgent-instance-1"
    agentIdentityBlueprintId = $checkAppId
    "sponsors@odata.bind" = @("$graph/users/$userId")
} | ConvertTo-Json -Depth 5)).id
$checkScopeId = Expose-Api -ObjId $checkObjId -AppId $checkAppId -Label "CheckerAgent"
Write-Host "CheckerAgent Blueprint appId=$checkAppId agentIdentitySpId=$checkAgentSpId scopeId=$checkScopeId"

# ------------------------------------------------------------
# 3. ExecutorAgent (Agent Identity Blueprint)
# ------------------------------------------------------------
$execBlueprint = Invoke-MgGraphRequest -Method POST -Uri "$graph/applications/microsoft.graph.agentIdentityBlueprint" -Body (@{
    displayName = "ExecutorAgentBlueprint"
    "sponsors@odata.bind" = @("$graph/users/$userId")
} | ConvertTo-Json -Depth 5)
$execAppId = $execBlueprint.appId; $execObjId = $execBlueprint.id
Invoke-MgGraphRequest -Method POST -Uri "$graph/servicePrincipals/microsoft.graph.agentIdentityBlueprintPrincipal" -Body (@{ appId = $execAppId } | ConvertTo-Json) | Out-Null
$execAgentSpId = (Invoke-MgGraphRequest -Method POST -Uri "$graph/servicePrincipals/microsoft.graph.agentIdentity" -Body (@{
    displayName = "ExecutorAgent-instance-1"
    agentIdentityBlueprintId = $execAppId
    "sponsors@odata.bind" = @("$graph/users/$userId")
} | ConvertTo-Json -Depth 5)).id
$execScopeId = Expose-Api -ObjId $execObjId -AppId $execAppId -Label "ExecutorAgent"
Write-Host "ExecutorAgent Blueprint appId=$execAppId agentIdentitySpId=$execAgentSpId scopeId=$execScopeId"

# ------------------------------------------------------------
# 4. Web (App Registration, front door)
# ------------------------------------------------------------
$webApp = Invoke-MgGraphRequest -Method POST -Uri "$graph/applications" -Body (@{
    displayName = "Web"
    web = @{ redirectUris = @("https://localhost:5001/signin-oidc") }
} | ConvertTo-Json -Depth 5)
$webAppId = $webApp.appId; $webObjId = $webApp.id
$webSpId = (Invoke-MgGraphRequest -Method POST -Uri "$graph/servicePrincipals" -Body (@{ appId = $webAppId } | ConvertTo-Json)).id
Invoke-MgGraphRequest -Method POST -Uri "$graph/applications/$webObjId/addPassword" -Body (@{ passwordCredential = @{ displayName = "web-secret" } } | ConvertTo-Json -Depth 5) | Out-Null
# NOTE: built as a raw JSON string, not via ConvertTo-Json — ConvertTo-Json silently
# collapses/drops the nested "resourceAccess" array (array inside an array element),
# even with -Depth set high enough.
$requiredResourceAccessJson = "{ `"requiredResourceAccess`": [ { `"resourceAppId`": `"$execAppId`", `"resourceAccess`": [ { `"id`": `"$execScopeId`", `"type`": `"Scope`" } ] } ] }"
Invoke-MgGraphRequest -Method PATCH -Uri "$graph/applications/$webObjId" -Body $requiredResourceAccessJson -ContentType "application/json"
Write-Host "Web appId=$webAppId spId=$webSpId"

# ------------------------------------------------------------
# 5. Wire pre-authorization + delegated grants across the chain
# ------------------------------------------------------------
# Web -> ExecutorAgent
Set-PreAuthorized -ObjId $execObjId -CallerAppIdsAndScopeIds @(@{ appId = $webAppId; scopeIds = @($execScopeId) })
Grant-Delegated -ClientSpId $webSpId -ResourceSpId $execAgentSpId

# ExecutorAgent -> CheckerAgent
Set-PreAuthorized -ObjId $checkObjId -CallerAppIdsAndScopeIds @(@{ appId = $execAppId; scopeIds = @($checkScopeId) })
Grant-Delegated -ClientSpId $execAgentSpId -ResourceSpId $checkAgentSpId

# ExecutorAgent -> McpServer, CheckerAgent -> McpServer (combined preauthorized list)
Set-PreAuthorized -ObjId $mcpObjId -CallerAppIdsAndScopeIds @(
    @{ appId = $execAppId; scopeIds = @($mcpScopeId) },
    @{ appId = $checkAppId; scopeIds = @($mcpScopeId) }
)
Grant-Delegated -ClientSpId $execAgentSpId -ResourceSpId $mcpSpId
Grant-Delegated -ClientSpId $checkAgentSpId -ResourceSpId $mcpSpId

Write-Host "OBO chain wired: Web -> ExecutorAgent -> {CheckerAgent, McpServer}; CheckerAgent -> McpServer"

# ------------------------------------------------------------
# 6. (Prod) Federated Identity Credentials on each Blueprint instead of secrets
# ------------------------------------------------------------
foreach ($pair in @(@{ objId = $execObjId; name = "executor-fic" }, @{ objId = $checkObjId; name = "checker-fic" })) {
    Invoke-MgGraphRequest -Method POST -Uri "$graph/applications/$($pair.objId)/microsoft.graph.agentIdentityBlueprint/federatedIdentityCredentials" -Body (@{
        name = $pair.name
        issuer = "https://<your-oidc-issuer>"
        subject = "system:serviceaccount:<ns>:<sa>"
        audiences = @("api://AzureADTokenExchange")
    } | ConvertTo-Json) | Out-Null
}
```

**Notes**
- Admin consent: `az ad app permission admin-consent --id $webAppId`.
- Web→ExecutorAgent runtime OBO = classic OBO grant (`urn:ietf:params:oauth:grant-type:jwt-bearer`).
- ExecutorAgent→CheckerAgent/McpServer and CheckerAgent→McpServer runtime OBO = two-step `fmi_path` exchange (not RFC 8693), using each Blueprint's credential (secret or FIC).

### 2. Cleanup — if setup fails mid-run

The setup script is **not idempotent** — re-running after a partial failure creates duplicate apps. Common causes: `400 Agent Blueprint Principal does not exist` (BlueprintPrincipal step skipped/failed) or `403` on a Graph call (wrong role / stale `Connect-MgGraph` scopes — recheck against the Prerequisites table). In either case, run the cleanup script below, then rerun setup from scratch.

The cleanup script looks objects up **by `displayName`** (not by saved variables), so it works even after a partial/failed run, and is safe to re-run (no-op if nothing found). Deleting an app registration/blueprint also removes its service principal(s), so grants and pre-authorizations disappear with it — no separate grant cleanup needed.

1. **Save the script** below as `cleanup-entra-obo-chain.ps1`.
2. **Run it**:
   ```powershell
   ./cleanup-entra-obo-chain.ps1
   ```
   A browser window opens for `Connect-MgGraph` — sign in with the same account/role used for setup.
3. **Watch the console output** — each found object prints `Deleting SP ...` / `Deleting application ...`; objects not found print `... - skipping` (safe, not an error).
4. **Verify** in Entra portal → App registrations — confirm `Web`, `McpServer`, `ExecutorAgentBlueprint`, `CheckerAgentBlueprint` are gone.
5. **Rerun the setup script** from scratch once cleanup finishes.

```powershell
# ============================================================
# cleanup-entra-obo-chain.ps1
# Deletes Web, McpServer, ExecutorAgentBlueprint, CheckerAgentBlueprint
# (and their Agent Identity / service principal children) by displayName.
# Safe to re-run.
# ============================================================
Connect-MgGraph -Scopes @(
    "Application.ReadWrite.All",
    "AgentIdentityBlueprint.ReadWrite.All",
    "AgentIdentity.ReadWrite.All"
)
$graph = "https://graph.microsoft.com/v1.0"

function Remove-AppByDisplayName {
    param([string]$DisplayName)
    $apps = (Invoke-MgGraphRequest -Method GET -Uri "$graph/applications?`$filter=displayName eq '$DisplayName'").value
    foreach ($app in $apps) {
        $sps = (Invoke-MgGraphRequest -Method GET -Uri "$graph/servicePrincipals?`$filter=appId eq '$($app.appId)'").value
        foreach ($sp in $sps) {
            Write-Host "Deleting SP $($sp.displayName) ($($sp.id))"
            Invoke-MgGraphRequest -Method DELETE -Uri "$graph/servicePrincipals/$($sp.id)"
        }
        Write-Host "Deleting application $($app.displayName) ($($app.id))"
        Invoke-MgGraphRequest -Method DELETE -Uri "$graph/applications/$($app.id)"
    }
    if (-not $apps) { Write-Host "No application found named '$DisplayName' - skipping" }
}

function Remove-AgentIdentityByDisplayName {
    param([string]$DisplayName)
    $sps = (Invoke-MgGraphRequest -Method GET -Uri "$graph/servicePrincipals?`$filter=displayName eq '$DisplayName'").value
    foreach ($sp in $sps) {
        Write-Host "Deleting Agent Identity $($sp.displayName) ($($sp.id))"
        Invoke-MgGraphRequest -Method DELETE -Uri "$graph/servicePrincipals/$($sp.id)"
    }
    if (-not $sps) { Write-Host "No Agent Identity found named '$DisplayName' - skipping" }
}

# Delete Agent Identity instances first, then their Blueprints (Blueprint delete also removes BlueprintPrincipal SP)
Remove-AgentIdentityByDisplayName -DisplayName "ExecutorAgent-instance-1"
Remove-AgentIdentityByDisplayName -DisplayName "CheckerAgent-instance-1"
Remove-AppByDisplayName -DisplayName "ExecutorAgentBlueprint"
Remove-AppByDisplayName -DisplayName "CheckerAgentBlueprint"

# Delete plain App Registrations
Remove-AppByDisplayName -DisplayName "Web"
Remove-AppByDisplayName -DisplayName "McpServer"

Write-Host "Cleanup complete."
```

---

## Python

### 1. Setup

1. **Install prerequisites** (once):
   ```bash
   pip install azure-identity requests
   ```
2. **Set your tenant ID**:
   ```bash
   export AZURE_TENANT_ID="<your-tenant-id>"
   ```
3. **Save the script** below as `setup_entra_obo_chain.py`.
4. **Run it**:
   ```bash
   python setup_entra_obo_chain.py
   ```
   `InteractiveBrowserCredential` opens a browser — sign in with an account holding the role above.
5. **Watch stdout** — prints `appId`/`spId`/`scopeId` for McpServer, CheckerAgent, ExecutorAgent, Web in order.
6. **Grant admin consent** (one-time, after script finishes):
   ```bash
   az ad app permission admin-consent --id <webAppId>
   ```
7. **Verify** in Entra portal → App registrations → search each app/blueprint name → check "Expose an API" tab shows `access_as_user` + pre-authorized apps as wired.
8. *(Optional, production)* Edit the FIC block near the bottom — replace `<your-oidc-issuer>` and `<ns>:<sa>` placeholders — before running that section against real workload identity.

```python
# ============================================================
# setup_entra_obo_chain.py
# pip install azure-identity requests
#
# Order: McpServer -> CheckerAgent -> ExecutorAgent -> Web -> wire grants
# ============================================================
import os
import uuid
import requests
from datetime import datetime, timedelta, timezone
from azure.identity import InteractiveBrowserCredential

GRAPH = "https://graph.microsoft.com/v1.0"
TENANT_ID = os.environ["AZURE_TENANT_ID"]

# NOTE: DefaultAzureCredential / az cli tokens are REJECTED by Agent Identity APIs
# (Directory.AccessAsUser.All -> 403). Use a delegated interactive login instead,
# or a dedicated app registration with client_credentials for pure app-reg calls.
credential = InteractiveBrowserCredential(tenant_id=TENANT_ID)
token = credential.get_token("https://graph.microsoft.com/.default")
headers = {
    "Authorization": f"Bearer {token.token}",
    "Content-Type": "application/json",
    "OData-Version": "4.0",
}

def gget(path, **kw):
    r = requests.get(f"{GRAPH}{path}", headers=headers, **kw); r.raise_for_status(); return r.json()

def gpost(path, body):
    r = requests.post(f"{GRAPH}{path}", headers=headers, json=body); r.raise_for_status(); return r.json()

def gpatch(path, body):
    r = requests.patch(f"{GRAPH}{path}", headers=headers, json=body); r.raise_for_status()

user_id = gget("/me")["id"]
graph_sp_id = gget("/servicePrincipals?$filter=appId eq '00000003-0000-0000-c000-000000000000'")["value"][0]["id"]
expiry = (datetime.now(timezone.utc) + timedelta(days=3650)).strftime("%Y-%m-%dT%H:%M:%SZ")


def expose_api(obj_id, app_id, display_label):
    """Expose an app/blueprint as an OAuth2 API with access_as_user scope. Returns scope id."""
    scope_id = str(uuid.uuid4())
    gpatch(f"/applications/{obj_id}", {
        "identifierUris": [f"api://{app_id}"],
        "api": {
            "requestedAccessTokenVersion": 2,
            "oauth2PermissionScopes": [{
                "id": scope_id,
                "value": "access_as_user",
                "type": "User",
                "isEnabled": True,
                "adminConsentDescription": f"Allow calling app to access {display_label} on behalf of user",
                "adminConsentDisplayName": f"Access {display_label}",
                "userConsentDescription": f"Allow calling app to access {display_label} on your behalf",
                "userConsentDisplayName": f"Access {display_label}",
            }],
        },
        "optionalClaims": {
            "accessToken": [{"name": "idtyp", "essential": False, "additionalProperties": ["include_user_token"]}]
        },
    })
    return scope_id


def preauthorize(obj_id, caller_app_ids_and_scopes):
    """caller_app_ids_and_scopes: list of (appId, [scopeId, ...])"""
    gpatch(f"/applications/{obj_id}", {
        "api": {
            "preAuthorizedApplications": [
                {"appId": app_id, "permissionIds": scope_ids} for app_id, scope_ids in caller_app_ids_and_scopes
            ]
        }
    })


def grant_delegated(client_sp_id, resource_sp_id, scope="access_as_user"):
    gpost("/oauth2PermissionGrants", {
        "clientId": client_sp_id,
        "consentType": "AllPrincipals",
        "resourceId": resource_sp_id,
        "scope": scope,
        "expiryTime": expiry,
    })


# ------------------------------------------------------------
# 1. McpServer (App Registration, downstream-most API)
# ------------------------------------------------------------
mcp_app = gpost("/applications", {"displayName": "McpServer"})
mcp_app_id, mcp_obj_id = mcp_app["appId"], mcp_app["id"]
mcp_sp_id = gpost("/servicePrincipals", {"appId": mcp_app_id})["id"]
mcp_scope_id = expose_api(mcp_obj_id, mcp_app_id, "McpServer")
gpost(f"/applications/{mcp_obj_id}/addPassword", {"passwordCredential": {"displayName": "mcp-secret"}})
print(f"McpServer appId={mcp_app_id} scopeId={mcp_scope_id}")

# ------------------------------------------------------------
# 2. CheckerAgent (Agent Identity Blueprint)
# ------------------------------------------------------------
check_blueprint = gpost("/applications/microsoft.graph.agentIdentityBlueprint", {
    "displayName": "CheckerAgentBlueprint",
    "sponsors@odata.bind": [f"{GRAPH}/users/{user_id}"],
})
check_app_id, check_obj_id = check_blueprint["appId"], check_blueprint["id"]
gpost("/servicePrincipals/microsoft.graph.agentIdentityBlueprintPrincipal", {"appId": check_app_id})
check_agent_sp_id = gpost("/servicePrincipals/microsoft.graph.agentIdentity", {
    "displayName": "CheckerAgent-instance-1",
    "agentIdentityBlueprintId": check_app_id,
    "sponsors@odata.bind": [f"{GRAPH}/users/{user_id}"],
})["id"]
check_scope_id = expose_api(check_obj_id, check_app_id, "CheckerAgent")
print(f"CheckerAgent Blueprint appId={check_app_id} agentIdentitySpId={check_agent_sp_id} scopeId={check_scope_id}")

# ------------------------------------------------------------
# 3. ExecutorAgent (Agent Identity Blueprint)
# ------------------------------------------------------------
exec_blueprint = gpost("/applications/microsoft.graph.agentIdentityBlueprint", {
    "displayName": "ExecutorAgentBlueprint",
    "sponsors@odata.bind": [f"{GRAPH}/users/{user_id}"],
})
exec_app_id, exec_obj_id = exec_blueprint["appId"], exec_blueprint["id"]
gpost("/servicePrincipals/microsoft.graph.agentIdentityBlueprintPrincipal", {"appId": exec_app_id})
exec_agent_sp_id = gpost("/servicePrincipals/microsoft.graph.agentIdentity", {
    "displayName": "ExecutorAgent-instance-1",
    "agentIdentityBlueprintId": exec_app_id,
    "sponsors@odata.bind": [f"{GRAPH}/users/{user_id}"],
})["id"]
exec_scope_id = expose_api(exec_obj_id, exec_app_id, "ExecutorAgent")
print(f"ExecutorAgent Blueprint appId={exec_app_id} agentIdentitySpId={exec_agent_sp_id} scopeId={exec_scope_id}")

# ------------------------------------------------------------
# 4. Web (App Registration, front door)
# ------------------------------------------------------------
web_app = gpost("/applications", {
    "displayName": "Web",
    "web": {"redirectUris": ["https://localhost:5001/signin-oidc"]},
})
web_app_id, web_obj_id = web_app["appId"], web_app["id"]
web_sp_id = gpost("/servicePrincipals", {"appId": web_app_id})["id"]
gpost(f"/applications/{web_obj_id}/addPassword", {"passwordCredential": {"displayName": "web-secret"}})
gpatch(f"/applications/{web_obj_id}", {
    "requiredResourceAccess": [{
        "resourceAppId": exec_app_id,
        "resourceAccess": [{"id": exec_scope_id, "type": "Scope"}],
    }]
})
print(f"Web appId={web_app_id} spId={web_sp_id}")

# ------------------------------------------------------------
# 5. Wire pre-authorization + delegated grants across the chain
# ------------------------------------------------------------
# Web -> ExecutorAgent
preauthorize(exec_obj_id, [(web_app_id, [exec_scope_id])])
grant_delegated(web_sp_id, exec_agent_sp_id)

# ExecutorAgent -> CheckerAgent
preauthorize(check_obj_id, [(exec_app_id, [check_scope_id])])
grant_delegated(exec_agent_sp_id, check_agent_sp_id)

# ExecutorAgent -> McpServer, CheckerAgent -> McpServer (combined preauthorized list)
preauthorize(mcp_obj_id, [(exec_app_id, [mcp_scope_id]), (check_app_id, [mcp_scope_id])])
grant_delegated(exec_agent_sp_id, mcp_sp_id)
grant_delegated(check_agent_sp_id, mcp_sp_id)

print("OBO chain wired: Web -> ExecutorAgent -> {CheckerAgent, McpServer}; CheckerAgent -> McpServer")

# ------------------------------------------------------------
# 6. (Prod) Federated Identity Credentials on each Blueprint instead of secrets
# ------------------------------------------------------------
for obj_id, name in [(exec_obj_id, "executor-fic"), (check_obj_id, "checker-fic")]:
    gpost(f"/applications/{obj_id}/microsoft.graph.agentIdentityBlueprint/federatedIdentityCredentials", {
        "name": name,
        "issuer": "https://<your-oidc-issuer>",
        "subject": "system:serviceaccount:<ns>:<sa>",
        "audiences": ["api://AzureADTokenExchange"],
    })
```

**Notes**
- Admin consent: `az ad app permission admin-consent --id <webAppId>`.
- Web→ExecutorAgent runtime OBO = classic OBO grant (`urn:ietf:params:oauth:grant-type:jwt-bearer`).
- ExecutorAgent→CheckerAgent/McpServer and CheckerAgent→McpServer runtime OBO = two-step `fmi_path` exchange (not RFC 8693), using each Blueprint's credential (secret or FIC).

### 2. Cleanup — if setup fails mid-run

The setup script is **not idempotent** — re-running after a partial failure creates duplicate apps. Common causes: `400 Agent Blueprint Principal does not exist` (BlueprintPrincipal step skipped/failed) or `403` on a Graph call (wrong role / stale token scopes — recheck against the Prerequisites table). In either case, run the cleanup script below, then rerun setup from scratch.

The cleanup script looks objects up **by `displayName`** (not by saved variables), so it works even after a partial/failed run, and is safe to re-run (no-op if nothing found). Deleting an app registration/blueprint also removes its service principal(s), so grants and pre-authorizations disappear with it — no separate grant cleanup needed.

1. **Ensure prerequisites installed** (same as setup): `pip install azure-identity requests`.
2. **Set your tenant ID** (skip if already exported): `export AZURE_TENANT_ID="<your-tenant-id>"`.
3. **Save the script** below as `cleanup_entra_obo_chain.py`.
4. **Run it**:
   ```bash
   python cleanup_entra_obo_chain.py
   ```
   `InteractiveBrowserCredential` opens a browser — sign in with the same account/role used for setup.
5. **Watch stdout** — each found object prints `Deleting SP ...` / `Deleting application ...`; objects not found print `... - skipping` (safe, not an error).
6. **Verify** in Entra portal → App registrations — confirm `Web`, `McpServer`, `ExecutorAgentBlueprint`, `CheckerAgentBlueprint` are gone.
7. **Rerun the setup script** from scratch once cleanup finishes.

```python
# ============================================================
# cleanup_entra_obo_chain.py
# Deletes Web, McpServer, ExecutorAgentBlueprint, CheckerAgentBlueprint
# (and their Agent Identity / service principal children) by displayName.
# Safe to re-run.
# ============================================================
import os
import requests
from azure.identity import InteractiveBrowserCredential

GRAPH = "https://graph.microsoft.com/v1.0"
TENANT_ID = os.environ["AZURE_TENANT_ID"]

credential = InteractiveBrowserCredential(tenant_id=TENANT_ID)
token = credential.get_token("https://graph.microsoft.com/.default")
headers = {"Authorization": f"Bearer {token.token}", "OData-Version": "4.0"}

def gget(path):
    r = requests.get(f"{GRAPH}{path}", headers=headers); r.raise_for_status(); return r.json()["value"]

def gdelete(path):
    r = requests.delete(f"{GRAPH}{path}", headers=headers); r.raise_for_status()

def remove_app_by_display_name(display_name):
    apps = gget(f"/applications?$filter=displayName eq '{display_name}'")
    if not apps:
        print(f"No application found named '{display_name}' - skipping")
        return
    for app in apps:
        sps = gget(f"/servicePrincipals?$filter=appId eq '{app['appId']}'")
        for sp in sps:
            print(f"Deleting SP {sp['displayName']} ({sp['id']})")
            gdelete(f"/servicePrincipals/{sp['id']}")
        print(f"Deleting application {app['displayName']} ({app['id']})")
        gdelete(f"/applications/{app['id']}")

def remove_agent_identity_by_display_name(display_name):
    sps = gget(f"/servicePrincipals?$filter=displayName eq '{display_name}'")
    if not sps:
        print(f"No Agent Identity found named '{display_name}' - skipping")
        return
    for sp in sps:
        print(f"Deleting Agent Identity {sp['displayName']} ({sp['id']})")
        gdelete(f"/servicePrincipals/{sp['id']}")

# Delete Agent Identity instances first, then their Blueprints (Blueprint delete also removes BlueprintPrincipal SP)
remove_agent_identity_by_display_name("ExecutorAgent-instance-1")
remove_agent_identity_by_display_name("CheckerAgent-instance-1")
remove_app_by_display_name("ExecutorAgentBlueprint")
remove_app_by_display_name("CheckerAgentBlueprint")

# Delete plain App Registrations
remove_app_by_display_name("Web")
remove_app_by_display_name("McpServer")

print("Cleanup complete.")
```

---

Not implemented — scripts only, run yourself.
