param(
    [Parameter(Mandatory = $true)]
    [string]$FederatedIssuer,

    [string]$FederatedSubject = "loop-runtime-local",
    [string]$FederatedAudience = "api://AzureADTokenExchange",
    [string]$ExecutorAppId = "ba511c70-c2c8-4374-94d3-26742a4ede58",
    [string]$CheckerAppId = "40d39e9a-69f7-4bf2-b50e-c126c07fd2bd"
)

Connect-MgGraph -Scopes @("Application.ReadWrite.All")
$graph = "https://graph.microsoft.com/v1.0"

function Get-AppObjectId {
    param([string]$AppId)

    $encodedFilter = [uri]::EscapeDataString("appId eq '$AppId'")
    $result = Invoke-MgGraphRequest -Method GET -Uri "$graph/applications?`$filter=$encodedFilter&`$select=id,appId,displayName"
    if (-not $result.value -or $result.value.Count -ne 1) {
        throw "Expected exactly one application for appId '$AppId', found $($result.value.Count)."
    }

    return $result.value[0].id
}

function Set-FederatedCredential {
    param(
        [string]$ObjectId,
        [string]$Name,
        [string]$Issuer,
        [string]$Subject,
        [string]$Audience
    )

    $existing = Invoke-MgGraphRequest -Method GET -Uri "$graph/applications/$ObjectId/federatedIdentityCredentials"
    foreach ($credential in @($existing.value | Where-Object { $_.name -eq $Name })) {
        Invoke-MgGraphRequest -Method DELETE -Uri "$graph/applications/$ObjectId/federatedIdentityCredentials/$($credential.id)"
    }

    Invoke-MgGraphRequest -Method POST -Uri "$graph/applications/$ObjectId/federatedIdentityCredentials" -Body (@{
        name = $Name
        issuer = $Issuer
        subject = $Subject
        audiences = @($Audience)
    } | ConvertTo-Json -Depth 5) | Out-Null
}

$executorObjectId = Get-AppObjectId -AppId $ExecutorAppId
$checkerObjectId = Get-AppObjectId -AppId $CheckerAppId

Set-FederatedCredential -ObjectId $executorObjectId -Name "executor-local-fic" -Issuer $FederatedIssuer -Subject $FederatedSubject -Audience $FederatedAudience
Set-FederatedCredential -ObjectId $checkerObjectId -Name "checker-local-fic" -Issuer $FederatedIssuer -Subject $FederatedSubject -Audience $FederatedAudience

Write-Host "FIC configured on existing ExecutorAgent and CheckerAgent Blueprint apps."
Write-Host "issuer=$FederatedIssuer"
Write-Host "subject=$FederatedSubject"
Write-Host "audience=$FederatedAudience"