metadata name = 'permission-grants'
metadata description = 'App-role admin consents (caller→target) and the BFF delegated grant. Caller can be either an app-reg SP (BFF) OR a worker Managed Identity SP (Kitchen).'

// `appRoleAssignedTo` IS the admin consent for app permissions; `oauth2PermissionGrants` is the delegated equivalent.
// Permission map: docs/sample-setup.md.

import { permissionGrant } from '../types.bicep'

extension graphV1
targetScope = 'tenant'

@description('Map of app key → { appId, spId } from app-registrations module.')
param apps object

@description('Map of role/scope id key → guid from app-registrations module.')
param roleIds object

@description('Map of worker MI key → principalId (the system MI of an ACA app). Empty = skip.')
param workerMiPrincipalIds object = {}

// 1. App-reg-SP → resource-SP grants (S2S where the caller carries an app-reg identity).
//    The BFF acquires app tokens via SignedAssertionFromManagedIdentity exchanged for a token
//    issued under its own app reg, so the caller principalId is the BFF app reg's SP.
var appRoleGrants permissionGrant[] = [
  { caller: 'apiGateway', target: 'ordersApi',      role: 'ordersProcess'   }
  { caller: 'apiGateway', target: 'restaurantsApi', role: 'restaurantsRead' }
]

resource appRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = [for g in appRoleGrants: {
  principalId: apps[g.caller].spId
  resourceId:  apps[g.target].spId
  appRoleId:   roleIds[g.role]
}]

// 2. MI → resource-SP grants (workers calling APIs as their bare Managed Identity, no app reg).
//    The KitchenWorker calls OrdersApi using ManagedIdentityCredential with `api://<orders>/.default`.
//    Entra issues an app token whose `oid` and `azp` are the MI's SP. To make that token carry
//    Orders.Process, we admin-consent the app role to the MI principalId here.
var miGrants permissionGrant[] = [
  { caller: 'kitchenWorker', target: 'ordersApi', role: 'ordersProcess' }
]

resource miAppRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = [for g in miGrants: if (contains(workerMiPrincipalIds, g.caller) && !empty(workerMiPrincipalIds[g.caller])) {
  principalId: workerMiPrincipalIds[g.caller]
  resourceId:  apps[g.target].spId
  appRoleId:   roleIds[g.role]
}]

// 3. BFF → OrdersApi delegated `orders.read` (admin-consented for all users).
resource gatewayOrdersDelegated 'Microsoft.Graph/oauth2PermissionGrants@v1.0' = {
  clientId:    apps.apiGateway.spId
  resourceId:  apps.ordersApi.spId
  consentType: 'AllPrincipals'
  scope:       'orders.read'
}
