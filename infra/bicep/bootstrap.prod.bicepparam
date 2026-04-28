// Day-0 bootstrap parameters for the prod environment.
// Deploy via: ./scripts/bootstrap-env.sh ENV=prod

using 'bootstrap.bicep'

param environmentName = 'prod'
param location        = readEnvironmentVariable('LOCATION', 'eastus')
param githubOwner     = readEnvironmentVariable('GH_OWNER', 'mghabin')
param githubRepo      = readEnvironmentVariable('GH_REPO',  'entra-auth-patterns-dotnet')
