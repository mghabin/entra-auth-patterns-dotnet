metadata name = 'azure-orchestrator'
metadata description = 'Resource-group-scope orchestrator that deploys Log Analytics, App Insights, Key Vault, the Container Apps managed environment, the FTGO container apps, and Key Vault RBAC for their managed identities. The target resource group is created beforehand by infra/bicep/bootstrap.bicep, so the CD UAMI only needs RG-scope Contributor (least privilege).'

extension az

targetScope = 'resourceGroup'

@description('Logical environment name; controls resource-group, naming, and ASPNETCORE_ENVIRONMENT.')
@allowed([ 'dev', 'ppe', 'prod' ])
param environmentName string

@description('Azure region for every resource. Defaults to eastus (largest free quota).')
param location string = 'eastus'

@description('Image tag applied uniformly across every service (e.g. sha-abc1234, latest).')
param imageTag string = 'latest'

@description('Container registry base. Public images: anonymous pull, no registry credentials needed.')
param containerRegistry string = 'ghcr.io/mghabin'

@description('Tags applied to every resource. Defaults include the standard env/workload/managedBy/repo set; callers can override per-deploy.')
param tags object = {
  environment: environmentName
  workload:    'ftgo'
  managedBy:   'bicep'
  repo:        'mghabin/entra-auth-patterns-dotnet'
  costCenter:  'sample-${environmentName}'
}

@description('Resolved Entra wiring (tenantId, app reg appIds, kitchen-worker MI clientId, downstream FQDNs). Empty `{}` on cold deploy → apps fall back to appsettings.json placeholders. Populated by scripts/provision-apps.sh after Entra app regs and worker MI are known.')
param entraConfig object = {}

@description('Key Vault soft-delete retention in days. Non-prod stays at 7 so churned envs free their globally-unique vault names quickly; prod sits at 90 to align with typical compliance/recovery windows.')
@minValue(7)
@maxValue(90)
param keyVaultSoftDeleteRetentionInDays int = environmentName == 'prod' ? 90 : 7

@description('Permanently lock the vault against early purge. Required in prod; off in non-prod so the resource can be deleted without waiting out the retention clock. NOTE: irreversible once set to true.')
param keyVaultEnablePurgeProtection bool = environmentName == 'prod'

// Region → short token folded into resource names. Falls back to first 3 chars for unmapped regions.
var regionShortMap = {
  eastus:       'eus'
  eastus2:      'eus2'
  westus2:      'wus2'
  westeurope:   'weu'
  northeurope:  'neu'
  centralus:    'cus'
}
var regionShort = regionShortMap[?location] ?? toLower(take(location, 3))

var lawName      = 'ftgo-${environmentName}-law-${regionShort}'
var aiName       = 'ftgo-${environmentName}-ai-${regionShort}'
var caeName      = 'ftgo-${environmentName}-cae-${regionShort}'
// KV global uniqueness: 'kv-ftgo-{env}-{8-char hash of rg.id}' = max 21 chars (within the 24 limit).
var kvName       = 'kv-ftgo-${environmentName}-${take(uniqueString(resourceGroup().id), 8)}'

module logAnalytics 'modules/log-analytics.bicep' = {
  name:  'law'
  params: {
    name:     lawName
    location: location
    tags:     tags
  }
}

module appInsights 'modules/app-insights.bicep' = {
  name:  'ai'
  params: {
    name:                aiName
    location:            location
    workspaceResourceId: logAnalytics.outputs.workspaceId
    tags:                tags
  }
}

module keyVault 'modules/key-vault.bicep' = {
  name:  'kv'
  params: {
    name:                       kvName
    location:                   location
    tenantId:                   subscription().tenantId
    logAnalyticsWorkspaceId:    logAnalytics.outputs.workspaceId
    tags:                       tags
    softDeleteRetentionInDays:  keyVaultSoftDeleteRetentionInDays
    enablePurgeProtection:      keyVaultEnablePurgeProtection
  }
}

module containerAppsEnv 'modules/container-apps-environment.bicep' = {
  name:  'cae'
  params: {
    name:                    caeName
    location:                location
    logAnalyticsWorkspaceName: lawName
    tags:                    tags
  }
  dependsOn: [
    logAnalytics
  ]
}

module acaStack 'modules/aca-stack.bicep' = {
  name:  'aca-stack'
  params: {
    environmentName:             environmentName
    location:                    location
    containerAppsEnvironmentId:  containerAppsEnv.outputs.environmentId
    appInsightsConnectionString: appInsights.outputs.connectionString
    containerRegistry:           containerRegistry
    imageTag:                    imageTag
    regionShort:                 regionShort
    entraConfig:                 entraConfig
    tags:                        tags
  }
}

module kvRbac 'modules/key-vault-rbac.bicep' = {
  name:  'kv-rbac'
  params: {
    keyVaultName: kvName
    principalIds: acaStack.outputs.principalIds
  }
  dependsOn: [
    keyVault
  ]
}

// Prod safety net: prevent accidental `az group delete` / portal-delete of the
// entire RG. CanNotDelete still lets ARM perform in-place updates but blocks
// destructive operations until the lock is removed. Dev/PPE intentionally have
// no lock so cd-cleanup and tear-down flows stay simple.
resource rgDeleteLock 'Microsoft.Authorization/locks@2020-05-01' = if (environmentName == 'prod') {
  name: 'ftgo-prod-rg-delete-lock'
  scope: resourceGroup()
  properties: {
    level: 'CanNotDelete'
    notes: 'Prod RG delete protection. Remove via `az lock delete` only as part of an approved teardown.'
  }
}

@description('Resource group containing every FTGO resource for this environment.')
output resourceGroupName string = resourceGroup().name

@description('Log Analytics workspace resource ID.')
output logAnalyticsWorkspaceId string = logAnalytics.outputs.workspaceId

@description('App Insights component resource ID. Connection string is *not* output (kept inside the deployment).')
output appInsightsId string = appInsights.outputs.id

@description('Key Vault URI.')
output keyVaultUri string = keyVault.outputs.vaultUri

@description('Container Apps managed environment default domain.')
output containerAppsDefaultDomain string = containerAppsEnv.outputs.defaultDomain

@description('Map of camelCase shortName → { fqdn, principalId, name } for the deployed services.')
output services object = acaStack.outputs.services

@description('FQDN of the API gateway (BFF) for redirect-uri wiring on the Entra apps.')
output apiGatewayFqdn string = acaStack.outputs.services.apiGateway.fqdn

@description('Scalar API explorer URL on the BFF.')
output scalarUrl string = 'https://${acaStack.outputs.services.apiGateway.fqdn}/scalar/v1'

@description('OIDC sign-in URL on the BFF (paste into Entra app reg redirect URIs).')
output apiGatewayRedirectUri string = 'https://${acaStack.outputs.services.apiGateway.fqdn}/signin-oidc'
