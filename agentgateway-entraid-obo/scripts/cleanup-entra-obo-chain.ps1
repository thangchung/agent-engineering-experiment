#Requires -Version 7.0
<#
.SYNOPSIS
    Deletes the Entra ID objects created by setup-entra-obo-chain.ps1. Idempotent.

.DESCRIPTION
    THIS SCRIPT DELETES REAL OBJECTS IN YOUR ENTRA TENANT. Review before running.
    Deletes, in order: the TodoAgent Agent Identity, its BlueprintPrincipal, the
    TodoAgent Blueprint application, and the TodoApi / TodoMcpServer app registrations.

.EXAMPLE
    ./scripts/cleanup-entra-obo-chain.ps1 -TenantId 768437b2-e373-41b5-9748-875e1507d85b -WhatIf

.EXAMPLE
    ./scripts/cleanup-entra-obo-chain.ps1 -TenantId 768437b2-e373-41b5-9748-875e1507d85b
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $false)]
    [string]$TenantId = "768437b2-e373-41b5-9748-875e1507d85b"
)

$ErrorActionPreference = "Stop"
$GraphBaseUrl = "https://graph.microsoft.com/v1.0"

function Connect-EntraGraph {
    Write-Host "Connecting to Microsoft Graph (tenant: $TenantId)..." -ForegroundColor Cyan
    Connect-MgGraph -TenantId $TenantId -Scopes @(
        "AgentIdentity.ReadWrite.All",
        "AgentIdentityBlueprint.ReadWrite.All",
        "Application.ReadWrite.All"
    ) -NoWelcome
}

function Remove-ByDisplayName {
    param(
        [Parameter(Mandatory)] [string]$DisplayName,
        [Parameter(Mandatory)] [string]$Endpoint,   # "applications" or "servicePrincipals"
        [string]$AgentFilterProperty                # e.g. agentIdentityBlueprintId, for Agent Identities
    )

    $found = Invoke-MgGraphRequest -Method GET `
        -Uri "$GraphBaseUrl/$Endpoint`?`$filter=displayName eq '$DisplayName'"

    if ($found.value.Count -eq 0) {
        Write-Host "  [skip] '$DisplayName' not found under $Endpoint." -ForegroundColor Yellow
        return
    }

    foreach ($item in $found.value) {
        if ($PSCmdlet.ShouldProcess("$DisplayName ($($item.id))", "Delete from $Endpoint")) {
            Invoke-MgGraphRequest -Method DELETE -Uri "$GraphBaseUrl/$Endpoint/$($item.id)"
            Write-Host "  [deleted] '$DisplayName' ($($item.id))" -ForegroundColor Green
        }
    }
}

Connect-EntraGraph

Write-Host "`n== Removing TodoAgent Agent Identity ==" -ForegroundColor Cyan
Remove-ByDisplayName -DisplayName "todoagent-instance-1" -Endpoint "servicePrincipals"

Write-Host "`n== Removing TodoAgent Blueprint (application; BlueprintPrincipal SP is removed automatically) ==" -ForegroundColor Cyan
Remove-ByDisplayName -DisplayName "TodoAgent-Blueprint" -Endpoint "applications"

Write-Host "`n== Removing TodoApi app registration ==" -ForegroundColor Cyan
Remove-ByDisplayName -DisplayName "TodoApi" -Endpoint "applications"

Write-Host "`n== Removing TodoMcpServer app registration ==" -ForegroundColor Cyan
Remove-ByDisplayName -DisplayName "TodoMcpServer" -Endpoint "applications"

Write-Host "`nDone." -ForegroundColor Green
