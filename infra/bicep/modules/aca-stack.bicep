metadata name = 'aca-stack'
metadata description = 'Iterates over the FTGO service list and instantiates one container-app per service. Returns a key-keyed map of fqdn/principalId/name for downstream RBAC and outputs.'

@description('Logical environment name (dev/ppe/prod).')
param environmentName string

@description('Azure region for the apps.')
param location string

@description('Resource ID of the Container Apps managed environment hosting all apps.')
param containerAppsEnvironmentId string

@description('Application Insights connection string injected into every app.')
@secure()
param appInsightsConnectionString string

@description('Container registry base (e.g. ghcr.io/mghabin). Final image: <registry>/ftgo-<shortName>:<imageTag>.')
param containerRegistry string

@description('Image tag applied uniformly across all services (e.g. sha-abc1234, latest).')
param imageTag string

@description('Region-short token folded into the app name (e.g. eus for eastus).')
param regionShort string

@description('Tags applied to every container app.')
param tags object = {}

@description('Resolved Entra wiring (tenantId, app reg appIds, kitchen-worker MI clientId, downstream FQDNs). Empty `{}` on cold deploy → no Entra env vars are injected and apps fall back to appsettings.json placeholders. Populated by scripts/provision-apps.sh after Entra app regs and worker MI are known.')
param entraConfig object = {}

// REQUIRED ORDER: services[0]=apiGateway, services[1]=ordersApi,
//                 services[2]=restaurantsApi, services[3]=kitchenWorker.
// The `services` output below indexes containerApps[] positionally because
// Bicep does not allow for-expressions inside `toObject(...)` for output
// values (BCP138) AND vars cannot reference module outputs (BCP182). Until
// that limitation is lifted (tracked as a follow-up to migrate to a true
// keyed-map output), callers MUST preserve both the length AND the order
// of the default array — overriding with a different ordering will silently
// produce a mis-keyed `services` map (e.g. apiGateway.fqdn pointing at the
// orders-api ingress). Bicep has no `assert` keyword, so this constraint
// is enforced by convention + review, not at template-evaluation time.
@description('Service definitions. project = csproj folder name; shortName = lowercase image/name suffix; key = stable Bicep map key (camelCase) used to dispatch env vars and build the output map; isWebApp = whether to expose HTTP ingress + /health/live + /health/ready probes. REQUIRED ORDER: [0]=apiGateway, [1]=ordersApi, [2]=restaurantsApi, [3]=kitchenWorker — the `services` output is positionally keyed against this ordering. Do NOT reorder or resize without also rewriting the `services` output map below.')
@metadata({
  requiredOrder:    [ 'apiGateway', 'ordersApi', 'restaurantsApi', 'kitchenWorker' ]
  requiredLength:   4
  orderingConstraint: 'The `services` output below is positionally keyed against this array (containerApps[0]=apiGateway, etc). Reordering or resizing produces a silently mis-keyed output — do not override unless you also rewrite the output map.'
})
param services array = [
  { project: 'Ftgo.ApiGateway',      shortName: 'apigateway',       key: 'apiGateway',     isWebApp: true  }
  { project: 'Ftgo.Orders.Api',      shortName: 'orders-api',       key: 'ordersApi',      isWebApp: true  }
  { project: 'Ftgo.Restaurants.Api', shortName: 'restaurants-api',  key: 'restaurantsApi', isWebApp: true  }
  { project: 'Ftgo.Kitchen.Worker',  shortName: 'kitchen-worker',   key: 'kitchenWorker',  isWebApp: false }
]

// `entraConfig` is treated as all-or-nothing. We require `tenantId` as the marker key
// because partial population would silently produce broken env vars (e.g. AzureAd:ClientId
// without AzureAd:TenantId). provision-apps.sh either populates everything or sends `{}`.
var hasEntra = contains(entraConfig, 'tenantId')

// Microsoft public-client appIds — pre-registered, well-known. Whitelisted ONLY in dev so
// developer laptops can call dev cloud APIs via DefaultAzureCredential / AzureCliCredential.
// ppe and prod accept tokens only from real workload identities (BFF + workers).
//   Azure CLI:          04b07795-8ddb-461a-bbee-02f9e1bf7b46
//   Visual Studio Code: aebc6443-996d-45c2-90f0-388ff96faa56
var devPublicClients = environmentName == 'dev' ? [
  '04b07795-8ddb-461a-bbee-02f9e1bf7b46'
  'aebc6443-996d-45c2-90f0-388ff96faa56'
] : []

