metadata name = 'container-app'
metadata description = 'Single Azure Container App (Consumption) with system-assigned MI, external HTTP ingress on 8080, /health liveness probe, and 0–3 HTTP-concurrency scaling.'

extension az

@description('Container App resource name (e.g. ftgo-dev-apigateway-eus).')
param appName string

@description('Lowercased short service name (e.g. apigateway, orderservice). Used as the container name and image suffix.')
param serviceNameLower string

@description('Azure region for the app.')
param location string

@description('Resource ID of the Container Apps managed environment.')
param containerAppsEnvironmentId string

@description('Fully-qualified container image (e.g. ghcr.io/mghabin/ftgo-apigateway:sha-abc1234).')
param image string

@description('Application Insights connection string injected as APPLICATIONINSIGHTS_CONNECTION_STRING.')
param appInsightsConnectionString string

@description('Logical environment name (dev/ppe/prod). Capitalized into ASPNETCORE_ENVIRONMENT.')
param environmentName string

@description('Tags applied to the container app.')
param tags object = {}

@description('CPU cores per replica.')
param cpu string = '0.5'

@description('Memory per replica.')
param memory string = '1.0Gi'

var aspNetCoreEnvironment = '${toUpper(substring(environmentName, 0, 1))}${substring(environmentName, 1)}'

resource app 'Microsoft.App/containerApps@2024-10-02-preview' = {
  name:     appName
  location: location
  tags:     tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    environmentId: containerAppsEnvironmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external:    true
        targetPort:  8080
        transport:   'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight:         100
          }
        ]
      }
      registries: []
    }
    template: {
      containers: [
        {
          name:  serviceNameLower
          image: image
          resources: {
            cpu:    json(cpu)
            memory: memory
          }
          env: [
            {
              name:  'ASPNETCORE_ENVIRONMENT'
              value: aspNetCoreEnvironment
            }
            {
              name:  'ASPNETCORE_HTTP_PORTS'
              value: '8080'
            }
            {
              name:  'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsightsConnectionString
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 10
              periodSeconds:       30
              timeoutSeconds:      5
              failureThreshold:    3
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
        rules: [
          {
            name: 'http-concurrency'
            http: {
              metadata: {
                concurrentRequests: '5'
              }
            }
          }
        ]
      }
    }
  }
}

@description('FQDN ACA assigned to the app (e.g. ftgo-dev-apigateway-eus.<random>.eastus.azurecontainerapps.io).')
output fqdn string = app.properties.configuration.ingress.fqdn

@description('System-assigned managed identity principalId — consumed by Key Vault RBAC.')
output principalId string = app.identity.principalId

@description('Container app resource name.')
output name string = app.name
