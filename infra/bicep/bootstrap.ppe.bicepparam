// Day-0 bootstrap parameters for the ppe environment.
// Deploy via: ./scripts/bootstrap-env.sh ENV=ppe

using 'bootstrap.bicep'

param environmentName = 'ppe'
param location        = 'eastus'
param githubOwner     = readEnvironmentVariable('GH_OWNER', 'mghabin')
param githubRepo      = readEnvironmentVariable('GH_REPO',  'entra-auth-patterns-dotnet')
