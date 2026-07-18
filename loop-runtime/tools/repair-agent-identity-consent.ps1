param(
    [string]$WebAppId = "49bdac29-24c3-413e-bf72-5dcfbfe55ed6",
    [string]$ExecutorBlueprintAppId = "ba511c70-c2c8-4374-94d3-26742a4ede58",
    [string]$CheckerBlueprintAppId = "40d39e9a-69f7-4bf2-b50e-c126c07fd2bd",
    [string]$McpAppId = "b228aac6-5596-4cd6-bf8e-8bc343613268",
    [string]$ExecutorAgentIdentityAppId = "6e1590ce-042e-49e8-bfd1-75104fa813a7",
    [string]$CheckerAgentIdentityAppId = "139e29cb-f6e9-4771-ac42-1d89e554c9fe"
)

$scopes = @(
    "Application.Read.All",
    "Application.ReadWrite.All",
    "DelegatedPermissionGrant.ReadWrite.All"
)
Connect-MgGraph -Scopes $scopes

$graph = "https://graph.microsoft.com/v1.0"
$expiry = (Get-Date).AddYears(10).ToString("yyyy-MM-ddTHH:mm:ssZ")

function Get-ApplicationByAppId {
    param([string]$AppId)

    $encodedFilter = [uri]::EscapeDataString("appId eq '$AppId'")
    $result = Invoke-MgGraphRequest -Method GET -Uri "$graph/applications?`$filter=$encodedFilter&`$select=id,appId,displayName,api"
    if (-not $result.value -or $result.value.Count -ne 1) {
        throw "Expected exactly one application for appId '$AppId', found $($result.value.Count)."
    }

    return $result.value[0]
}

function Get-ServicePrincipalByAppId {
    param([string]$AppId)

    $encodedFilter = [uri]::EscapeDataString("appId eq '$AppId'")
    $result = Invoke-MgGraphRequest -Method GET -Uri "$graph/servicePrincipals?`$filter=$encodedFilter&`$select=id,appId,displayName,servicePrincipalType"
    if (-not $result.value -or $result.value.Count -ne 1) {
        throw "Expected exactly one service principal for appId '$AppId', found $($result.value.Count)."
    }

    return $result.value[0]
}

function Get-AccessAsUserScopeId {
    param($Application)

    $scope = @($Application.api.oauth2PermissionScopes | Where-Object { $_.value -eq "access_as_user" })
    if ($scope.Count -ne 1) {
        throw "Expected exactly one access_as_user scope on '$($Application.displayName)', found $($scope.Count)."
    }

    return $scope[0].id
}

function Set-PreAuthorized {
    param([string]$ApplicationObjectId, [array]$CallerAppIdsAndScopeIds)

    $items = foreach ($caller in $CallerAppIdsAndScopeIds) {
        $permissionIds = ($caller.scopeIds | ForEach-Object { "`"$_`"" }) -join ","
        "{ `"appId`": `"$($caller.appId)`", `"permissionIds`": [$permissionIds] }"
    }
    $body = "{ `"api`": { `"preAuthorizedApplications`": [$($items -join ',')] } }"
    Invoke-MgGraphRequest -Method PATCH -Uri "$graph/applications/$ApplicationObjectId" -Body $body -ContentType "application/json" | Out-Null
}

function Grant-DelegatedIfMissing {
    param([string]$ClientServicePrincipalId, [string]$ResourceServicePrincipalId, [string]$Scope = "access_as_user")

    $encodedFilter = [uri]::EscapeDataString("clientId eq '$ClientServicePrincipalId' and resourceId eq '$ResourceServicePrincipalId'")
    $existing = Invoke-MgGraphRequest -Method GET -Uri "$graph/oauth2PermissionGrants?`$filter=$encodedFilter"
    $matching = @($existing.value | Where-Object { $_.scope -split ' ' -contains $Scope })
    if ($matching.Count -gt 0) {
        return
    }

    Invoke-MgGraphRequest -Method POST -Uri "$graph/oauth2PermissionGrants" -Body (@{
        clientId = $ClientServicePrincipalId
        consentType = "AllPrincipals"
        resourceId = $ResourceServicePrincipalId
        scope = $Scope
        expiryTime = $expiry
    } | ConvertTo-Json) | Out-Null
}

$web = Get-ServicePrincipalByAppId -AppId $WebAppId
$executorAgent = Get-ServicePrincipalByAppId -AppId $ExecutorAgentIdentityAppId
$checkerAgent = Get-ServicePrincipalByAppId -AppId $CheckerAgentIdentityAppId
$executorApi = Get-ApplicationByAppId -AppId $ExecutorBlueprintAppId
$checkerApi = Get-ApplicationByAppId -AppId $CheckerBlueprintAppId
$mcpApi = Get-ApplicationByAppId -AppId $McpAppId
$executorApiSp = Get-ServicePrincipalByAppId -AppId $ExecutorBlueprintAppId
$checkerApiSp = Get-ServicePrincipalByAppId -AppId $CheckerBlueprintAppId
$mcpApiSp = Get-ServicePrincipalByAppId -AppId $McpAppId

$executorScopeId = Get-AccessAsUserScopeId -Application $executorApi
$checkerScopeId = Get-AccessAsUserScopeId -Application $checkerApi
$mcpScopeId = Get-AccessAsUserScopeId -Application $mcpApi

Set-PreAuthorized -ApplicationObjectId $executorApi.id -CallerAppIdsAndScopeIds @(
    @{ appId = $WebAppId; scopeIds = @($executorScopeId) }
)
Grant-DelegatedIfMissing -ClientServicePrincipalId $web.id -ResourceServicePrincipalId $executorApiSp.id

Set-PreAuthorized -ApplicationObjectId $checkerApi.id -CallerAppIdsAndScopeIds @(
    @{ appId = $ExecutorBlueprintAppId; scopeIds = @($checkerScopeId) },
    @{ appId = $ExecutorAgentIdentityAppId; scopeIds = @($checkerScopeId) }
)
Grant-DelegatedIfMissing -ClientServicePrincipalId $executorAgent.id -ResourceServicePrincipalId $checkerApiSp.id

Set-PreAuthorized -ApplicationObjectId $mcpApi.id -CallerAppIdsAndScopeIds @(
    @{ appId = $ExecutorBlueprintAppId; scopeIds = @($mcpScopeId) },
    @{ appId = $CheckerBlueprintAppId; scopeIds = @($mcpScopeId) },
    @{ appId = $ExecutorAgentIdentityAppId; scopeIds = @($mcpScopeId) },
    @{ appId = $CheckerAgentIdentityAppId; scopeIds = @($mcpScopeId) }
)
Grant-DelegatedIfMissing -ClientServicePrincipalId $executorAgent.id -ResourceServicePrincipalId $mcpApiSp.id
Grant-DelegatedIfMissing -ClientServicePrincipalId $checkerAgent.id -ResourceServicePrincipalId $mcpApiSp.id

Write-Host "Agent Identity consent repaired."
Write-Host "Executor agent identity caller=$($executorAgent.displayName) appId=$ExecutorAgentIdentityAppId"
Write-Host "Checker agent identity caller=$($checkerAgent.displayName) appId=$CheckerAgentIdentityAppId"
Write-Host "Grants: Web -> Executor API; Executor Agent Identity -> Checker API/MCP; Checker Agent Identity -> MCP."