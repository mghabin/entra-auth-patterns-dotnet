metadata name = 'app-registrations'
metadata description = 'Provisions the 3 FTGO Entra app registrations + service principals (BFF, Orders API, Restaurants API) and exposes deterministic scope/role IDs. Workers run as Managed Identity and do not need their own app reg.'

// Scope/role IDs are deterministic GUIDs of (tenantId, app, value) so callers' references survive re-deploys.

extension graphV1
targetScope = 'tenant'

@description('Tenant id used to derive deterministic scope/role GUIDs.')
@minLength(36)
@maxLength(36)
param tenantId string

@description('Prefix applied to every app registration display name (e.g. "ftgo-dev" → "ftgo-dev-orders-api"). Cloud envs pass "ftgo-{env}".')
@minLength(2)
@maxLength(24)
param prefix string

@description('OIDC redirect URI registered on the BFF (api gateway).')
param apiGatewayRedirectUri string

@description('SPA redirect URI registered on the BFF for Scalar PKCE callback.')
param scalarRedirectUri string

@description('clientId (appId) of the BFF Container App\'s system-assigned MI. Used as the subject of the BFF federated identity credential. Empty string on cold deploy (the BFF MI does not yet exist) — the FIC is then skipped and provision-apps.sh re-runs the tenant deployment with this populated.')
param bffMiClientId string = ''

@description('Name of the BFF federated identity credential. Conventionally aca-{env}-apigateway. Ignored when bffMiClientId is empty.')
param bffFicName string = ''

var ordersReadScopeId        = guid(tenantId, '${prefix}-orders-api',      'orders.read')
var ordersProcessRoleId      = guid(tenantId, '${prefix}-orders-api',      'Orders.Process')
var restaurantsReadRoleId    = guid(tenantId, '${prefix}-restaurants-api', 'Restaurants.Read.All')
var gatewayOrdersReadScopeId = guid(tenantId, '${prefix}-apigateway',      'orders.read')

resource apiGateway 'Microsoft.Graph/applications@v1.0' = {
  uniqueName:     '${prefix}-apigateway'
  displayName:    '${prefix}-apigateway'
  signInAudience: 'AzureADMyOrg'
  web: {
    redirectUris: [ apiGatewayRedirectUri ]
    implicitGrantSettings: {
      enableIdTokenIssuance:     false
      enableAccessTokenIssuance: false
    }
  }
  spa: {
    redirectUris: [ scalarRedirectUri ]
  }
  api: {
    requestedAccessTokenVersion: 2
    oauth2PermissionScopes: [
      {
        id:                      gatewayOrdersReadScopeId
        adminConsentDisplayName: 'Read orders via the BFF'
        adminConsentDescription: 'Allows the user to invoke the BFF\'s checkout endpoints which fan-out to OrdersApi.'
        userConsentDisplayName:  'Use checkout'
        userConsentDescription:  'Allows the app to invoke checkout on your behalf.'
        value:                   'orders.read'
        type:                    'User'
        isEnabled:               true
      }
    ]
  }
}

resource apiGatewaySp 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: apiGateway.appId
}

// Federated identity credential on the BFF app reg trusting the BFF Container App's
// system-assigned MI. The BFF mints a token with audience `api://AzureADTokenExchange`
// using its system MI; the FIC tells Entra to accept that token as proof that the
// caller IS the BFF app reg, enabling SignedAssertionFromManagedIdentity for OBO/S2S
// without certs or secrets on the box.
//
// Skipped on cold deploy (bffMiClientId == '' because the Container App + its MI do
// not yet exist). provision-apps.sh re-runs this template once azure.bicep has been
// deployed and the MI clientId is known.
resource apiGatewayFic 'Microsoft.Graph/applications/federatedIdentityCredentials@v1.0' = if (!empty(bffMiClientId)) {
  name:        '${apiGateway.uniqueName}/${bffFicName}'
  audiences:   [ 'api://AzureADTokenExchange' ]
  description: 'ACA system MI → BFF app reg (SignedAssertionFromManagedIdentity)'
  #disable-next-line no-hardcoded-env-urls
  issuer:      'https://login.microsoftonline.com/${tenantId}/v2.0'
  subject:     bffMiClientId
}

resource ordersApi 'Microsoft.Graph/applications@v1.0' = {
  uniqueName:     '${prefix}-orders-api'
  displayName:    '${prefix}-orders-api'
  signInAudience: 'AzureADMyOrg'
  api: {
    requestedAccessTokenVersion: 2
    oauth2PermissionScopes: [
      {
        id:                      ordersReadScopeId
        adminConsentDisplayName: 'Read orders'
        adminConsentDescription: 'Allows the app to read the user\'s orders.'
        userConsentDisplayName:  'Read your orders'
        userConsentDescription:  'Allows the app to read your orders.'
        value:                   'orders.read'
        type:                    'User'
        isEnabled:               true
      }
    ]
  }
  appRoles: [
    {
      id:                 ordersProcessRoleId
      allowedMemberTypes: [ 'Application' ]
      displayName:        'Orders.Process'
      description:        'Process orders as a daemon (workers + gateway s2s).'
      value:              'Orders.Process'
      isEnabled:          true
    }
  ]
}

resource ordersApiSp 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: ordersApi.appId
}

resource restaurantsApi 'Microsoft.Graph/applications@v1.0' = {
  uniqueName:     '${prefix}-restaurants-api'
  displayName:    '${prefix}-restaurants-api'
  signInAudience: 'AzureADMultipleOrgs'
  api: {
    requestedAccessTokenVersion: 2
  }
  appRoles: [
    {
      id:                 restaurantsReadRoleId
      allowedMemberTypes: [ 'Application' ]
      displayName:        'Restaurants.Read.All'
      description:        'Read all restaurants (multi-tenant API).'
      value:              'Restaurants.Read.All'
      isEnabled:          true
    }
  ]
}

resource restaurantsApiSp 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: restaurantsApi.appId
}

@description('Map of app key → { appId, spId } consumed by permission-grants and provision-apps.sh.')
output apps object = {
  apiGateway:      { appId: apiGateway.appId,      spId: apiGatewaySp.id      }
  ordersApi:       { appId: ordersApi.appId,       spId: ordersApiSp.id       }
  restaurantsApi:  { appId: restaurantsApi.appId,  spId: restaurantsApiSp.id  }
}

@description('Deterministic role/scope IDs consumed by permission-grants.')
output roleIds object = {
  ordersProcess:          ordersProcessRoleId
  restaurantsRead:        restaurantsReadRoleId
  ordersReadScope:        ordersReadScopeId
  gatewayOrdersReadScope: gatewayOrdersReadScopeId
}
