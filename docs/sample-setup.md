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
| `Ftgo.OrderService`          | 7102 | Single-tenant resource: user (`scp=orders.read`) **or** app (`roles=Orders.Process` + `azp` allow-list) |
| `Ftgo.RestaurantService`     | 7103 | Multi-tenant resource: app-only (`roles=Restaurants.Read.All`) with tenant allow-list `IssuerValidator` |
| `Ftgo.KitchenService`        | —    | App token via **Managed Identity**                                   |
| `Ftgo.AccountingService`     | —    | App token via **Certificate** (Key Vault-backed)                     |
| `Ftgo.DeliveryService`       | —    | App token via **Workload Identity Federation (FIC)**                 |
| `Ftgo.NotificationService`   | —    | App token via **Client Secret** (⚠ anti-pattern, included for contrast) |
| `Ftgo.Auth`                  | —    | The one-line `AddEntraAuth(...)` bootstrap library                   |

## App registrations

Create seven app registrations (the `scripts/setup-entra.{ps1,sh}` helpers
do this idempotently — see `run-locally.md`):

1. **ftgo-apigateway** (single-tenant) — exposes scope `orders.read`.
2. **ftgo-orderservice** (single-tenant) — exposes scope `orders.read` and app role `Orders.Process`.
3. **ftgo-restaurantservice** (multi-tenant, `signInAudience: AzureADMultipleOrgs`) — exposes app role `Restaurants.Read.All`.
4. **ftgo-kitchenservice** (single-tenant, optional) — only needed if running outside Azure; in Azure the identity is a **Managed Identity**.
5. **ftgo-accountingservice** (single-tenant) — has a **certificate** credential whose private key lives in Key Vault.
6. **ftgo-deliveryservice** (single-tenant) — has a **federated identity credential** (GitHub Actions / AKS / etc.).
7. **ftgo-notificationservice** (single-tenant) — has a **client secret** (anti-pattern; rotate ≤ 6 months).

For each API app reg, set manifest `requestedAccessTokenVersion = 2` so
`aud` is the API's client ID GUID.

## Permissions / role grants

| Caller                                | Callee                  | Permission                                                          |
|---------------------------------------|-------------------------|---------------------------------------------------------------------|
| ftgo-apigateway (delegated)           | ftgo-orderservice       | scope `orders.read` (admin-consented)                               |
| ftgo-apigateway (app)                 | ftgo-orderservice       | role `Orders.Process`                                               |
| ftgo-apigateway (app)                 | ftgo-restaurantservice  | role `Restaurants.Read.All` (consented in each provisioned tenant)  |
| ftgo-kitchenservice MI                | ftgo-orderservice       | role `Orders.Process` (assign with `New-MgServicePrincipalAppRoleAssignment`) |
| ftgo-accountingservice                | ftgo-orderservice       | role `Orders.Process`                                               |
| ftgo-deliveryservice                  | ftgo-restaurantservice  | role `Restaurants.Read.All`                                         |
| ftgo-notificationservice              | ftgo-orderservice       | role `Orders.Process`                                               |

Set `appRoleAssignmentRequired = true` on the resource APIs so only
allow-listed callers receive `roles`.

## Filling in `appsettings.json`

The committed `appsettings.json` files use placeholder zero-GUIDs so the
repo never carries real identifiers. Replace them in your environment via
`dotnet user-secrets` (per-project) instead of editing the files:

- `src/Ftgo.ApiGateway/appsettings.json` — tenant id, ApiGateway client id, Orders + Restaurants downstream IDs.
- `src/Ftgo.OrderService/appsettings.json` — tenant id, OrderService client id, **`AllowedClientApps`** (ApiGateway + every worker app id; for KitchenService use the **MI's client id**).
- `src/Ftgo.RestaurantService/appsettings.json` — RestaurantService client id, **`AllowedTenantIds`**, allowed callers.
- `src/Ftgo.KitchenService/appsettings.json` — Orders scope (`api://<orderservice-app-id>/.default`); optional UAMI client id.
- `src/Ftgo.AccountingService/appsettings.json` — tenant id, AccountingService client id, KV uri + cert name, Orders scope.
- `src/Ftgo.DeliveryService/appsettings.json` — tenant id, DeliveryService client id, Restaurants scope.
- `src/Ftgo.NotificationService/appsettings.json` — tenant id, NotificationService client id, Orders scope. Secret comes from env var **`FTGO_NOTIFICATIONSERVICE_CLIENT_SECRET`** (KV-injected), never from the file.

ApiGateway's outbound credential is configured under
`AzureAd:ClientCredentials` — defaults to **MI**; swap `SourceType` for
`KeyVault` (cert) or `SignedAssertionFromVault` (FIC) as needed.

## Running

```bash
# 1. Restore + build
dotnet build EntraAuthPatterns.slnx

# 2. In separate terminals, start the three APIs:
dotnet run --project src/Ftgo.OrderService       # https://localhost:7102
dotnet run --project src/Ftgo.RestaurantService  # https://localhost:7103
dotnet run --project src/Ftgo.ApiGateway         # https://localhost:7101

# 3. Exercise flows
#    User → ApiGateway → OBO → OrderService
curl -k -H "Authorization: Bearer <user-token>" https://localhost:7101/api/checkout/via-obo

#    ApiGateway → S2S app token → OrderService
curl -k -H "Authorization: Bearer <user-token>" https://localhost:7101/api/checkout/via-s2s

#    ApiGateway → RestaurantService (multi-tenant)
curl -k -H "Authorization: Bearer <user-token>" https://localhost:7101/api/checkout/via-s2s-multitenant

# 4. Run any worker (must run where its credential is available — Azure for KitchenService MI,
#    AKS / GitHub Actions for DeliveryService federation, etc.)
dotnet run --project src/Ftgo.KitchenService
dotnet run --project src/Ftgo.AccountingService
dotnet run --project src/Ftgo.DeliveryService
dotnet run --project src/Ftgo.NotificationService
```

## Negative tests to try

- Hit OrderService user endpoint (`/api/orders/whoami`) with an **app token** → `403` (no `scp`).
- Hit OrderService system endpoint (`/api/orders/system`) with a **user token** → `403` (no `roles`).
- Hit OrderService system endpoint with an app token whose `azp` is **not** in `AllowedClientApps` → `403`.
- Hit RestaurantService with a token from a `tid` **not** in `AllowedTenantIds` → `401`.

## What's deliberately not in the sample

- No local fake / TestKit (per scope decision — see `run-locally.md` for the free real-tenant path).
- No SPA / mobile client. Bring a user token (e.g. Postman + auth-code+PKCE against the ApiGateway app reg).
- NotificationService (secret) is included **only** to show the contrast; do not adopt this credential type.
