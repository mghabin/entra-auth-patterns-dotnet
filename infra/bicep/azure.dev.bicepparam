// Cloud-dev provisioning of the FTGO Azure stack into rg-ftgo-dev-eastus.
// Deploy:
//   IMAGE_TAG=sha-abc1234 \
//     az deployment sub create --location eastus \
//       --template-file infra/bicep/azure.bicep \
//       --parameters infra/bicep/azure.dev.bicepparam

using 'azure.bicep'

param environmentName   = 'dev'
param location          = readEnvironmentVariable('LOCATION', 'eastus')
param imageTag          = readEnvironmentVariable('IMAGE_TAG',          'latest')
param containerRegistry = readEnvironmentVariable('CONTAINER_REGISTRY', 'ghcr.io/mghabin')
