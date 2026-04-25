// Cloud-PPE provisioning of the FTGO Azure stack into rg-ftgo-ppe-eastus.
// Deploy:
//   IMAGE_TAG=sha-abc1234 \
//     az deployment sub create --location eastus \
//       --template-file infra/bicep/azure.bicep \
//       --parameters infra/bicep/azure.ppe.bicepparam

using 'azure.bicep'

param environmentName   = 'ppe'
param location          = 'eastus'
param imageTag          = readEnvironmentVariable('IMAGE_TAG',          'latest')
param containerRegistry = readEnvironmentVariable('CONTAINER_REGISTRY', 'ghcr.io/mghabin')
