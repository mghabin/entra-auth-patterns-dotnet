metadata name = 'key-vault'
metadata description = 'Standard-SKU Key Vault with RBAC authorization, soft-delete on, 7-day retention. Public network access enabled for ACA reachability.'

extension az

@description('Key Vault name (3–24 chars, globally unique). Caller is responsible for the length cap.')
@minLength(3)
@maxLength(24)
param name string

@description('Azure region for the vault.')
param location string

@description('Tenant id that owns the vault (used to scope RBAC role assignments).')
param tenantId string

@description('Tags applied to the vault.')
param tags object = {}

resource kv 'Microsoft.KeyVault/vaults@2024-04-01-preview' = {
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
    softDeleteRetentionInDays: 7
    enablePurgeProtection:     null
    publicNetworkAccess:       'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass:        'AzureServices'
    }
  }
}

@description('Vault URI (https://<name>.vault.azure.net/).')
output vaultUri string = kv.properties.vaultUri

@description('Vault name passed through for RBAC scoping.')
output keyVaultName string = kv.name

@description('Vault resource ID (used for role assignment scoping).')
output id string = kv.id
