# Run / develop the sample locally

The sample no longer provisions a separate set of `ftgo-local-*` app
registrations. **Local development uses your own Azure CLI / VS Code
identity** to call the cloud-deployed APIs — exactly the pattern you'd
use in a real project (`DefaultAzureCredential` falls through to
`AzureCliCredential` on a laptop and to `ManagedIdentityCredential` in
Azure, with no code change).

For the full credential demos (cert, FIC, secret, MI), use the cloud
**dev** environment — those flows are realistic only when the workload
runs as a real service principal anyway.

## You need

- a **Microsoft Entra tenant** (your work tenant, or a [free Microsoft 365
  Developer tenant](https://developer.microsoft.com/microsoft-365/dev-program)),
- the .NET 10 SDK (pinned by `global.json`),
- `az`, `gh`, `docker`.

## 1. Sign in

```bash
az login --tenant <YOUR_TENANT>
gh auth login
```

## 2. Build & test

```bash
dotnet build EntraAuthPatterns.slnx
dotnet test  EntraAuthPatterns.slnx
```

Unit tests stub all Entra interactions and run offline.

## 3. Hit the cloud APIs from your laptop

After deploying the cloud **dev** env (see
[`deploy-cloud.md`](deploy-cloud.md)), the script
`scripts/provision-apps.sh ENV=dev` whitelists the well-known **Azure CLI**
(`04b07795-…`) and **VS Code** (`aebc6443-…`) public client IDs as
allowed callers on `ftgo-dev-orderservice` and `ftgo-dev-restaurantservice`.

> **Dev only.** ppe and prod do not whitelist these public clients —
> they only accept tokens from the real workload identities (BFF + workers).
> Laptop-issued tokens are by design a dev-env affordance.

Acquire a user token and call the API:

```bash
ORDERS_APPID=$(az ad app list --filter "displayName eq 'ftgo-dev-orderservice'" --query "[0].appId" -o tsv)
TOKEN=$(az account get-access-token --resource "api://${ORDERS_APPID}" --query accessToken -o tsv)
ORDERS_FQDN=$(az containerapp show -g rg-ftgo-dev-eastus -n ftgo-dev-orderservice-eus --query properties.configuration.ingress.fqdn -o tsv)

curl -H "Authorization: Bearer $TOKEN" "https://${ORDERS_FQDN}/api/orders/system"
```

The same pattern works for `ftgo-dev-restaurantservice`.

## 4. OIDC sign-in (browser flow)

OIDC sign-in needs a confidential client with a registered redirect URI,
which only the deployed BFF has. Test it in the cloud:

```
https://ftgo-dev-apigateway-eus.<random>.eastus.azurecontainerapps.io/scalar/v1
```

## 5. Telemetry — Aspire Dashboard locally (optional)

```bash
docker compose -f tests/local/docker-compose.yml up -d
open http://localhost:18888
# OTLP endpoint: http://localhost:4317
```

Set `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317` if you run a
service locally that you wish to export traces from.

## What about the credential demos (cert / FIC / secret / MI)?

| Worker                         | Where it's real                                                   |
|--------------------------------|-------------------------------------------------------------------|
| **KitchenService** (MI)        | Cloud dev — runs as the Container App's system MI                 |
| **AccountingService** (cert)   | Cloud dev — cert lives in Key Vault, fetched at startup           |
| **DeliveryService** (FIC)      | GitHub Actions workflow `wi-demo.yml` — OIDC token exchange       |
| **NotificationService** (secret) | Cloud dev — included as the **anti-pattern** for contrast       |

All four are wired and run on every cloud deploy. The code is identical
to what you'd ship to production.
