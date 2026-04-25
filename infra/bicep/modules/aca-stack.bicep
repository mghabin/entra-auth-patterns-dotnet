metadata name = 'aca-stack'
metadata description = 'Iterates over the FTGO service list and instantiates one container-app per service. Returns a shortName-keyed map of fqdn/principalId/name for downstream RBAC and outputs.'

@description('Logical environment name (dev/ppe/prod).')
param environmentName string

@description('Azure region for the apps.')
param location string

@description('Resource ID of the Container Apps managed environment hosting all 7 apps.')
param containerAppsEnvironmentId string

@description('Application Insights connection string injected into every app.')
param appInsightsConnectionString string

@description('Container registry base (e.g. ghcr.io/mghabin). Final image: <registry>/ftgo-<shortName>:<imageTag>.')
param containerRegistry string

@description('Image tag applied uniformly across all 7 services (e.g. sha-abc1234, latest).')
param imageTag string

@description('Region-short token folded into the app name (e.g. eus for eastus).')
param regionShort string

@description('Tags applied to every container app.')
param tags object = {}

@description('Service definitions: project = csproj folder name, shortName = lowercase image/name suffix, isWebApp = whether to expose HTTP ingress + /health probe.')
param services array = [
  { project: 'Ftgo.ApiGateway',           shortName: 'apigateway',          isWebApp: true  }
  { project: 'Ftgo.OrderService',         shortName: 'orderservice',        isWebApp: true  }
  { project: 'Ftgo.RestaurantService',    shortName: 'restaurantservice',   isWebApp: true  }
  { project: 'Ftgo.KitchenService',       shortName: 'kitchenservice',      isWebApp: false }
  { project: 'Ftgo.AccountingService',    shortName: 'accountingservice',   isWebApp: false }
  { project: 'Ftgo.DeliveryService',      shortName: 'deliveryservice',     isWebApp: false }
  { project: 'Ftgo.NotificationService',  shortName: 'notificationservice', isWebApp: false }
]

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
    tags:                        tags
  }
}]

// Map output is hard-coded to the canonical 7-service ordering of the `services`
// param default. If callers override `services`, they must keep the same ordering
// (apigateway, orderservice, restaurantservice, kitchenservice, accountingservice,
// deliveryservice, notificationservice) or rebuild the map themselves.
@description('Map of shortName → { fqdn, principalId, name } for the deployed apps.')
output services object = {
  apigateway:          { fqdn: containerApps[0].outputs.fqdn, principalId: containerApps[0].outputs.principalId, name: containerApps[0].outputs.name }
  orderservice:        { fqdn: containerApps[1].outputs.fqdn, principalId: containerApps[1].outputs.principalId, name: containerApps[1].outputs.name }
  restaurantservice:   { fqdn: containerApps[2].outputs.fqdn, principalId: containerApps[2].outputs.principalId, name: containerApps[2].outputs.name }
  kitchenservice:      { fqdn: containerApps[3].outputs.fqdn, principalId: containerApps[3].outputs.principalId, name: containerApps[3].outputs.name }
  accountingservice:   { fqdn: containerApps[4].outputs.fqdn, principalId: containerApps[4].outputs.principalId, name: containerApps[4].outputs.name }
  deliveryservice:     { fqdn: containerApps[5].outputs.fqdn, principalId: containerApps[5].outputs.principalId, name: containerApps[5].outputs.name }
  notificationservice: { fqdn: containerApps[6].outputs.fqdn, principalId: containerApps[6].outputs.principalId, name: containerApps[6].outputs.name }
}

@description('Flat list of system-assigned principalIds for downstream RBAC (Key Vault, etc.).')
output principalIds array = [for (svc, i) in services: containerApps[i].outputs.principalId]
