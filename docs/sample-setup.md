# Sample setup — getting the projects running against a real Entra tenant

This sample is wired to a real Microsoft Entra tenant. For a zero-cost
walkthrough that scripts everything below, see
[`run-locally.md`](./run-locally.md).

The naming follows Chris Richardson's *Microservices Patterns* convention
(service per business capability) using the **FTGO** sample domain — so
auth shapes are taught against recognisable, business-meaningful names.

## Projects

| Project                      | Port | Auth shape demonstrated                                              |
|------------------------------|------|----------------------------------------------------------------------|
| `Ftgo.ApiGateway`            | 7101 | BFF: validates user tokens; calls Orders via OBO + S2S; calls Restaurants via S2S |
| `Ftgo.Orders.Api`            | 7102 | Single-tenant resource: user (`scp=orders.read`) **or** app (`roles=Orders.Process` + `azp` allow-list) |
| `Ftgo.Restaurants.Api`       | 7103 | Multi-tenant resource: app-only (`roles=Restaurants.Read.All`) with tenant allow-list `IssuerValidator` |
| `Ftgo.Kitchen.Worker`        | —    | App token via **Managed Identity** (the one cloud-worker pattern that matters in 2026) |
| `Ftgo.Auth` / `Ftgo.Auth.Client` | — | The one-line `AddEntraAuth(...)` / `AddEntraAuthClient(...)` libraries |

For cert / FIC / client-secret patterns, see [`docs/credential-patterns/`](./credential-patterns/) — documented as references, not deployed.

## App registrations

Create three app registrations per env. Provisioning is declarative via the **Microsoft.Graph Bicep extension** at `infra/bicep/main.bicep`, run through `scripts/provision-apps.sh ENV=dev` — apps, service principals, scopes/roles and admin-consented permissions are created idempotently:

1. **ftgo-dev-bff** (single-tenant) — OIDC web client + OBO. Uses `SignedAssertionFromManagedIdentity` so it has **no stored secret/cert**. Consumes `orders.read` delegated scope.
2. **ftgo-dev-orders-api** (single-tenant) — exposes scope `orders.read` and app role `Orders.Process`.
3. **ftgo-dev-restaurants-api** (multi-tenant, `signInAudience: AzureADMultipleOrgs`) — exposes app role `Restaurants.Read.All`.

`Ftgo.Kitchen.Worker` has **no app registration** — it authenticates as its system-assigned Container App MI, which is granted `Orders.Process` directly via `permission-grants.bicep`.

For each API app reg, set manifest `requestedAccessTokenVersion = 2` so
`aud` is the API's client ID GUID.

## Permissions / role grants

| Caller                                | Callee                  | Permission                                                          |
|---------------------------------------|-------------------------|---------------------------------------------------------------------|
| ftgo-dev-bff (delegated)              | ftgo-dev-orders-api     | scope `orders.read` (admin-consented)                               |
| ftgo-dev-bff (app)                    | ftgo-dev-orders-api     | role `Orders.Process`                                               |
| ftgo-dev-bff (app)                    | ftgo-dev-restaurants-api| role `Restaurants.Read.All` (consented in each provisioned tenant)  |
| Kitchen.Worker container-app MI       | ftgo-dev-orders-api     | role `Orders.Process` (granted by `permission-grants.bicep` `miAppRoleGrant`) |

Set `appRoleAssignmentRequired = true` on the resource APIs so only
allow-listed callers receive `roles`.

## Filling in `appsettings.json`

The committed `appsettings.json` files use placeholder zero-GUIDs so the
repo never carries real identifiers. Replace them in your environment via
`dotnet user-secrets` (per-project) instead of editing the files:

- `src/Ftgo.ApiGateway/appsettings.json` — tenant id, BFF client id, Orders + Restaurants downstream IDs.
- `src/Ftgo.Orders.Api/appsettings.json` — tenant id, Orders API client id, **`AllowedClientApps`** (BFF + Kitchen.Worker MI client id).
- `src/Ftgo.Restaurants.Api/appsettings.json` — Restaurants API client id, **`AllowedTenantIds`**, allowed callers.
- `src/Ftgo.Kitchen.Worker/appsettings.json` — Orders scope (`api://<orders-api-app-id>/.default`). No client id — authenticates as its system-assigned MI.

ApiGateway's outbound credential is configured under
`AzureAd:ClientCredentials` — defaults to **MI**; swap `SourceType` for
`KeyVault` (cert) or `SignedAssertionFromVault` (FIC) as needed.

## Running

```bash
# 1. Restore + build
dotnet build EntraAuthPatterns.slnx

# 2. In separate terminals, start the three APIs:
dotnet run --project src/Ftgo.Orders.Api         # https://localhost:7102
dotnet run --project src/Ftgo.Restaurants.Api    # https://localhost:7103
dotnet run --project src/Ftgo.ApiGateway         # https://localhost:7101

# 3. Exercise flows
#    User → ApiGateway → OBO → Orders API
curl -k -H "Authorization: Bearer <user-token>" https://localhost:7101/api/checkout/via-obo

#    ApiGateway → S2S app token → Orders API
curl -k -H "Authorization: Bearer <user-token>" https://localhost:7101/api/checkout/via-s2s

#    ApiGateway → Restaurants API (multi-tenant)
curl -k -H "Authorization: Bearer <user-token>" https://localhost:7101/api/checkout/via-s2s-multitenant

# 4. Kitchen.Worker authenticates as its Container App MI — only runs in Azure.
#    For local MI experiments, use az login + DefaultAzureCredential (it falls
#    back to AzureCliCredential when MI is absent).
dotnet run --project src/Ftgo.Kitchen.Worker
```

## Negative tests to try

- Hit Orders API user endpoint (`/api/orders/whoami`) with an **app token** → `403` (no `scp`).
- Hit Orders API system endpoint (`/api/orders/system`) with a **user token** → `403` (no `roles`).
- Hit Orders API system endpoint with an app token whose `azp` is **not** in `AllowedClientApps` → `403`.
- Hit Restaurants API with a token from a `tid` **not** in `AllowedTenantIds` → `401`.

## What's deliberately not in the sample

- No local fake / TestKit (per scope decision — see `run-locally.md` for the free real-tenant path).
- No SPA / mobile client. Bring a user token (e.g. Postman + auth-code+PKCE against the BFF app reg).
- No deployed cert / secret / non-MI-FIC workers — those patterns live in [`docs/credential-patterns/`](./credential-patterns/) for reference only.
