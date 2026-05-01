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
| 10 | Multi-tenant SaaS API | User and/or App | Microsoft.Identity.Web | API's own: MI / FIC / cert | `IssuerValidator` with **tenant allow-list**; `tid` matches issuer; `roles`/`scp` (see [validation.md §3](validation.md#3-issuer-audience-v1-vs-v2-single-vs-multi-tenant)) | `TenantId: "common"` with no tenant filter (open door); admin-consent flow forgotten |
| 11 | API exposed to both users and apps | Both | Microsoft.Identity.Web | — | **Two separate named policies on two separate `[Authorize]` attributes** — never one OR-claims policy. Per [dotnet-engineering-guide ch02 §10](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz) and [decision-trees.md Tree 4](decision-trees.md#4-does-this-endpoint-accept-delegated-app-only-or-both). | One `[Authorize]` policy that silently accepts either |
| 12 | Cross-tenant S2S (your app calling a partner tenant) | App | MSAL.NET | **Multi-tenant app reg + cert/FIC** (MI cannot do this) | partner validates `roles` granted in *their* tenant | Trying to use MI; partner hasn't consented your app |
| 13 | API receives token from another API (pass-through pattern) | App or User | n/a (token already inbound) | n/a | **Validate, don't cache**: full signature/audience/issuer/`roles`+`azp` (or `scp`) checks per [validation.md](validation.md). The intermediate API **must not** re-mint or cache the inbound token; if it needs to call further downstream as the same user, use **OBO** ([acquisition.md §2b](acquisition.md#2b-calling-a-downstream-api-as-that-user-obo)) — never replay the raw token. | Forwarding the raw inbound token to a downstream API; trusting the upstream's claims without re-validating signature |

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

Decisive picks — these libraries are not interchangeable. Pick by call target, not preference.

| You're calling… | Library | Why |
|---|---|---|
| Azure resource SDK (Storage, KV, Cosmos, Service Bus, Graph SDK using `TokenCredential`) | **Azure.Identity** | Designed for `TokenCredential`-aware Azure SDKs; integrates with MI / `DefaultAzureCredential` chain. |
| Your own / 3rd-party Entra-protected REST API from a worker | **MSAL.NET** | Arbitrary OAuth flows incl. `client_credentials`, `WithClientAssertion` (FIC), and OBO. Not bound to ASP.NET Core. |
| Downstream API from inside an ASP.NET Core API (incl. OBO) | **Microsoft.Identity.Web** | Wraps MSAL.NET + JwtBearer wiring; `ITokenAcquisition` / `IDownstreamApi` / distributed token cache integration. |

If you find yourself using MSAL.NET to call Azure Storage, or `Azure.Identity` to do OBO, you've picked the wrong library — re-derive from the table.

---

## Sources

- Microsoft identity platform overview — [learn.microsoft.com/entra/identity-platform/v2-overview](https://learn.microsoft.com/entra/identity-platform/v2-overview)
- On-Behalf-Of flow — [learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow](https://learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow)
- Managed identities for Azure resources — [learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview](https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview)
- Workload identity federation — [learn.microsoft.com/entra/workload-id/workload-identity-federation](https://learn.microsoft.com/entra/workload-id/workload-identity-federation)
- Azure Workload Identity (AKS) — [learn.microsoft.com/azure/aks/workload-identity-overview](https://learn.microsoft.com/azure/aks/workload-identity-overview)
- Microsoft.Identity.Web — [github.com/AzureAD/microsoft-identity-web](https://github.com/AzureAD/microsoft-identity-web)
- MSAL.NET — [github.com/AzureAD/microsoft-authentication-library-for-dotnet](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet)
- Azure.Identity for .NET — [github.com/Azure/azure-sdk-for-net/tree/main/sdk/identity/Azure.Identity](https://github.com/Azure/azure-sdk-for-net/tree/main/sdk/identity/Azure.Identity)
- dotnet-engineering-guide ch02 §10 — auth doctrine — [github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz)
- infra-engineering-guide ch06 §3 — workload identity — [github.com/mghabin/infra-engineering-guide/blob/main/docs/06-security-supply-chain.md#3-workload-identity--the-no-static-credentials-rule](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/06-security-supply-chain.md#3-workload-identity--the-no-static-credentials-rule)
- RFC 6749 — OAuth 2.0 — [rfc-editor.org/rfc/rfc6749](https://www.rfc-editor.org/rfc/rfc6749)
- RFC 8693 — OAuth 2.0 Token Exchange — [rfc-editor.org/rfc/rfc8693](https://www.rfc-editor.org/rfc/rfc8693)
