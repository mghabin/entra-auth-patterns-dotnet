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

@description('OIDC redirect URI registered on the BFF for local-dev sign-in.')
param apiGatewayRedirectUri string = 'https://localhost:7101/signin-oidc'

module appRegistrations 'modules/app-registrations.bicep' = {
  name: 'app-registrations'
  params: {
    tenantId:              tenantId
    prefix:                prefix
    apiGatewayRedirectUri: apiGatewayRedirectUri
  }
}

module permissionGrants 'modules/permission-grants.bicep' = {
  name: 'permission-grants'
  params: {
    apps:    appRegistrations.outputs.apps
    roleIds: appRegistrations.outputs.roleIds
  }
}

output tenantId string = tenantId
output apps     object = appRegistrations.outputs.apps
output roleIds  object = appRegistrations.outputs.roleIds

