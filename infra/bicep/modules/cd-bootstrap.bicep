metadata name        = 'cd-bootstrap'
metadata description = 'CD UAMI + GitHub-Actions federated identity credential + RG-scoped role assignments. Runs at resource-group scope from bootstrap.bicep.'

extension az

@description('Azure region for the managed identity.')
param location string

@description('User-assigned managed identity name.')
param miName string

@description('Federated identity credential name.')
param ficName string

@description('OIDC subject claim. Format: repo:OWNER/REPO:environment:ENV')
param ficSubject string

@description('Built-in role-definition GUIDs to assign to the UAMI at this RG scope.')
param roleIds array

@description('Tags applied to the UAMI.')
param tags object = {}

resource mi 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name:     miName
  location: location
  tags:     tags
}

resource fic 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  name:   ficName
  parent: mi
  properties: {
    issuer:    'https://token.actions.githubusercontent.com'
    subject:   ficSubject
    audiences: [ 'api://AzureADTokenExchange' ]
  }
}

// Deterministic name → idempotent re-runs. Recovery procedure for the
// "principalId changed after MI recreate" 409 → docs/operations.md § "CD identity (UAMI) recovery".
resource roleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for roleId in roleIds: {
  name: guid(resourceGroup().id, mi.id, roleId)
  properties: {
    principalId:      mi.properties.principalId
    principalType:    'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleId)
  }
}]

output clientId    string = mi.properties.clientId
output principalId string = mi.properties.principalId
output resourceId  string = mi.id
