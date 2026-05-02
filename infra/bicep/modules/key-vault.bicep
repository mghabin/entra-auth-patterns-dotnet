metadata name = 'key-vault'
metadata description = 'Standard-SKU Key Vault with RBAC authorization, soft-delete on. Soft-delete retention is parameterized so non-prod can recover quickly (7d) and prod meets compliance windows (90d). Public network access enabled for ACA reachability.'

extension az

@description('Key Vault name (3–24 chars, globally unique). Caller is responsible for the length cap.')
@minLength(3)
@maxLength(24)
param name string

@description('Azure region for the vault.')
param location string

@description('Tenant id that owns the vault (used to scope RBAC role assignments).')
param tenantId string

@description('Resource ID of the Log Analytics workspace receiving the vault audit log + AllMetrics.')
param logAnalyticsWorkspaceId string

@description('Tags applied to the vault.')
param tags object = {}

@description('Soft-delete retention in days. Microsoft minimum is 7, maximum is 90. Prod should sit at 90 to meet typical audit/recovery windows; non-prod stays at 7 to free names quickly.')
@minValue(7)
@maxValue(90)
param softDeleteRetentionInDays int = 7

@description('When true, the vault cannot be purged before retention expires (irreversible). Required for prod compliance; off in non-prod so churned environments do not pile up undeletable vaults.')
param enablePurgeProtection bool = false

resource kv 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name:     name
  location: location
  tags:     tags
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name:   'standard'
    }
    enableRbacAuthorization:   true
    enableSoftDelete:          true
    softDeleteRetentionInDays: softDeleteRetentionInDays
    enablePurgeProtection:     enablePurgeProtection ? true : null
    publicNetworkAccess:       'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass:        'AzureServices'
    }
  }
}

// AuditEvent → Log Analytics. Captures every secret/key/cert read & policy
// change. Required for any production audit trail; cheap on a dev workload
// where no secrets are actually read.
resource kvDiag 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name:  'to-law'
  scope: kv
  properties: {
    workspaceId: logAnalyticsWorkspaceId
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

@description('Vault URI (https://<name>.vault.azure.net/).')
output vaultUri string = kv.properties.vaultUri

@description('Vault name passed through for RBAC scoping.')
output keyVaultName string = kv.name

@description('Vault resource ID (used for role assignment scoping).')
output id string = kv.id