var apiGatewayEnv = hasEntra ? [
  { name: 'AzureAd__TenantId',                              value: entraConfig.tenantId }
  { name: 'AzureAd__ClientId',                              value: entraConfig.bffAppId }
  { name: 'AzureAd__ClientCredentials__0__SourceType',      value: 'SignedAssertionFromManagedIdentity' }
  { name: 'DownstreamApis__Orders__BaseUrl',                value: 'https://${entraConfig.ordersApiFqdn}/' }
  { name: 'DownstreamApis__Orders__Scopes__0',              value: 'api://${entraConfig.ordersApiAppId}/orders.read' }
  { name: 'DownstreamApis__Orders__AppPermissionScopes__0', value: 'api://${entraConfig.ordersApiAppId}/.default' }
  { name: 'DownstreamApis__Restaurants__BaseUrl',           value: 'https://${entraConfig.restaurantsApiFqdn}/' }
  { name: 'DownstreamApis__Restaurants__AppPermissionScopes__0', value: 'api://${entraConfig.restaurantsApiAppId}/.default' }
] : []

// Build OrdersApi allow-list: BFF first (index 0), then dev public clients (1..N), then
// kitchen-worker MI clientId (last). Order is stable across envs because dev-only entries
// only ever appear in dev.
var ordersApiAllowedClientsBase = hasEntra ? concat(
  [ entraConfig.bffAppId ],
  devPublicClients,
  [ entraConfig.kitchenWorkerMiClientId ]
) : []
var ordersApiEnv = hasEntra ? concat([
  { name: 'AzureAd__TenantId', value: entraConfig.tenantId }
  { name: 'AzureAd__ClientId', value: entraConfig.ordersApiAppId }
], map(range(0, length(ordersApiAllowedClientsBase)), i => {
  name:  'EntraAuth__AllowedClientApps__${i}'
  value: ordersApiAllowedClientsBase[i]
})) : []

var restaurantsApiAllowedClientsBase = hasEntra ? concat(
  [ entraConfig.bffAppId ],
  devPublicClients
) : []
var restaurantsApiEnv = hasEntra ? concat([
  { name: 'AzureAd__TenantId',                  value: entraConfig.tenantId }
  { name: 'AzureAd__ClientId',                  value: entraConfig.restaurantsApiAppId }
  { name: 'EntraAuth__AllowedTenantIds__0',     value: entraConfig.tenantId }
], map(range(0, length(restaurantsApiAllowedClientsBase)), i => {
  name:  'EntraAuth__AllowedClientApps__${i}'
  value: restaurantsApiAllowedClientsBase[i]
})) : []

// KitchenWorker uses bare MI (no app reg). Just point it at OrdersApi.
var kitchenWorkerEnv = hasEntra ? [
  { name: 'Downstream__BaseUrl', value: 'https://${entraConfig.ordersApiFqdn}/' }
  { name: 'Downstream__Scope',   value: 'api://${entraConfig.ordersApiAppId}/.default' }
] : []

// Per-service env arrays, dispatched by service `key` (camelCase). A service
// whose key is absent here gets an empty extraEnvVars array (e.g. a hypothetical
// new "billingApi" service added without env wiring would deploy bare). This
// removes the previous index-based coupling between this map and the `services`
// param ordering.
var envByKey = hasEntra ? {
  apiGateway:     apiGatewayEnv
  ordersApi:      ordersApiEnv
  restaurantsApi: restaurantsApiEnv
  kitchenWorker:  kitchenWorkerEnv
} : {}

module containerApps 'container-app.bicep' = [for svc in services: {
  name: 'aca-${svc.shortName}'
  params: {
    appName:                     'ftgo-${environmentName}-${svc.shortName}-${regionShort}'
    serviceNameLower:            svc.shortName
    location:                    location
    containerAppsEnvironmentId:  containerAppsEnvironmentId
    image:                       '${containerRegistry}/ftgo-${svc.shortName}:${imageTag}'
    appInsightsConnectionString: appInsightsConnectionString
    environmentName:             environmentName
    enableIngress:               svc.isWebApp
    extraEnvVars:                envByKey[?svc.key] ?? []
    tags:                        tags
  }
}]

// Map output is hard-coded to the canonical service ordering of the `services`
// param default. Bicep does not allow for-expressions inside `toObject(...)` for
// output values (BCP138) AND vars cannot reference module outputs (BCP182), so
// neither dynamic-key construction works. If callers override `services`, they
// must keep the same ordering (apiGateway, ordersApi, restaurantsApi,
// kitchenWorker) or rebuild this map.
@description('Map of camelCase key → { fqdn, principalId, name } for the deployed apps. Keys are the camelCase form of services[*].shortName.')
output services object = {
  apiGateway:      { fqdn: containerApps[0].outputs.fqdn, principalId: containerApps[0].outputs.principalId, name: containerApps[0].outputs.name }
  ordersApi:       { fqdn: containerApps[1].outputs.fqdn, principalId: containerApps[1].outputs.principalId, name: containerApps[1].outputs.name }
  restaurantsApi:  { fqdn: containerApps[2].outputs.fqdn, principalId: containerApps[2].outputs.principalId, name: containerApps[2].outputs.name }
  kitchenWorker:   { fqdn: containerApps[3].outputs.fqdn, principalId: containerApps[3].outputs.principalId, name: containerApps[3].outputs.name }
}

@description('Flat list of system-assigned principalIds for downstream RBAC (Key Vault, etc.).')
output principalIds array = [for (svc, i) in services: containerApps[i].outputs.principalId]
