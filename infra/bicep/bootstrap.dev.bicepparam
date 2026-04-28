// Day-0 bootstrap parameters for the dev environment.
// Deploy via: ./scripts/bootstrap-env.sh ENV=dev

using 'bootstrap.bicep'

param environmentName = 'dev'
param location        = readEnvironmentVariable('LOCATION', 'eastus')
param githubOwner     = readEnvironmentVariable('GH_OWNER', 'mghabin')
param githubRepo      = readEnvironmentVariable('GH_REPO',  'entra-auth-patterns-dotnet')
