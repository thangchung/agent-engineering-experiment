# ============================================================
# setup-entra-obo-chain.ps1
# Install-Module Microsoft.Graph.Applications -Scope CurrentUser -Force
#
# Order: McpServer -> CheckerAgent -> ExecutorAgent -> Web -> wire grants
# ============================================================
param(
    [string]$FederatedIssuer,
    [string]$FederatedSubject,
    [string]$FederatedAudience = "api://AzureADTokenExchange"
)

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

function Get-ServicePrincipalByAppId {
    param([string]$AppId)

    $encodedFilter = [uri]::EscapeDataString("appId eq '$AppId'")
    $result = Invoke-MgGraphRequest -Method GET -Uri "$graph/servicePrincipals?`$filter=$encodedFilter&`$select=id,appId,displayName"
    if (-not $result.value -or $result.value.Count -ne 1) {
        throw "Expected exactly one service principal for appId '$AppId', found $($result.value.Count)."
    }

    return $result.value[0]
}

function Add-FederatedCredential {
    param(
        [string]$ObjId,
        [string]$Name,
        [string]$Issuer,
        [string]$Subject,
        [string]$Audience
    )

    Invoke-MgGraphRequest -Method POST -Uri "$graph/applications/$ObjId/federatedIdentityCredentials" -Body (@{
        name = $Name
        issuer = $Issuer
        subject = $Subject
        audiences = @($Audience)
    } | ConvertTo-Json -Depth 5) | Out-Null
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
$checkAgentIdentity = Invoke-MgGraphRequest -Method POST -Uri "$graph/servicePrincipals/microsoft.graph.agentIdentity" -Body (@{
    displayName = "CheckerAgent-instance-1"
    agentIdentityBlueprintId = $checkAppId
    "sponsors@odata.bind" = @("$graph/users/$userId")
} | ConvertTo-Json -Depth 5)
$checkAgentAppId = $checkAgentIdentity.appId
$checkAgentSpId = (Get-ServicePrincipalByAppId -AppId $checkAgentAppId).id
$checkScopeId = Expose-Api -ObjId $checkObjId -AppId $checkAppId -Label "CheckerAgent"
Write-Host "CheckerAgent Blueprint appId=$checkAppId agentIdentityAppId=$checkAgentAppId agentIdentitySpId=$checkAgentSpId scopeId=$checkScopeId"

# ------------------------------------------------------------
# 3. ExecutorAgent (Agent Identity Blueprint)
# ------------------------------------------------------------
$execBlueprint = Invoke-MgGraphRequest -Method POST -Uri "$graph/applications/microsoft.graph.agentIdentityBlueprint" -Body (@{
    displayName = "ExecutorAgentBlueprint"
    "sponsors@odata.bind" = @("$graph/users/$userId")
} | ConvertTo-Json -Depth 5)
$execAppId = $execBlueprint.appId; $execObjId = $execBlueprint.id
Invoke-MgGraphRequest -Method POST -Uri "$graph/servicePrincipals/microsoft.graph.agentIdentityBlueprintPrincipal" -Body (@{ appId = $execAppId } | ConvertTo-Json) | Out-Null
$execAgentIdentity = Invoke-MgGraphRequest -Method POST -Uri "$graph/servicePrincipals/microsoft.graph.agentIdentity" -Body (@{
    displayName = "ExecutorAgent-instance-1"
    agentIdentityBlueprintId = $execAppId
    "sponsors@odata.bind" = @("$graph/users/$userId")
} | ConvertTo-Json -Depth 5)
$execAgentAppId = $execAgentIdentity.appId
$execAgentSpId = (Get-ServicePrincipalByAppId -AppId $execAgentAppId).id
$execScopeId = Expose-Api -ObjId $execObjId -AppId $execAppId -Label "ExecutorAgent"
Write-Host "ExecutorAgent Blueprint appId=$execAppId agentIdentityAppId=$execAgentAppId agentIdentitySpId=$execAgentSpId scopeId=$execScopeId"

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
Set-PreAuthorized -ObjId $checkObjId -CallerAppIdsAndScopeIds @(
    @{ appId = $execAppId; scopeIds = @($checkScopeId) },
    @{ appId = $execAgentAppId; scopeIds = @($checkScopeId) }
)
Grant-Delegated -ClientSpId $execAgentSpId -ResourceSpId $checkAgentSpId

# ExecutorAgent -> McpServer, CheckerAgent -> McpServer (combined preauthorized list)
Set-PreAuthorized -ObjId $mcpObjId -CallerAppIdsAndScopeIds @(
    @{ appId = $execAppId; scopeIds = @($mcpScopeId) },
    @{ appId = $checkAppId; scopeIds = @($mcpScopeId) },
    @{ appId = $execAgentAppId; scopeIds = @($mcpScopeId) },
    @{ appId = $checkAgentAppId; scopeIds = @($mcpScopeId) }
)
Grant-Delegated -ClientSpId $execAgentSpId -ResourceSpId $mcpSpId
Grant-Delegated -ClientSpId $checkAgentSpId -ResourceSpId $mcpSpId

if ($FederatedIssuer -and $FederatedSubject) {
    Add-FederatedCredential -ObjId $execObjId -Name "executor-local-fic" -Issuer $FederatedIssuer -Subject $FederatedSubject -Audience $FederatedAudience
    Add-FederatedCredential -ObjId $checkObjId -Name "checker-local-fic" -Issuer $FederatedIssuer -Subject $FederatedSubject -Audience $FederatedAudience
    Write-Host "Federated identity credentials added for ExecutorAgent and CheckerAgent."
}
else {
    Write-Host "Federated identity credentials NOT created. Re-run with -FederatedIssuer and -FederatedSubject matching the signed assertion used by Parameters:federated-token-file."
}

Write-Host "OBO chain wired: Web -> ExecutorAgent -> {CheckerAgent, McpServer}; CheckerAgent -> McpServer"