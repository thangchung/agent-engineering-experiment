#Requires -Version 7.0
<#
.SYNOPSIS
    Provisions the Entra ID objects for the Agentic Todo dual-OBO chain (prd.md/arch.md).

.DESCRIPTION
    Idempotent. Creates:
      - TodoApi app registration (standard entra-app-registration, exposes access_as_user)
      - TodoMcpServer app registration (standard entra-app-registration, exposes access_as_user)
      - TodoAgent Entra Agent ID chain: Blueprint -> BlueprintPrincipal -> Agent Identity
      - Delegated permission grants (oauth2PermissionGrants) TodoApi->TodoAgent and
        TodoAgent(Agent Identity)->TodoMcpServer, both scoped access_as_user (R-3: never .default
        on our own APIs)
      - Authorizes the Azure CLI client on TodoApi for access_as_user (enables
        `az account get-access-token` per prd.md section 7 R-5 / docs/GET-TOKEN.md)
      - Writes all IDs/secrets to .env (git-ignored). .env.local stays a committed
        placeholder/reference template with dummy values only.

    THIS SCRIPT CREATES REAL OBJECTS IN YOUR ENTRA TENANT. Review before running.
    Requires Agent Identity Developer / Administrator or Application Administrator role,
    and Microsoft Entra Agent ID (preview) enabled in the tenant (prd.md R-1: confirmed yes).

.PARAMETER TenantId
    Target tenant. Defaults to the tenant recorded in prd.md/arch.md.

.PARAMETER OutputPath
    Where to write the generated .env file (git-ignored; real secrets).

.EXAMPLE
    ./scripts/setup-entra-obo-chain.ps1 -TenantId 768437b2-e373-41b5-9748-875e1507d85b -WhatIf

.EXAMPLE
    ./scripts/setup-entra-obo-chain.ps1 -TenantId 768437b2-e373-41b5-9748-875e1507d85b
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $false)]
    [string]$TenantId = "768437b2-e373-41b5-9748-875e1507d85b",

    [Parameter(Mandatory = $false)]
    [string]$OutputPath = (Join-Path $PSScriptRoot ".." ".env")
)

$ErrorActionPreference = "Stop"
$GraphBaseUrl = "https://graph.microsoft.com/v1.0"

# Azure CLI's well-known client ID (public, not a secret) -- authorized on TodoApi so
# `az account get-access-token --scope api://<TodoApi>/access_as_user` works (prd R-5).
$AzureCliClientId = "04b07795-8ddb-461a-bbee-02f9e1bf7b46"

function Connect-EntraGraph {
    Write-Host "Connecting to Microsoft Graph (tenant: $TenantId)..." -ForegroundColor Cyan
    Connect-MgGraph -TenantId $TenantId -Scopes @(
        "AgentIdentityBlueprint.Create",
        "AgentIdentityBlueprint.ReadWrite.All",
        "AgentIdentityBlueprintPrincipal.Create",
        "AgentIdentity.Create.All",
        "AgentIdentity.ReadWrite.All",
        "Application.ReadWrite.All",
        "AppRoleAssignment.ReadWrite.All",
        "DelegatedPermissionGrant.ReadWrite.All",
        "User.Read"
    ) -NoWelcome
}

function Get-SignedInUserId {
    (Invoke-MgGraphRequest -Method GET -Uri "$GraphBaseUrl/me").id
}

