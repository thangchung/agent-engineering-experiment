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