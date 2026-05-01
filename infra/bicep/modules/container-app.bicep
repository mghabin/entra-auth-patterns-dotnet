metadata name = 'container-app'
metadata description = 'Single Azure Container App (Consumption) with system-assigned MI, external HTTP ingress on 8080, /health/live + /health/ready probes, and 0–3 HTTP-concurrency scaling.'

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
@secure()
param appInsightsConnectionString string

@description('Logical environment name (dev/ppe/prod). Capitalized into ASPNETCORE_ENVIRONMENT.')
param environmentName string

@description('Tags applied to the container app.')
param tags object = {}

@description('CPU cores per replica. Default 0.25 keeps dev cost-bounded; bump to 0.5+ for ppe/prod via parameter override.')
param cpu string = '0.25'

@description('Memory per replica. Default 0.5Gi keeps dev cost-bounded; bump to 1.0Gi+ for ppe/prod via parameter override.')
param memory string = '0.5Gi'

@description('Maximum replicas the HTTP/CPU scaler is allowed to spin up. Default 1 caps dev at a known-tiny worst case (4 apps × 1 replica × 0.25 vCPU ≈ \$16/mo if pinned 24/7); ppe/prod should override.')
@minValue(1)
@maxValue(30)
param maxReplicas int = 1

@description('When false, the app has no public ingress, no HTTP probe, and uses CPU-based scaling. Used for headless worker services.')
param enableIngress bool = true

@description('Per-env env-var overlay appended to the static container env (ASPNETCORE_*, APPINSIGHTS). Empty on cold deploy; populated by provision-apps.sh after Entra app regs are resolved.')
param extraEnvVars array = []

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
    configuration: union({
      activeRevisionsMode: 'Single'
      registries: []
    }, enableIngress ? {
      ingress: {
        external:      true
        targetPort:    8080
        transport:     'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight:         100
          }
        ]
      }
    } : {})
    template: {
      containers: [
        {
          name:  serviceNameLower
          image: image
          resources: {
            cpu:    json(cpu)
            memory: memory
          }
          env: concat([
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
          ], extraEnvVars)
          probes: enableIngress ? [
            {
              type: 'Startup'
              httpGet: {
                path: '/health/startup'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 2
              periodSeconds:       5
              timeoutSeconds:      3
              failureThreshold:    24
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 10
              periodSeconds:       30
              timeoutSeconds:      5
              failureThreshold:    3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 5
              periodSeconds:       10
              timeoutSeconds:      3
              failureThreshold:    3
            }
          ] : []
        }
      ]
      scale: {
        // Workers (no ingress) historically defaulted to min=1 because there's no HTTP
        // scaler to wake them on demand. That keeps a vCPU pinned 24/7 (~$2.4/mo per
        // worker on dev tier) for a probe-once-and-exit pattern. Switching to min=0:
        // the worker runs once on revision creation/update (executes the probe, exits),
        // then stays at 0 replicas until the next deploy. CPU-utilization scaler stays
        // wired so it can scale 0→N if the process ever does sustained work.
        // Honest cost note: README "$0/mo at idle" is now true for workers too; for a
        // fully run-on-demand worker, prefer Microsoft.App/jobs over containerApps.
        minReplicas: 0
        maxReplicas: maxReplicas
        rules: enableIngress ? [
          {
            name: 'http-concurrency'
            http: {
              metadata: {
                concurrentRequests: '5'
              }
            }
          }
        ] : [
          {
            name: 'cpu-load'
            custom: {
              type: 'cpu'
              metadata: {
                type:  'Utilization'
                value: '70'
              }
            }
          }
        ]
      }
    }
  }
}

@description('FQDN ACA assigned to the app, or empty string for headless workers.')
output fqdn string = enableIngress ? app.properties.configuration.ingress.fqdn : ''

@description('System-assigned managed identity principalId — consumed by Key Vault RBAC.')
output principalId string = app.identity.principalId

@description('Container app resource name.')
output name string = app.name
