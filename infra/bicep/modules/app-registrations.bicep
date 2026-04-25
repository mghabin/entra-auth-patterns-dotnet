metadata name = 'app-registrations'
metadata description = 'Provisions the 7 FTGO app registrations + service principals and exposes deterministic scope/role IDs.'

// Scope/role IDs are deterministic GUIDs of (tenantId, app, value) so callers' references survive re-deploys.

extension graphV1
targetScope = 'tenant'

@description('Tenant id used to derive deterministic scope/role GUIDs.')
@minLength(36)
@maxLength(36)
param tenantId string

@description('Prefix applied to every app registration display name (e.g. "ftgo" → "ftgo-orderservice"). When called from the cloud envs the orchestrator passes "ftgo-{env}".')
@minLength(2)
@maxLength(24)
param prefix string

@description('OIDC redirect URI registered on the BFF (api gateway) for local-dev sign-in.')
param apiGatewayRedirectUri string

@description('SPA redirect URI registered on the BFF for Scalar PKCE callback.')
param scalarRedirectUri string

var ordersReadScopeId        = guid(tenantId, '${prefix}-orderservice',      'orders.read')
var ordersProcessRoleId      = guid(tenantId, '${prefix}-orderservice',      'Orders.Process')
var restaurantsReadRoleId    = guid(tenantId, '${prefix}-restaurantservice', 'Restaurants.Read.All')
var gatewayOrdersReadScopeId = guid(tenantId, '${prefix}-apigateway',        'orders.read')

var workerApps = [
  'kitchenService'
  'accountingService'
  'deliveryService'
  'notificationService'
]

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
        adminConsentDescription: 'Allows the user to invoke the BFF\'s checkout endpoints which fan-out to OrderService.'
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

resource orderService 'Microsoft.Graph/applications@v1.0' = {
  uniqueName:     '${prefix}-orderservice'
  displayName:    '${prefix}-orderservice'
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

resource orderServiceSp 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: orderService.appId
}

resource restaurantService 'Microsoft.Graph/applications@v1.0' = {
  uniqueName:     '${prefix}-restaurantservice'
  displayName:    '${prefix}-restaurantservice'
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

resource restaurantServiceSp 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: restaurantService.appId
}

resource workerApp 'Microsoft.Graph/applications@v1.0' = [for name in workerApps: {
  uniqueName:     '${prefix}-${toLower(name)}'
  displayName:    '${prefix}-${toLower(name)}'
  signInAudience: 'AzureADMyOrg'
}]

resource workerAppSp 'Microsoft.Graph/servicePrincipals@v1.0' = [for (_, i) in workerApps: {
  appId: workerApp[i].appId
}]

@description('Map of app key → { appId, spId } consumed by permission-grants and deploy.sh.')
output apps object = {
  apiGateway:          { appId: apiGateway.appId,        spId: apiGatewaySp.id        }
  orderService:        { appId: orderService.appId,      spId: orderServiceSp.id      }
  restaurantService:   { appId: restaurantService.appId, spId: restaurantServiceSp.id }
  kitchenService:      { appId: workerApp[0].appId,      spId: workerAppSp[0].id      }
  accountingService:   { appId: workerApp[1].appId,      spId: workerAppSp[1].id      }
  deliveryService:     { appId: workerApp[2].appId,      spId: workerAppSp[2].id      }
  notificationService: { appId: workerApp[3].appId,      spId: workerAppSp[3].id      }
}

@description('Deterministic role/scope IDs consumed by permission-grants.')
output roleIds object = {
  ordersProcess:        ordersProcessRoleId
  restaurantsRead:      restaurantsReadRoleId
  ordersReadScope:      ordersReadScopeId
  gatewayOrdersReadScope: gatewayOrdersReadScopeId
}
