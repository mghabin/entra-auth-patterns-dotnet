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

// Deterministic GUID → idempotent re-runs (same scope+principal+role → same name).
//
// NOTE: Role assignments are scoped to the UAMI's principalId at creation.
// If you delete and recreate the CD UAMI with the same name, the principalId
// changes; the OLD role assignments will linger and the deployment will fail
// with 'role assignment already exists'. Manually delete the old assignments
// before re-running. See operations.md §<add-section> for the recovery
// procedure.
//
// Concretely: `mi.id` (resourceId) stays stable across delete+recreate
// because it is derived from name+RG, so `guid(rg.id, mi.id, roleId)` —
// which we use as the assignment name — also stays stable. ARM keys role
// assignments by name (immutable principalId on the existing record), so
// the redeploy attempts to update an immutable field and 409s.
//
// Recovery: `az role assignment delete --assignee <old-principalId>` (or
// `az role assignment list --scope <rg> --query "[?principalId=='<old>'].id"
// | xargs -n1 az role assignment delete --ids`) then re-run bootstrap.bicep.
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
