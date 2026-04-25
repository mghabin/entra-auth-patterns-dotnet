metadata name = 'app-insights'
metadata description = 'Workspace-based Application Insights component pointing at the FTGO Log Analytics workspace.'

extension az

@description('Application Insights component name.')
param name string

@description('Azure region for the AI component.')
param location string

@description('Resource ID of the Log Analytics workspace backing this AI component.')
param workspaceResourceId string

@description('Tags applied to the AI component.')
param tags object = {}

resource ai 'Microsoft.Insights/components@2020-02-02' = {
  name:     name
  location: location
  tags:     tags
  kind:     'web'
  properties: {
    Application_Type:    'web'
    WorkspaceResourceId: workspaceResourceId
    IngestionMode:       'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery:     'Enabled'
  }
}

@description('Connection string injected into each container app as APPLICATIONINSIGHTS_CONNECTION_STRING.')
output connectionString string = ai.properties.ConnectionString

@description('Legacy instrumentation key (kept for compatibility; prefer the connection string).')
output instrumentationKey string = ai.properties.InstrumentationKey

@description('Resource ID of the AI component.')
output id string = ai.id
