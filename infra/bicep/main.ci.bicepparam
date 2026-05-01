// Entra app-reg provisioning for the cloud-CI environment (suffixes display names with -ci).
//
// The redirect URIs need to point at the BFF's ACA FQDN, which we don't know
// until azure.bicep has finished deploying (the FQDN folds in cae.defaultDomain,
// a per-environment random token). The bicepparam therefore reads the URIs
// from env vars set by scripts/deploy.sh after the azure-stack deployment:
//
//   FTGO_GATEWAY_REDIRECT_URI=https://<bff-fqdn>/signin-oidc
//   FTGO_SCALAR_REDIRECT_URI=https://<bff-fqdn>/scalar/v1
//
// The placeholder defaults are based on the *expected* app name and assume
// eastus; they let `bicep build-params` succeed without env vars set, but
// you must override them before the actual tenant deployment.

using 'main.bicep'

param tenantId             = readEnvironmentVariable('AZURE_TENANT_ID')
param prefix               = readEnvironmentVariable('FTGO_PREFIX',                'ftgo')
param environmentName      = 'ci'
param apiGatewayRedirectUri = readEnvironmentVariable('FTGO_GATEWAY_REDIRECT_URI', 'https://ftgo-ci-apigateway-eus.eastus.azurecontainerapps.io/signin-oidc')
param scalarRedirectUri     = readEnvironmentVariable('FTGO_SCALAR_REDIRECT_URI',  'https://ftgo-ci-apigateway-eus.eastus.azurecontainerapps.io/scalar/v1')
