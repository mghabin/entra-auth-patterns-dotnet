metadata name = 'ftgo-entra-apps'
metadata description = 'Tenant-scope orchestrator for the FTGO Entra auth-patterns sample. Idempotent.'

// Tenant-scope orchestrator. Idempotent — re-running picks up existing apps by uniqueName.

extension graphV1
targetScope = 'tenant'

@description('Tenant id used to derive deterministic scope/role GUIDs.')
@minLength(36)
@maxLength(36)
param tenantId string

@description('Prefix applied to every app registration display name.')
@minLength(2)
@maxLength(16)
param prefix string = 'ftgo'

@description('Logical deployment-tier name. Suffixed onto the prefix (ftgo-ci-*, ftgo-ppe-*, ftgo-prod-*) so each tier owns an isolated set of app regs. "local" is a documented fourth tier (no Azure resources) and is therefore not in this list.')
@allowed([ 'ci', 'ppe', 'prod' ])
param environmentName string = 'ci'

@description('OIDC redirect URI registered on the BFF for local-dev sign-in.')
param apiGatewayRedirectUri string = 'https://localhost:7101/signin-oidc'

@description('SPA redirect URI for Scalar PKCE callback on the BFF.')
param scalarRedirectUri string = 'https://localhost:7101/scalar/v1'

@description('Map of worker MI key → principalId (system MI of the corresponding ACA app). Passed in by provision-apps.sh after reading azure.bicep outputs. Empty during a cold deploy — the script re-runs the tenant deploy with this populated once the ACA stack exists.')
param workerMiPrincipalIds object = {}

@description('clientId (appId) of the BFF Container App\'s system-assigned MI. Used as the subject of the BFF federated identity credential. Empty on cold deploy; populated on the warm wire-back run by provision-apps.sh.')
param bffMiClientId string = ''

var effectivePrefix = '${prefix}-${environmentName}'
var bffFicName      = 'aca-${environmentName}-apigateway'

module appRegistrations 'modules/app-registrations.bicep' = {
  name: 'app-registrations'
  params: {
    tenantId:              tenantId
    prefix:                effectivePrefix
    apiGatewayRedirectUri: apiGatewayRedirectUri
    scalarRedirectUri:     scalarRedirectUri
    bffMiClientId:         bffMiClientId
    bffFicName:            bffFicName
  }
}

module permissionGrants 'modules/permission-grants.bicep' = {
  name: 'permission-grants'
  params: {
    apps:                  appRegistrations.outputs.apps
    roleIds:               appRegistrations.outputs.roleIds
    workerMiPrincipalIds:  workerMiPrincipalIds
  }
}

output tenantId        string = tenantId
output environmentName string = environmentName
output apps            object = appRegistrations.outputs.apps
output roleIds         object = appRegistrations.outputs.roleIds

