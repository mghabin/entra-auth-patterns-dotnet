metadata name = 'permission-grants'
metadata description = 'Caller→target appRole assignments and the gateway delegated grant for the FTGO sample.'

// `appRoleAssignedTo` IS the admin consent for app permissions; `oauth2PermissionGrants` for delegated.
// Permission map: docs/sample-setup.md.

import { permissionGrant } from '../types.bicep'

extension graphV1
targetScope = 'tenant'

@description('Map of app key → { appId, spId } from app-registrations module.')
param apps object

@description('Map of role/scope id key → guid from app-registrations module.')
param roleIds object

var appRoleGrants permissionGrant[] = [
  { caller: 'apiGateway',          target: 'orderService',      role: 'ordersProcess'   }
  { caller: 'apiGateway',          target: 'restaurantService', role: 'restaurantsRead' }
  { caller: 'kitchenService',      target: 'orderService',      role: 'ordersProcess'   }
  { caller: 'accountingService',   target: 'orderService',      role: 'ordersProcess'   }
  { caller: 'deliveryService',     target: 'restaurantService', role: 'restaurantsRead' }
  { caller: 'notificationService', target: 'orderService',      role: 'ordersProcess'   }
]

resource appRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = [for g in appRoleGrants: {
  principalId: apps[g.caller].spId
  resourceId:  apps[g.target].spId
  appRoleId:   roleIds[g.role]
}]

resource gatewayOrdersDelegated 'Microsoft.Graph/oauth2PermissionGrants@v1.0' = {
  clientId:    apps.apiGateway.spId
  resourceId:  apps.orderService.spId
  consentType: 'AllPrincipals'
  scope:       'orders.read'
}
