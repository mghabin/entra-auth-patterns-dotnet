metadata name        = 'bootstrap'
metadata description = 'Day-0 per-environment bootstrap: creates rg-ftgo-{env}-{location}, the CD UAMI, its GitHub-Actions OIDC federated credential, and RG-scoped RBAC. Run once per env by an Azure Owner from a developer machine; subsequent deploys use the UAMI. The GitHub side (Environment, env vars, repo secret) is configured by scripts/bootstrap-env.sh from this template`s outputs.'

extension az

targetScope = 'subscription'

@description('Logical environment name; controls RG/MI naming and the federated subject.')
@allowed([ 'dev', 'ppe', 'prod' ])
param environmentName string

@description('Azure region. Defaults to eastus (largest free quota).')
param location string = 'eastus'

@description('GitHub repo owner (org or user). Used in the OIDC subject claim.')
param githubOwner string

@description('GitHub repo name. Used in the OIDC subject claim.')
param githubRepo string

// ------- Naming (kept identical to the previous bootstrap-env.sh values) -------
var rgName     = 'rg-ftgo-${environmentName}-${location}'
var miName     = 'ftgo-${environmentName}-cd-mi'
var ficName    = 'github-${environmentName}'
var ficSubject = 'repo:${githubOwner}/${githubRepo}:environment:${environmentName}'

// Built-in role-definition GUIDs.
// Source: https://learn.microsoft.com/azure/role-based-access-control/built-in-roles
var roles = {
  Contributor:             'b24988ac-6180-42a0-ab88-20f7382dd24c'
  UserAccessAdministrator: '18d7d88d-d35e-4fb5-a5c3-7773c20a72d9'
}

// All envs need User Access Administrator (RG-scoped) so the deploy pipeline
// can grant Key Vault RBAC to the per-app managed identities created by
// azure.bicep (modules/key-vault-rbac.bicep). The role is scoped to this RG
// only — the UAMI cannot assign roles outside its environment.
var roleIds = [ roles.Contributor, roles.UserAccessAdministrator ]

var tags = {
  environment: environmentName
  workload:    'ftgo'
  managedBy:   'bicep'
  purpose:     'cd-bootstrap'
}

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name:     rgName
  location: location
  tags:     tags
}

module cdBootstrap 'modules/cd-bootstrap.bicep' = {
  scope: resourceGroup(rg.name)
  name:  'cd-bootstrap'
  params: {
    location:   location
    miName:     miName
    ficName:    ficName
    ficSubject: ficSubject
    roleIds:    roleIds
    tags:       tags
  }
}

output resourceGroupName string = rg.name
output clientId          string = cdBootstrap.outputs.clientId
output principalId       string = cdBootstrap.outputs.principalId
output subscriptionId    string = subscription().subscriptionId
output tenantId          string = subscription().tenantId
output federatedSubject  string = ficSubject