function New-ApiAppRegistration {
    <#
    Registers a standard confidential-client API app (TodoApi / TodoMcpServer pattern):
    v2 tokens, identifierUris, an exposed access_as_user delegated scope, and a client secret.
    #>
    param(
        [Parameter(Mandatory)] [string]$DisplayName,
        [Parameter(Mandatory)] [string]$SignedInUserId
    )

    $existing = Invoke-MgGraphRequest -Method GET `
        -Uri "$GraphBaseUrl/applications?`$filter=displayName eq '$DisplayName'"
    if ($existing.value.Count -gt 0) {
        Write-Host "  [skip] '$DisplayName' app registration already exists." -ForegroundColor Yellow
        $app = $existing.value[0]
        # oauth2PermissionGrants.clientId needs the service principal id, not the app object id.
        $existingSp = Invoke-MgGraphRequest -Method GET `
            -Uri "$GraphBaseUrl/servicePrincipals?`$filter=appId eq '$($app.appId)'"
        $spId = if ($existingSp.value.Count -gt 0) { $existingSp.value[0].id } else { $null }
        return [pscustomobject]@{ AppId = $app.appId; ObjectId = $app.id; ServicePrincipalId = $spId; ClientSecret = $null }
    }

    if (-not $PSCmdlet.ShouldProcess($DisplayName, "Create app registration")) {
        return [pscustomobject]@{ AppId = "dry-run"; ObjectId = "dry-run"; ServicePrincipalId = "dry-run"; ClientSecret = $null }
    }

    $scopeId = [guid]::NewGuid().ToString()
    $body = @{
        displayName    = $DisplayName
        signInAudience = "AzureADMyOrg"
        api            = @{
            requestedAccessTokenVersion = 2
            oauth2PermissionScopes      = @(
                @{
                    id                      = $scopeId
                    adminConsentDisplayName = "Access $DisplayName as the signed-in user"
                    adminConsentDescription = "Allows the app to access $DisplayName on behalf of the signed-in user."
                    userConsentDisplayName  = "Access $DisplayName"
                    userConsentDescription  = "Allow the app to access $DisplayName on your behalf."
                    value                   = "access_as_user"
                    type                    = "User"
                    isEnabled               = $true
                }
            )
        }
    } | ConvertTo-Json -Depth 10

    $app = Invoke-MgGraphRequest -Method POST -Uri "$GraphBaseUrl/applications" -Body $body

    # identifierUris must be set after creation (references the app's own appId).
    Invoke-MgGraphRequest -Method PATCH -Uri "$GraphBaseUrl/applications/$($app.id)" -Body (@{
        identifierUris = @("api://$($app.appId)")
    } | ConvertTo-Json)

    # sp.id (not the application object id) is what oauth2PermissionGrants.clientId needs.
    $sp = Invoke-MgGraphRequest -Method POST -Uri "$GraphBaseUrl/servicePrincipals" -Body (@{
        appId = $app.appId
    } | ConvertTo-Json)

    $secretResp = Invoke-MgGraphRequest -Method POST -Uri "$GraphBaseUrl/applications/$($app.id)/addPassword" -Body (@{
        passwordCredential = @{ displayName = "local-dev" }
    } | ConvertTo-Json)

    Write-Host "  [created] '$DisplayName' appId=$($app.appId)" -ForegroundColor Green
    [pscustomobject]@{ AppId = $app.appId; ObjectId = $app.id; ServicePrincipalId = $sp.id; ClientSecret = $secretResp.secretText }
}

