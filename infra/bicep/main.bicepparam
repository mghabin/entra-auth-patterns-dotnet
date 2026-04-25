// Sample bicepparam for local provisioning.
// Override at deploy time:
//   AZURE_TENANT_ID=$(az account show --query tenantId -o tsv) \
//     az deployment tenant create --location eastus \
//       --template-file infra/bicep/main.bicep \
//       --parameters infra/bicep/main.bicepparam
//
// scripts/deploy.sh wraps this and adds the post-deploy steps Bicep can't do
// (federated credential, dev cert, dotnet user-secrets, gh repo secrets).

using 'main.bicep'

param tenantId             = readEnvironmentVariable('AZURE_TENANT_ID')
param prefix               = readEnvironmentVariable('FTGO_PREFIX',                'ftgo')
param environmentName      = 'local'
param apiGatewayRedirectUri = readEnvironmentVariable('FTGO_GATEWAY_REDIRECT_URI', 'https://localhost:7101/signin-oidc')
param scalarRedirectUri     = readEnvironmentVariable('FTGO_SCALAR_REDIRECT_URI',  'https://localhost:7101/scalar/v1')
