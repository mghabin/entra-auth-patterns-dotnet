// Cloud-prod provisioning of the FTGO Azure stack into rg-ftgo-prod-eastus.
// Deploy:
//   IMAGE_TAG=sha-abc1234 \
//     az deployment sub create --location eastus \
//       --template-file infra/bicep/azure.bicep \
//       --parameters infra/bicep/azure.prod.bicepparam

using 'azure.bicep'

param environmentName   = 'prod'
param location          = 'eastus'
param imageTag          = readEnvironmentVariable('IMAGE_TAG',          'latest')
param containerRegistry = readEnvironmentVariable('CONTAINER_REGISTRY', 'ghcr.io/mghabin')
