# scripts/setup-entra.ps1
#
# PowerShell sibling of setup-entra.sh. Idempotently creates the seven
# Entra app registrations + the workload-identity federated credential
# and prints the user-secret commands.
#
# Prerequisites: az CLI logged in.

[CmdletBinding()]
param(
    [string]$GhOwner = 'mghabin',
    [string]$GhRepo  = 'entra-auth-patterns-dotnet',
    [string]$GhRef   = 'refs/heads/main'
)

$ErrorActionPreference = 'Stop'

$apps = [ordered]@{
    'ftgo-apigateway'         = 'AzureADMyOrg'
    'ftgo-orderservice'       = 'AzureADMyOrg'
    'ftgo-restaurantservice'  = 'AzureADMultipleOrgs'
    'ftgo-kitchenservice'     = 'AzureADMyOrg'
    'ftgo-accountingservice'  = 'AzureADMyOrg'
    'ftgo-deliveryservice'    = 'AzureADMyOrg'
    'ftgo-notificationservice'= 'AzureADMyOrg'
}

$appIds = @{}
foreach ($name in $apps.Keys) {
    $audience = $apps[$name]
    $id = az ad app list --display-name $name --query '[0].appId' -o tsv 2>$null
    if (-not $id) {
        Write-Host "==> Creating app: $name ($audience)"
        $id = az ad app create --display-name $name --sign-in-audience $audience --query appId -o tsv
    } else {
        Write-Host "==> Found existing app: $name ($id)"
    }
    $appIds[$name] = $id
    az ad sp show --id $id 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { az ad sp create --id $id | Out-Null }
}

$orderApp = $appIds['ftgo-orderservice']
$restApp  = $appIds['ftgo-restaurantservice']
$gateApp  = $appIds['ftgo-apigateway']
$delivApp = $appIds['ftgo-deliveryservice']

Write-Host "`n==> Setting Application ID URIs"
az ad app update --id $orderApp --identifier-uris "api://$orderApp" | Out-Null
az ad app update --id $restApp  --identifier-uris "api://$restApp"  | Out-Null

Write-Host "`n==> Adding federated credential to ftgo-deliveryservice"
$fic = @{
    name = "github-$GhOwner-$GhRepo-main"
    issuer = 'https://token.actions.githubusercontent.com'
    subject = "repo:${GhOwner}/${GhRepo}:ref:$GhRef"
    description = 'GitHub Actions OIDC for the workload-identity demo'
    audiences = @('api://AzureADTokenExchange')
} | ConvertTo-Json -Compress
az ad app federated-credential create --id $delivApp --parameters $fic 2>$null

Write-Host "`n============================================================"
Write-Host "App registrations ready. user-secrets commands:"
Write-Host "============================================================"
$tenant = az account show --query tenantId -o tsv

@"
dotnet user-secrets --project src/Ftgo.ApiGateway set "AzureAd:TenantId" "$tenant"
dotnet user-secrets --project src/Ftgo.ApiGateway set "AzureAd:ClientId" "$gateApp"
dotnet user-secrets --project src/Ftgo.ApiGateway set "DownstreamApis:Orders:Scopes:0"              "api://$orderApp/orders.read"
dotnet user-secrets --project src/Ftgo.ApiGateway set "DownstreamApis:Orders:AppPermissionScopes:0" "api://$orderApp/.default"
dotnet user-secrets --project src/Ftgo.ApiGateway set "DownstreamApis:Restaurants:AppPermissionScopes:0" "api://$restApp/.default"

dotnet user-secrets --project src/Ftgo.OrderService set "AzureAd:ClientId" "$orderApp"
dotnet user-secrets --project src/Ftgo.OrderService set "EntraAuth:AllowedClientApps:0" "$gateApp"

gh secret set AZURE_TENANT_ID         -b "$tenant"
gh secret set AZURE_CLIENT_ID         -b "$delivApp"
gh secret set FTGO_RESTAURANTS_SCOPE  -b "api://$restApp/.default"
"@
