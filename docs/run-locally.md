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
allowed callers on `ftgo-dev-orders-api` and `ftgo-dev-restaurants-api`.

> **Dev only.** ppe and prod do not whitelist these public clients —
> they only accept tokens from the real workload identities (BFF + workers).
> Laptop-issued tokens are by design a dev-env affordance.

> **Why Azure CLI is treated as a public client.** Azure CLI (and VS
> Code) are **public clients** — they ship to every developer machine
> and **cannot hold a secret** (anything compiled in or stored on disk
> would be world-readable on enterprise-managed laptops). Per OAuth 2.1
> and the OIDC Core spec, public clients authenticate the *user*, not
> the client itself, using **PKCE** ([RFC 7636](https://www.rfc-editor.org/rfc/rfc7636))
> instead of a client secret. That is the right model for a CLI on a
> trusted developer workstation; it is the **wrong** model for a cloud
> workload, which has a stable identity and **MUST** use Managed
> Identity / FIC ([`credential-patterns/managed-identity.md`](credential-patterns/managed-identity.md),
> [`credential-patterns/federated-identity.md`](credential-patterns/federated-identity.md)).
> Whitelisting the Azure CLI app id (`04b07795-…`) on a resource
> therefore **MUST** stay scoped to dev — promoting it to prod would
> mean accepting tokens from any signed-in developer's laptop as if
> they were the workload itself. Avoid.

Acquire a user token and call the API:

```bash
ORDERS_APPID=$(az ad app list --filter "displayName eq 'ftgo-dev-orders-api'" --query "[0].appId" -o tsv)
TOKEN=$(az account get-access-token --resource "api://${ORDERS_APPID}" --query accessToken -o tsv)
ORDERS_FQDN=$(az containerapp show -g rg-ftgo-dev-eastus -n ftgo-dev-orders-api-eus --query properties.configuration.ingress.fqdn -o tsv)

curl -H "Authorization: Bearer $TOKEN" "https://${ORDERS_FQDN}/api/orders/system"
```

The same pattern works for `ftgo-dev-restaurants-api`.

## 4. OIDC sign-in (browser flow)

OIDC sign-in needs a confidential client with a registered redirect URI,
which only the deployed BFF has. Test it in the cloud:

```
https://ftgo-dev-apigateway-eus.<random>.eastus.azurecontainerapps.io/scalar/v1
```

## 5. Telemetry — Aspire Dashboard locally (optional)

The Aspire Dashboard via docker-compose is **optional** and exists only
for local debugging — to inspect OTLP traces/metrics/logs without
shipping them to the cloud App Insights workspace. Skip this section
entirely if you don't need local telemetry; nothing else in the sample
depends on it.

```bash
docker compose -f tests/local/docker-compose.yml up -d
open http://localhost:18888
# OTLP endpoint: http://localhost:4317
```

Set `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317` if you run a
service locally that you wish to export traces from.

## What about the credential demos (cert / FIC / secret / MI)?

| Pattern                        | Where it's real                                                   |
|--------------------------------|-------------------------------------------------------------------|
| **MI** (`Ftgo.Kitchen.Worker`) | Cloud dev — runs as the Container App's system MI; calls Orders API with role `Orders.Process` |
| **FIC** (`wi-demo.yml`)        | GitHub Actions workflow — OIDC token exchange, no stored secret   |
| **Cert / secret**              | Documented in [`docs/credential-patterns/`](credential-patterns/) — not deployed (cert is a niche on-prem/HSM tool, secret is an anti-pattern) |

The MI demo runs on every cloud deploy. The FIC demo runs on its own
schedule via `wi-demo.yml`. Both are production-grade.

## Sources

- Sign in with Azure CLI — `az login` — [learn.microsoft.com/cli/azure/authenticate-azure-cli](https://learn.microsoft.com/cli/azure/authenticate-azure-cli)
- `az account get-access-token` — [learn.microsoft.com/cli/azure/account#az-account-get-access-token](https://learn.microsoft.com/cli/azure/account#az-account-get-access-token)
- `DefaultAzureCredential` — credential chain order — [learn.microsoft.com/dotnet/api/azure.identity.defaultazurecredential](https://learn.microsoft.com/dotnet/api/azure.identity.defaultazurecredential)
- Azure.Identity authentication for .NET apps — [learn.microsoft.com/dotnet/azure/sdk/authentication/](https://learn.microsoft.com/dotnet/azure/sdk/authentication/)
- Microsoft identity platform — public vs confidential client applications — [learn.microsoft.com/entra/identity-platform/msal-client-applications](https://learn.microsoft.com/entra/identity-platform/msal-client-applications)
- RFC 7636 — Proof Key for Code Exchange (PKCE) — [rfc-editor.org/rfc/rfc7636](https://www.rfc-editor.org/rfc/rfc7636)
- OpenID Connect Core 1.0 — [openid.net/specs/openid-connect-core-1_0.html](https://openid.net/specs/openid-connect-core-1_0.html)
- .NET Aspire dashboard — [learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/overview](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/overview)
