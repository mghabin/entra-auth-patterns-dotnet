# Run the sample for free, end-to-end

You only need:

- a **Microsoft work or school Entra tenant** (your existing one is fine,
  or a [free Microsoft 365 Developer tenant](https://developer.microsoft.com/microsoft-365/dev-program) which renews while in use),
- a **GitHub account** (free Actions minutes are unlimited on public repos),
- the .NET 10 SDK pinned by `global.json`,
- `az` CLI, `jq`, `openssl`, `docker`, `gh`.

You do **not** need any paid Azure resources. Every flow has a free
local equivalent (table at the bottom).

## 1. One-time bootstrap

```bash
az login --allow-no-subscriptions --tenant <YOUR_TENANT>
gh auth login        # for `gh secret set` later

# Microsoft.Graph Bicep extension declaratively creates 7 app regs +
# service principals + scopes/roles + admin-consented permissions.
# A wrapper script then adds the federated credential, generates a
# self-signed cert, and hydrates dotnet user-secrets for all 7 projects.
# Idempotent — safe to re-run.
./scripts/deploy.sh   # bash 4+; needs Owner at `/` (see notes below)
```

> **Heads-up — ARM access.** `az deployment tenant create` requires
> Resource Manager RBAC even when deploying only `Microsoft.Graph/*`
> resources. On a fresh dev tenant: portal.azure.com → **Microsoft Entra
> ID → Properties → "Access management for Azure resources" = Yes**, then
> `az role assignment create --assignee-object-id $(az ad signed-in-user
> show --query id -o tsv) --role Owner --scope /`.

## 2. Telemetry — Aspire Dashboard locally (free, OSS)

```bash
docker compose -f tests/local/docker-compose.yml up -d
open http://localhost:18888                     # web UI
# OTLP endpoint the .NET services write to:     http://localhost:4317
```

The .NET services already export OTLP via `AddEntraAuthTelemetry`. Set
`OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317` for each service.

## 3. Build and run

```bash
dotnet build EntraAuthPatterns.slnx

# In separate terminals:
dotnet run --project src/Ftgo.OrderService       # https://localhost:7102
dotnet run --project src/Ftgo.RestaurantService  # https://localhost:7103
dotnet run --project src/Ftgo.ApiGateway        # https://localhost:7101
```

Acquire a user token (e.g. Postman → Authorization Code with PKCE
against the `ftgo-apigateway` app reg, with redirect
`https://localhost:7101/signin-oidc`) and exercise:

```bash
TOKEN=...
curl -k -H "Authorization: Bearer $TOKEN" https://localhost:7101/api/checkout/via-obo
curl -k -H "Authorization: Bearer $TOKEN" https://localhost:7101/api/checkout/via-s2s
curl -k -H "Authorization: Bearer $TOKEN" https://localhost:7101/api/checkout/via-s2s-multitenant
```

## 4. Run the workers (one at a time)

| Worker                       | How to run for free                                                                 |
|------------------------------|--------------------------------------------------------------------------------------|
| **Ftgo.KitchenService** (MI) | Locally `az login` makes `DefaultAzureCredential` work; in production this is MI.   |
| **Ftgo.AccountingService** (cert) | `scripts/new-cert.sh` creates the .pfx and uploads the public key. In prod the worker reads it from Key Vault via managed identity; for local dev set `KeyVault:LocalPfxPath` (and optionally `KeyVault:LocalPfxPassword`) in user-secrets to point at the .pfx — Key Vault is then bypassed entirely. |
| **Ftgo.DeliveryService** (FIC)   | Run the GitHub Actions workflow `.github/workflows/wi-demo.yml` — it logs in via OIDC and acquires a token, no secret. |
| **Ftgo.NotificationService** (secret) | `dotnet user-secrets set` the secret. Included as the **anti-pattern** for contrast — don't adopt this in real systems. |

```bash
dotnet run --project src/Ftgo.KitchenService
dotnet run --project src/Ftgo.AccountingService
dotnet run --project src/Ftgo.NotificationService
gh workflow run wi-demo.yml      # workload identity worker
```

## 5. Production swaps (free → paid, one-line each)

| Local (free)                              | Production swap                                |
|-------------------------------------------|------------------------------------------------|
| `az login` (Azure CLI cred chain)         | Real Managed Identity on App Service / AKS pod |
| Aspire Dashboard via docker-compose       | Application Insights / any OTLP backend        |
| GitHub OIDC federated credential          | AKS workload-identity OIDC                     |
| Self-signed cert in `./.certs/`           | Cert in Azure Key Vault (auto-rotated)         |
| `dotnet user-secrets`                     | Key Vault references / env vars from K8s       |
| GitHub Container Registry (ghcr.io)       | ACR (Premium has geo-replication, content trust) |

The .NET code is **identical** across both columns — only configuration
changes. That's the whole point of `Ftgo.Auth`.
