// Cloud-CI provisioning of the FTGO Azure stack into rg-ftgo-ci-eastus.
// Deploy:
//   IMAGE_TAG=sha-abc1234 \
//     az deployment sub create --location eastus \
//       --template-file infra/bicep/azure.bicep \
//       --parameters infra/bicep/azure.ci.bicepparam

using 'azure.bicep'

param environmentName   = 'ci'
param location          = readEnvironmentVariable('LOCATION', 'eastus')
param imageTag          = readEnvironmentVariable('IMAGE_TAG',          'latest')
param containerRegistry = readEnvironmentVariable('CONTAINER_REGISTRY', 'ghcr.io/mghabin')
