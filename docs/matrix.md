# Scenario Matrix

One row per realistic backend scenario. "Validation" assumes the receiving API uses Microsoft.Identity.Web defaults from [validation.md](validation.md).

| # | Scenario | Token type | Acquisition lib | Credential | Server validation must check | Common pitfalls |
|---|---|---|---|---|---|---|
| 1 | ASP.NET Core API receives request from SPA / mobile | User | n/a (client acquires) | n/a | `scp`, audience = API client ID (v2) / App ID URI (v1), issuer per tenant | Forgetting `RequiredScope`; treating missing `scp` as allow |
| 2 | Same API calls Microsoft Graph **as the user** | User (OBO) | Microsoft.Identity.Web (`IDownstreamApi`) | API's own: MI / FIC / cert | downstream Graph validates user token | Using app token for user data; missing distributed token cache when scaled out |
| 3 | Worker on App Service calls Graph as itself | App | Azure.Identity (Graph SDK) **or** MSAL.NET | **System or User-assigned MI** | `roles`, `azp`=MI client id, `idtyp=app` | Granting app roles to the wrong SP; not allow-listing `azp` |
| 4 | Worker on App Service calls **your own API** as itself | App | MSAL.NET (`AcquireTokenForClient`) with MI assertion, **or** `ManagedIdentityCredential.GetTokenAsync` | MI | `roles`, `azp` allow-list | Requesting wrong scope (`/.default` vs custom) |
| 5 | Worker on AKS calls a downstream API | App | MSAL.NET with `WithClientAssertion`, or `WorkloadIdentityCredential` | **FIC** (Azure Workload Identity add-on) | same as #4 | Missing federated credential subject; clock skew on projected token |
| 6 | GitHub Actions job calls Entra-protected API | App | MSAL.NET with `WithClientAssertion` using `ACTIONS_ID_TOKEN_REQUEST_*` | **FIC** on app reg, no secret | same as #4 | FIC subject mismatch (`repo:org/repo:ref:refs/heads/main`) |
| 7 | On-prem worker calls Graph | App | MSAL.NET | **Certificate** (KV-backed) → `WithCertificate(cert)` (add `sendX5C: true` only for SNI / x5c cert-rollover) | `roles`, `azp` | Cert in source tree; no rotation; cargo-culting `sendX5C` when not needed |
| 8 | Legacy daemon, MI/FIC not possible | App | MSAL.NET | **Client secret** (KV) | `roles`, `azp` | Long-lived secret; secret in env var on dev box |
| 9 | API in Azure calls Storage / Key Vault / Cosmos | App (Azure resource) | **Azure.Identity** (`DefaultAzureCredential`) on the SDK client | MI in prod, CLI/VS in dev | n/a (Azure RBAC, not your API) | Wrapping `TokenCredential` in your own cache; using account keys |
| 10 | Multi-tenant SaaS API | User and/or App | Microsoft.Identity.Web | API's own: MI / FIC / cert | `IssuerValidator` with **tenant allow-list**; `tid` matches issuer; `roles`/`scp` | `TenantId: "common"` with no tenant filter (open door); admin-consent flow forgotten |
| 11 | API exposed to both users and apps | Both | Microsoft.Identity.Web | — | Branch on `idtyp`/`scp` vs `roles`; separate authorization policies | One `[Authorize]` policy that silently accepts either |
| 12 | Cross-tenant S2S (your app calling a partner tenant) | App | MSAL.NET | **Multi-tenant app reg + cert/FIC** (MI cannot do this) | partner validates `roles` granted in *their* tenant | Trying to use MI; partner hasn't consented your app |

## Quick "which credential" by host

| Host | Default | Fallback |
|---|---|---|
| App Service / Functions / Container Apps | System or User-assigned MI | FIC (rare) |
| AKS | **FIC** via Azure Workload Identity | UAMI assigned to node pool (legacy) |
| VM / VMSS | System-assigned MI | UAMI |
| Azure Arc-enabled server | MI via Arc | Cert |
| GitHub Actions / Azure DevOps | **FIC** (OIDC) | Secret in vault (avoid) |
| Other clouds (EKS/GKE) | **FIC** via OIDC | Cert |
| On-prem / dev box (prod) | Cert in KV | Secret in KV (avoid) |
| Local dev | `DefaultAzureCredential` (CLI/VS) | — |

## "Which library" by call target

| You're calling… | Library |
|---|---|
| Azure resource SDK (Storage, KV, Cosmos, Service Bus, Graph SDK using `TokenCredential`) | **Azure.Identity** |
| Your own / 3rd-party Entra-protected REST API from a worker | **MSAL.NET** |
| Downstream API from inside an ASP.NET Core API (incl. OBO) | **Microsoft.Identity.Web** |
