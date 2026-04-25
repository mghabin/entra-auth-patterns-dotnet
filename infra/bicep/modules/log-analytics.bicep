metadata name = 'log-analytics'
metadata description = 'Single Log Analytics workspace (PerGB2018) with a 1 GB/day cap to stay safely inside the free grant.'

extension az

@description('Workspace name (≤ 63 chars, lowercase, dash-separated).')
param name string

@description('Azure region for the workspace.')
param location string

@description('Tags applied to the workspace.')
param tags object = {}

resource law 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name:     name
  location: location
  tags:     tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays:   30
    workspaceCapping: {
      dailyQuotaGb: 1
    }
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery:     'Enabled'
  }
}

@description('Resource ID of the workspace (used by App Insights and Container Apps Environment).')
output workspaceId string = law.id

@description('Customer ID (workspace GUID) consumed by the Container Apps Environment log config.')
output customerId string = law.properties.customerId

@description('Workspace name passed through for downstream tagging/diagnostics.')
output name string = law.name
