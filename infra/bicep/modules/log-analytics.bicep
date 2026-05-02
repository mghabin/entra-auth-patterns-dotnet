metadata name = 'log-analytics'
metadata description = 'Single Log Analytics workspace (PerGB2018) with a 1 GB/day cap to stay safely inside the free grant.'

extension az

@description('Workspace name (≤ 63 chars, lowercase, dash-separated).')
param name string

@description('Azure region for the workspace.')
param location string

@description('Tags applied to the workspace.')
param tags object = {}

@description('Log retention in days. 30 covers ci/ppe ops; prod uses 90+ for the audit-log compliance floor.')
@minValue(30)
@maxValue(730)
param retentionInDays int = 30

resource law 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name:     name
  location: location
  tags:     tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays:   retentionInDays
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

// Self-monitor: ship the workspace's own audit log (who queried what) into
// itself. Audit goes into LAQueryLogs table — useful for "who ran an
// expensive query" investigations and required for compliance audits.
resource lawDiag 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name:  'self-audit'
  scope: law
  properties: {
    workspaceId: law.id
    logs: [
      {
        categoryGroup: 'audit'
        enabled:       true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled:  true
      }
    ]
  }
}

@description('Resource ID of the workspace (used by App Insights and Container Apps Environment).')
output workspaceId string = law.id

@description('Customer ID (workspace GUID) consumed by the Container Apps Environment log config.')
output customerId string = law.properties.customerId

@description('Workspace name passed through for downstream tagging/diagnostics.')
output name string = law.name
