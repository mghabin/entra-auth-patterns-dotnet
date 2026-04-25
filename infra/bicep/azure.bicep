metadata name = 'azure-orchestrator'
metadata description = 'Subscription-scope orchestrator that creates rg-ftgo-{env}-{location} and deploys Log Analytics, App Insights, Key Vault, the Container Apps managed environment, the 7 FTGO container apps, and Key Vault RBAC for their managed identities.'

extension az

targetScope = 'subscription'

@description('Logical environment name; controls resource-group, naming, and ASPNETCORE_ENVIRONMENT.')
@allowed([ 'dev', 'ppe', 'prod' ])
param environmentName string

@description('Azure region for every resource. Defaults to eastus (largest free quota).')
param location string = 'eastus'

@description('Image tag applied uniformly across all 7 services (e.g. sha-abc1234, latest).')
param imageTag string = 'latest'

@description('Container registry base. Public images: anonymous pull, no registry credentials needed.')
param containerRegistry string = 'ghcr.io/mghabin'

@description('Tags applied to every resource.')
param tags object = {
  environment: environmentName
  workload:    'ftgo'
  managedBy:   'bicep'
}

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

var rgName       = 'rg-ftgo-${environmentName}-${location}'
var lawName      = 'ftgo-${environmentName}-law-${regionShort}'
var aiName       = 'ftgo-${environmentName}-ai-${regionShort}'
var caeName      = 'ftgo-${environmentName}-cae-${regionShort}'
// KV global uniqueness: 'kv-ftgo-{env}-{8-char hash of rg.id}' = max 21 chars (within the 24 limit).
var kvName       = 'kv-ftgo-${environmentName}-${take(uniqueString(subscription().subscriptionId, rgName), 8)}'

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name:     rgName
  location: location
  tags:     tags
}

module logAnalytics 'modules/log-analytics.bicep' = {
  name:  'law'
  scope: resourceGroup(rg.name)
  params: {
    name:     lawName
    location: location
    tags:     tags
  }
}

module appInsights 'modules/app-insights.bicep' = {
  name:  'ai'
  scope: resourceGroup(rg.name)
  params: {
    name:                aiName
    location:            location
    workspaceResourceId: logAnalytics.outputs.workspaceId
    tags:                tags
  }
}

module keyVault 'modules/key-vault.bicep' = {
  name:  'kv'
  scope: resourceGroup(rg.name)
  params: {
    name:     kvName
    location: location
    tenantId: subscription().tenantId
    tags:     tags
  }
}

module containerAppsEnv 'modules/container-apps-environment.bicep' = {
  name:  'cae'
  scope: resourceGroup(rg.name)
  params: {
    name:                    caeName
    location:                location
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceId
    logAnalyticsCustomerId:  logAnalytics.outputs.customerId
    tags:                    tags
  }
}

module acaStack 'modules/aca-stack.bicep' = {
  name:  'aca-stack'
  scope: resourceGroup(rg.name)
  params: {
    environmentName:             environmentName
    location:                    location
    containerAppsEnvironmentId:  containerAppsEnv.outputs.environmentId
    appInsightsConnectionString: appInsights.outputs.connectionString
    containerRegistry:           containerRegistry
    imageTag:                    imageTag
    regionShort:                 regionShort
    tags:                        tags
  }
}

module kvRbac 'modules/key-vault-rbac.bicep' = {
  name:  'kv-rbac'
  scope: resourceGroup(rg.name)
  params: {
    keyVaultName: keyVault.outputs.keyVaultName
    principalIds: acaStack.outputs.principalIds
  }
}

@description('Resource group containing every FTGO resource for this environment.')
output resourceGroupName string = rg.name

@description('Log Analytics workspace resource ID.')
output logAnalyticsWorkspaceId string = logAnalytics.outputs.workspaceId

@description('App Insights component resource ID. Connection string is *not* output (kept inside the deployment).')
output appInsightsId string = appInsights.outputs.id

@description('Key Vault URI.')
output keyVaultUri string = keyVault.outputs.vaultUri

@description('Container Apps managed environment default domain.')
output containerAppsDefaultDomain string = containerAppsEnv.outputs.defaultDomain

@description('Map of shortName → { fqdn, principalId, name } for the 7 deployed services.')
output services object = acaStack.outputs.services

@description('FQDN of the API gateway (BFF) for redirect-uri wiring on the Entra apps.')
output apiGatewayFqdn string = acaStack.outputs.services.apigateway.fqdn

@description('Scalar API explorer URL on the BFF.')
output scalarUrl string = 'https://${acaStack.outputs.services.apigateway.fqdn}/scalar/v1'

@description('OIDC sign-in URL on the BFF (paste into Entra app reg redirect URIs).')
output apiGatewayRedirectUri string = 'https://${acaStack.outputs.services.apigateway.fqdn}/signin-oidc'