function New-TodoAgentIdentityChain {
    <#
    Creates the Entra Agent ID object chain for TodoAgent:
    Blueprint (application, holds credentials) -> BlueprintPrincipal (SP, must be created
    explicitly) -> Agent Identity (SP per instance, cannot hold credentials).
    #>
    param([Parameter(Mandatory)] [string]$SignedInUserId)

    $blueprintName = "TodoAgent-Blueprint"
    $existing = Invoke-MgGraphRequest -Method GET `
        -Uri "$GraphBaseUrl/applications?`$filter=displayName eq '$blueprintName'"

    if ($existing.value.Count -gt 0) {
        Write-Host "  [skip] '$blueprintName' already exists." -ForegroundColor Yellow
        $blueprint = $existing.value[0]
    }
    else {
        if (-not $PSCmdlet.ShouldProcess($blueprintName, "Create Agent Identity Blueprint")) {
            return [pscustomobject]@{ BlueprintAppId = "dry-run"; AgentIdentityId = "dry-run"; ClientSecret = $null }
        }

        $scopeId = [guid]::NewGuid().ToString()
        $blueprintBody = @{
            displayName        = $blueprintName
            "sponsors@odata.bind" = @("$GraphBaseUrl/users/$SignedInUserId")
        } | ConvertTo-Json

        $blueprint = Invoke-MgGraphRequest -Method POST `
            -Uri "$GraphBaseUrl/applications/microsoft.graph.agentIdentityBlueprint" -Body $blueprintBody

        Invoke-MgGraphRequest -Method PATCH -Uri "$GraphBaseUrl/applications/$($blueprint.id)" -Body (@{
            identifierUris = @("api://$($blueprint.appId)")
            api            = @{
                requestedAccessTokenVersion = 2
                oauth2PermissionScopes      = @(
                    @{
                        id                      = $scopeId
                        adminConsentDisplayName = "Access TodoAgent as the signed-in user"
                        adminConsentDescription = "Allows the caller to access TodoAgent on behalf of the signed-in user."
                        userConsentDisplayName  = "Access TodoAgent"
                        userConsentDescription  = "Allow access to TodoAgent on your behalf."
                        value                   = "access_as_user"
                        type                    = "User"
                        isEnabled               = $true
                    }
                )
            }
        } | ConvertTo-Json -Depth 10)

        # BlueprintPrincipal is not auto-created -- this step is mandatory.
        Invoke-MgGraphRequest -Method POST `
            -Uri "$GraphBaseUrl/servicePrincipals/microsoft.graph.agentIdentityBlueprintPrincipal" `
            -Body (@{ appId = $blueprint.appId } | ConvertTo-Json) | Out-Null

        Write-Host "  [created] '$blueprintName' appId=$($blueprint.appId)" -ForegroundColor Green
    }

    # Credentials live on the Blueprint (Agent Identities can't hold their own). A secret's
    # plaintext is only ever returned once, at creation -- guard against piling up orphans on re-run.
    $secretText = $null
    $blueprintDetail = Invoke-MgGraphRequest -Method GET `
        -Uri "$GraphBaseUrl/applications/$($blueprint.id)?`$select=passwordCredentials"
    if ($blueprintDetail.passwordCredentials.Count -gt 0) {
        Write-Host "  [skip] '$blueprintName' already has a client secret (existing value cannot be retrieved -- rotate manually if needed)." -ForegroundColor Yellow
    }
    elseif ($PSCmdlet.ShouldProcess("$blueprintName", "Add client secret (local dev)")) {
        $secretResp = Invoke-MgGraphRequest -Method POST -Uri "$GraphBaseUrl/applications/$($blueprint.id)/addPassword" -Body (@{
            passwordCredential = @{ displayName = "local-dev" }
        } | ConvertTo-Json)
        $secretText = $secretResp.secretText
    }

    $agentName = "todoagent-instance-1"
    $existingAgents = Invoke-MgGraphRequest -Method GET `
        -Uri "$GraphBaseUrl/servicePrincipals/microsoft.graph.agentIdentity?`$filter=displayName eq '$agentName'"

    if ($existingAgents.value.Count -gt 0) {
        Write-Host "  [skip] Agent Identity '$agentName' already exists." -ForegroundColor Yellow
        $agent = $existingAgents.value[0]
    }
    else {
        if (-not $PSCmdlet.ShouldProcess($agentName, "Create Agent Identity")) {
            return [pscustomobject]@{ BlueprintAppId = $blueprint.appId; AgentIdentityId = "dry-run"; ClientSecret = $secretText }
        }

        $agentBody = @{
            displayName               = $agentName
            agentIdentityBlueprintId  = $blueprint.appId
            "sponsors@odata.bind"     = @("$GraphBaseUrl/users/$SignedInUserId")
        } | ConvertTo-Json

        $agent = Invoke-MgGraphRequest -Method POST `
            -Uri "$GraphBaseUrl/servicePrincipals/microsoft.graph.agentIdentity" -Body $agentBody

        Write-Host "  [created] Agent Identity '$agentName' id=$($agent.id)" -ForegroundColor Green
    }

    [pscustomobject]@{
        BlueprintAppId  = $blueprint.appId
        BlueprintObjectId = $blueprint.id
        AgentIdentityId = $agent.id
        ClientSecret    = $secretText
    }
}

function Grant-DelegatedPermissionWithRetry {
    <#
    Programmatic delegated consent via oauth2PermissionGrants -- browser-based admin consent
    does NOT work for Agent Identities (entra-agent-id skill). Retries 403s with backoff to
    absorb the 30-120s permission-propagation delay after consent (RISK-003).
    #>
    param(
        [Parameter(Mandatory)] [string]$ClientId,       # the calling principal's object id
        [Parameter(Mandatory)] [string]$ResourceAppId,  # the resource app's appId (api:// owner)
        [Parameter(Mandatory)] [string]$Scope,          # e.g. "access_as_user" (R-3: never .default)
        [int]$MaxAttempts = 5
    )

    $resourceSp = (Invoke-MgGraphRequest -Method GET `
        -Uri "$GraphBaseUrl/servicePrincipals?`$filter=appId eq '$ResourceAppId'").value[0]

    $existingGrants = Invoke-MgGraphRequest -Method GET `
        -Uri "$GraphBaseUrl/oauth2PermissionGrants?`$filter=clientId eq '$ClientId' and resourceId eq '$($resourceSp.id)'"
    if ($existingGrants.value.Count -gt 0) {
        Write-Host "  [skip] Delegated grant $ClientId -> $ResourceAppId already exists." -ForegroundColor Yellow
        return
    }

    if (-not $PSCmdlet.ShouldProcess("$ClientId -> $ResourceAppId", "Grant delegated permission '$Scope'")) {
        return
    }

    $expiry = (Get-Date).ToUniversalTime().AddYears(10).ToString("yyyy-MM-ddTHH:mm:ssZ")
    $body = @{
        clientId    = $ClientId
        consentType = "AllPrincipals"
        resourceId  = $resourceSp.id
        scope       = $Scope
        expiryTime  = $expiry
    } | ConvertTo-Json

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            Invoke-MgGraphRequest -Method POST -Uri "$GraphBaseUrl/oauth2PermissionGrants" -Body $body | Out-Null
            Write-Host "  [granted] $ClientId -> $ResourceAppId ($Scope)" -ForegroundColor Green
            return
        }
        catch {
            if ($attempt -eq $MaxAttempts) { throw }
            $delay = [Math]::Min(30 * $attempt, 120)
            Write-Host "  [retry $attempt/$MaxAttempts] grant not yet propagated, waiting ${delay}s..." -ForegroundColor DarkYellow
            Start-Sleep -Seconds $delay
        }
    }
}

