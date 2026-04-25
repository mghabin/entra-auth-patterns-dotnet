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

@description('Logical environment name. "local" keeps the existing local-dev names (ftgo-*); cloud envs (dev/ppe/prod) suffix it (ftgo-dev-*).')
@allowed([ 'local', 'dev', 'ppe', 'prod' ])
param environmentName string = 'local'

@description('OIDC redirect URI registered on the BFF for local-dev sign-in.')
param apiGatewayRedirectUri string = 'https://localhost:7101/signin-oidc'

@description('SPA redirect URI for Scalar PKCE callback on the BFF.')
param scalarRedirectUri string = 'https://localhost:7101/scalar/v1'

var effectivePrefix = environmentName == 'local' ? prefix : '${prefix}-${environmentName}'

module appRegistrations 'modules/app-registrations.bicep' = {
  name: 'app-registrations'
  params: {
    tenantId:              tenantId
    prefix:                effectivePrefix
    apiGatewayRedirectUri: apiGatewayRedirectUri
    scalarRedirectUri:     scalarRedirectUri
  }
}

module permissionGrants 'modules/permission-grants.bicep' = {
  name: 'permission-grants'
  params: {
    apps:    appRegistrations.outputs.apps
    roleIds: appRegistrations.outputs.roleIds
  }
}

output tenantId        string = tenantId
output environmentName string = environmentName
output apps            object = appRegistrations.outputs.apps
output roleIds         object = appRegistrations.outputs.roleIds

