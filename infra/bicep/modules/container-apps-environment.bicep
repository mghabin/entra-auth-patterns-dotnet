metadata name = 'container-apps-environment'
metadata description = 'Container Apps managed environment (Consumption) wired to the FTGO Log Analytics workspace.'

extension az

@description('Managed environment name.')
param name string

@description('Azure region for the environment.')
param location string

@description('Resource ID of the Log Analytics workspace receiving container app logs.')
param logAnalyticsWorkspaceId string

@description('Customer ID (workspace GUID) of the Log Analytics workspace.')
param logAnalyticsCustomerId string

@description('Tags applied to the environment.')
param tags object = {}

resource law 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: last(split(logAnalyticsWorkspaceId, '/'))
}

resource cae 'Microsoft.App/managedEnvironments@2024-10-02-preview' = {
  name:     name
  location: location
  tags:     tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsCustomerId
        sharedKey:  law.listKeys().primarySharedKey
      }
    }
    zoneRedundant: false
  }
}

@description('Resource ID of the managed environment (consumed by container-app modules).')
output environmentId string = cae.id

@description('Default domain ACA assigns to apps in this environment (e.g. <random>.eastus.azurecontainerapps.io).')
output defaultDomain string = cae.properties.defaultDomain

@description('Static IP for the environment (useful for diagnostics).')
output staticIp string = cae.properties.staticIp