function Grant-AzureCliAuthorizedClient {
    param([Parameter(Mandatory)] [string]$TodoApiObjectId)

    if (-not $PSCmdlet.ShouldProcess("TodoApi", "Authorize Azure CLI client for access_as_user")) { return }

    $app = Invoke-MgGraphRequest -Method GET -Uri "$GraphBaseUrl/applications/$TodoApiObjectId"
    $scopeId = $app.api.oauth2PermissionScopes | Where-Object { $_.value -eq "access_as_user" } | Select-Object -First 1 -ExpandProperty id
    $preAuthorized = @($app.api.preAuthorizedApplications)
    if ($preAuthorized | Where-Object { $_.appId -eq $AzureCliClientId }) {
        Write-Host "  [skip] Azure CLI already authorized on TodoApi." -ForegroundColor Yellow
        return
    }

    $preAuthorized += @{ appId = $AzureCliClientId; delegatedPermissionIds = @($scopeId) }
    Invoke-MgGraphRequest -Method PATCH -Uri "$GraphBaseUrl/applications/$TodoApiObjectId" -Body (@{
        api = @{ preAuthorizedApplications = $preAuthorized }
    } | ConvertTo-Json -Depth 10)
    Write-Host "  [authorized] Azure CLI client can request api://.../access_as_user (prd R-5)." -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

Connect-EntraGraph
$userId = Get-SignedInUserId

Write-Host "`n== TodoApi app registration ==" -ForegroundColor Cyan
$todoApi = New-ApiAppRegistration -DisplayName "TodoApi" -SignedInUserId $userId

Write-Host "`n== TodoMcpServer app registration ==" -ForegroundColor Cyan
$todoMcpServer = New-ApiAppRegistration -DisplayName "TodoMcpServer" -SignedInUserId $userId

Write-Host "`n== TodoAgent Entra Agent ID chain (Blueprint -> BlueprintPrincipal -> Agent Identity) ==" -ForegroundColor Cyan
$todoAgent = New-TodoAgentIdentityChain -SignedInUserId $userId

Write-Host "`n== Delegated permission grants (access_as_user only -- R-3, never .default) ==" -ForegroundColor Cyan
# Hop-1: TodoApi -> TodoAgent. ClientId MUST be the service principal id, not the
# application object id -- the latter causes a 404 Request_ResourceNotFound.
Grant-DelegatedPermissionWithRetry -ClientId $todoApi.ServicePrincipalId -ResourceAppId $todoAgent.BlueprintAppId -Scope "access_as_user"
# Hop-2: TodoAgent's Agent Identity -> TodoMcpServer, for the .NET Agent-ID OBO.
Grant-DelegatedPermissionWithRetry -ClientId $todoAgent.AgentIdentityId -ResourceAppId $todoMcpServer.AppId -Scope "access_as_user"

Write-Host "`n== Azure CLI token acquisition (prd R-5 / docs/GET-TOKEN.md) ==" -ForegroundColor Cyan
Grant-AzureCliAuthorizedClient -TodoApiObjectId $todoApi.ObjectId

# ---------------------------------------------------------------------------
# Emit .env (git-ignored, real values). .env.local stays the committed placeholder
# template -- do not overwrite it here.
# ---------------------------------------------------------------------------

$envContent = @"
# Generated by scripts/setup-entra-obo-chain.ps1 -- DO NOT COMMIT (git-ignored).
TENANT_ID=$TenantId
TODOAPI_CLIENT_ID=$($todoApi.AppId)
TODOAPI_CLIENT_SECRET=$($todoApi.ClientSecret)
TODOAGENT_BLUEPRINT_CLIENT_ID=$($todoAgent.BlueprintAppId)
TODOAGENT_AGENT_IDENTITY_ID=$($todoAgent.AgentIdentityId)
TODOAGENT_CLIENT_SECRET=$($todoAgent.ClientSecret)
TODOMCPSERVER_CLIENT_ID=$($todoMcpServer.AppId)
"@

if ($PSCmdlet.ShouldProcess($OutputPath, "Write generated Entra values")) {
    Set-Content -Path $OutputPath -Value $envContent -NoNewline
    Write-Host "`nWrote $OutputPath" -ForegroundColor Green
    Write-Host "apphost.cs reads this automatically (layered over .env.local) -- no manual" -ForegroundColor Cyan
    Write-Host "user-secrets/environment step needed. Add AZURE_OPENAI_* values to the same" -ForegroundColor Cyan
    Write-Host "file yourself (this script only provisions Entra, not Foundry) before" -ForegroundColor Cyan
    Write-Host "re-running 'aspire run' for the live end-to-end chain." -ForegroundColor Cyan
}

Write-Host "`nDone." -ForegroundColor Green
