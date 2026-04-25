// Entra app-reg provisioning for the cloud-PPE environment (suffixes display names with -ppe).
// See main.dev.bicepparam for the rationale on FTGO_GATEWAY_REDIRECT_URI / FTGO_SCALAR_REDIRECT_URI.

using 'main.bicep'

param tenantId             = readEnvironmentVariable('AZURE_TENANT_ID')
param prefix               = readEnvironmentVariable('FTGO_PREFIX',                'ftgo')
param environmentName      = 'ppe'
param apiGatewayRedirectUri = readEnvironmentVariable('FTGO_GATEWAY_REDIRECT_URI', 'https://ftgo-ppe-apigateway-eus.eastus.azurecontainerapps.io/signin-oidc')
param scalarRedirectUri     = readEnvironmentVariable('FTGO_SCALAR_REDIRECT_URI',  'https://ftgo-ppe-apigateway-eus.eastus.azurecontainerapps.io/scalar/v1')
