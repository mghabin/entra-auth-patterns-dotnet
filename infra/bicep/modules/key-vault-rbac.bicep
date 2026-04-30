metadata name = 'key-vault-rbac'
metadata description = 'Grants the "Key Vault Secrets User" role to a list of principalIds at the Key Vault scope. Deterministic role-assignment names so re-runs are idempotent.'

extension az

@description('Name of the existing Key Vault to scope role assignments to.')
param keyVaultName string

@description('List of system-assigned managed identity principalIds to grant the role to.')
param principalIds array

// Built-in role: Key Vault Secrets User
// Reference: https://learn.microsoft.com/azure/role-based-access-control/built-in-roles#key-vault-secrets-user
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource kv 'Microsoft.KeyVault/vaults@2024-04-01-preview' existing = {
  name: keyVaultName
}

resource secretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in principalIds: {
  name:  guid(kv.id, principalId, keyVaultSecretsUserRoleId)
  scope: kv
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId:      principalId
    principalType:    'ServicePrincipal'
  }
}]
