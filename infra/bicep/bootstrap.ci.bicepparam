// Day-0 bootstrap parameters for the ci environment.
// Deploy via: ./scripts/bootstrap-env.sh ENV=ci

using 'bootstrap.bicep'

param environmentName = 'ci'
param location        = readEnvironmentVariable('LOCATION', 'eastus')
param githubOwner     = readEnvironmentVariable('GH_OWNER', 'mghabin')
param githubRepo      = readEnvironmentVariable('GH_REPO',  'entra-auth-patterns-dotnet')
