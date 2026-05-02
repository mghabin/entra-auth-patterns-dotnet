# Token Acquisition

Two flows you ever acquire on the server:

| Flow           | Who is the token *for*   | Claim shape                                                                                                                                                                                                                                                          | OAuth grant                                                  |
| -------------- | ------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------ |
| **App token**  | The calling app/workload | `roles` *(when app roles are assigned/required)*, no `scp`; `idtyp=app` may not be present (defense-in-depth only — rely on `roles` + `azp` allow-list for enforcement, see [validation.md §4 App-token specific checks](validation.md#4-app-token-specific-checks)) | `client_credentials` (or FIC assertion)                      |
| **User token** | The signed-in user       | `scp` (delegated scopes); identity in `oid` + `tid` (use these for decisions); `name` / `preferred_username` for display                                                                                                                                             | `authorization_code` (client) → API → **OBO** for downstream |

> Rule of thumb: if there is no human in the request, you want an **app token**. If there is, propagate the user identity via **OBO**, don't fall back to an app token.

---

## 1. App tokens (S2S / daemons / workers)

### 1a. Managed Identity — preferred when in Azure

Works on App Service, Functions, Container Apps, AKS (via workload identity), VMs, Arc.

**Calling Azure resources** (Storage, Key Vault, Service Bus, Graph SDK, …) — use `Azure.Identity`:

```csharp
// System-assigned MI
var cred = new DefaultAzureCredential();           // dev → CLI/VS, prod → MI
// or be explicit:
var cred = new ManagedIdentityCredential();

// User-assigned MI
var cred = new ManagedIdentityCredential(
    ManagedIdentityId.FromUserAssignedClientId("<client-id>"));

var blob = new BlobServiceClient(new Uri("https://<storage>.blob.core.windows.net"), cred);
```

**Calling your own protected Web API with MI** — you need an actual JWT, two options:

```csharp
// (a) Azure.Identity — you control the scope ("<api-app-id-uri>/.default")
var token = await new ManagedIdentityCredential()
    .GetTokenAsync(new TokenRequestContext(new[] { "api://<downstream>/.default" }));

// (b) Microsoft.Identity.Web (in ASP.NET Core) - via ITokenAcquisition with MI cred
//     Configure "AzureAd:ClientCredentials" with "SourceType":"SignedAssertionFromManagedIdentity".
```

Notes:
- MI is **system-assigned** (lifecycle tied to resource) or **user-assigned** (portable; preferred for shared infra and blue/green).
- MI tokens are issued by `https://login.microsoftonline.com/<tenant>/` with `appid` of the MI's service principal — see [validation](validation.md#5-mi-tokens).
- MI cannot do OBO. MI cannot represent a user.

### 1b. Workload Identity Federation (FIC) — preferred outside Azure

Use when running in GitHub Actions, AKS with workload identity, EKS/GKE, on-prem k8s — anywhere with an OIDC issuer. Configure a **federated credential** on the app registration; no secret stored.

```csharp
// Generic: get an external OIDC token, exchange via MSAL using a signed assertion
var app = ConfidentialClientApplicationBuilder
    .Create(clientId)
    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
    .WithClientAssertion(async _ => await GetExternalOidcTokenAsync()) // e.g. AKS projected token, GH OIDC
    .Build();

var result = await app.AcquireTokenForClient(new[] { "api://<downstream>/.default" })
                      .ExecuteAsync();
```

In AKS with the **Azure Workload Identity** add-on, `DefaultAzureCredential` / `WorkloadIdentityCredential` does this transparently:

```csharp
var cred = new WorkloadIdentityCredential(); // reads AZURE_*_FILE env vars
```

### 1c. Certificate on an app registration — acceptable when MI/FIC unavailable

Cert lives in Key Vault or the OS cert store; never on disk in plaintext.

```csharp
var cert = new X509Certificate2(/* from KV or store */);

var app = ConfidentialClientApplicationBuilder
    .Create(clientId)
    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
    .WithCertificate(cert)                  // add sendX5C: true only for SNI / x5c cert-rollover scenarios
    .Build();

var result = await app.AcquireTokenForClient(new[] { "https://graph.microsoft.com/.default" })
                      .ExecuteAsync();
```

### 1d. Client secret — last resort

Same as cert but `.WithClientSecret("…")`. Rotate ≤ 6 months, store only in Key Vault. Avoid in production.

### 1e. Picking the library

| You are doing…                                                       | Use                                                               |
| -------------------------------------------------------------------- | ----------------------------------------------------------------- |
| Calling an **Azure resource SDK**                                    | `Azure.Identity` (`TokenCredential`)                              |
| Calling **your own / a 3rd-party Entra-protected API** from a worker | `MSAL.NET` (`ConfidentialClientApplication`)                      |
| Calling a downstream API **from inside an ASP.NET Core API**         | `Microsoft.Identity.Web` → `ITokenAcquisition` / `IDownstreamApi` |

---

## 2. User tokens (delegated)

### 2a. Validating the inbound user token

The web client (SPA / native) does auth-code + PKCE and calls your API with a Bearer token. Your API just **validates** — see [validation](validation.md). You do not acquire a user token on the server.

### 2b. Calling a downstream API as that user (OBO)

```csharp
// Program.cs (ASP.NET Core 10, Microsoft.Identity.Web)
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))
        .EnableTokenAcquisitionToCallDownstreamApi()
        .AddDownstreamApi("Graph", builder.Configuration.GetSection("Graph"))
        .AddInMemoryTokenCaches(); // or AddDistributedTokenCaches for multi-instance
```

```csharp
public class MyController(IDownstreamApi downstream) : ControllerBase
{
    [HttpGet("me")]
    [RequiredScope("access_as_user")]
    public Task<HttpResponseMessage> Me() =>
        downstream.CallApiForUserAsync("Graph", o => o.RelativePath = "me");
}
```

What this does: server takes the inbound user token and calls `/oauth2/v2.0/token` with the OBO parameters — `grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer`, `requested_token_use=on_behalf_of`, and `assertion=<incoming access token>` — plus the API's own client credentials (MI/FIC/cert/secret). It gets back a new user token for the downstream resource; the downstream API still sees a *user* token (with `scp`, user `oid`, etc.).

The credential the API uses to authenticate **itself** to the token endpoint during OBO can be **any** of the credentials in §1 (MI, FIC, cert, secret). **Prefer MI when the API runs on Azure compute; FIC when it runs on a non-Azure platform with an OIDC issuer (GitHub Actions, AKS workload identity, EKS/GKE, on-prem k8s).** Cert is acceptable only when neither MI nor FIC is available; secret only as a documented, time-boxed exception. Configure under `AzureAd:ClientCredentials`:

```jsonc
"AzureAd": {
  "Instance":  "https://login.microsoftonline.com/",
  "TenantId":  "organizations",           // multi-tenant Entra-only; use a specific tenant GUID for single-tenant. Avoid "common" unless you intentionally accept MSA.
  "ClientId":  "<api-app-id>",
  "ClientCredentials": [
    { "SourceType": "SignedAssertionFromManagedIdentity",
      "ManagedIdentityClientId": "<uami-client-id>" }
    // alternates:
    // { "SourceType": "KeyVault", "KeyVaultUrl": "...", "KeyVaultCertificateName": "..." }
    // { "SourceType": "SignedAssertionFromVault" }   // FIC via KV
    // { "SourceType": "ClientSecret", "ClientSecret": "..." } // avoid
  ]
}
```

> Prefer **MI or FIC** as the API's own credential, even when its job is OBO.

---

## 3. Single-tenant vs multi-tenant — at acquisition time

|                         | Single-tenant                                  | Multi-tenant                                                                                                                                                                                                                                                                 |
| ----------------------- | ---------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Authority               | `https://login.microsoftonline.com/<tenantId>` | `https://login.microsoftonline.com/organizations` (work/school — default for Entra-only APIs). Only use `/common` (work + MSA) if you truly accept personal accounts and filter explicitly.                                                                                  |
| App registration        | `signInAudience: AzureADMyOrg`                 | `AzureADMultipleOrgs` (or `…AndPersonalMicrosoftAccount`)                                                                                                                                                                                                                    |
| Admin consent           | Once, in your tenant                           | Per-tenant; expose via admin-consent URL                                                                                                                                                                                                                                     |
| App token (`/.default`) | Issued by your tenant                          | Issued by the **calling tenant** — the app must be provisioned there                                                                                                                                                                                                         |
| OBO                     | Same-tenant                                    | Token is issued in the **user's** home tenant; downstream must accept that issuer. The downstream API **must** enforce a `tid` allow-list via `IssuerValidator` — see [validation.md §3 Issuer & audience](validation.md#3-issuer-audience-v1-vs-v2-single-vs-multi-tenant). |
| MI / FIC                | Same                                           | MI is per-resource in your tenant — to call into other tenants you need a **multi-tenant app reg** + cert/FIC, not raw MI                                                                                                                                                    |

Key takeaway: **MI is single-tenant by nature.** For cross-tenant S2S, use a multi-tenant app registration with a cert or FIC, or use MI to call your own multi-tenant app reg's federated credential.

---

## 4. Token caching — don't get this wrong

App-token caching (S2S / `client_credentials`) and user-token caching (OBO) have different correctness requirements. Treat them separately.

### 4a. App tokens

- MSAL caches app tokens per `(authority, scope)` automatically — you **must** reuse a single `IConfidentialClientApplication` instance (singleton) for caching to take effect.
- `Azure.Identity` `TokenCredential` already caches internally; **do not** wrap it in your own cache. Reuse a single credential instance per process.
- In-memory cache is sufficient for app tokens because the cache key is `(authority, scope)` — no per-user partitioning, identical across instances.

### 4b. User tokens (OBO)

- The OBO cache is keyed by the inbound user's identity, so each instance would otherwise re-acquire on first hit. **Distributed cache is mandatory for multi-instance deployments**: `AddDistributedTokenCaches()` backed by Redis or SQL.
- `AddInMemoryTokenCaches()` is acceptable **only** for single-instance or local dev.
- An undersized / misconfigured distributed cache will silently fall back to per-instance acquisition; alert on a sudden drop in cache hit-rate. The downstream API still enforces validation per [validation.md](validation.md) — caching does not skip validation.

### 4c. Universal rules

- Never cache raw tokens yourself outside MSAL / `TokenCredential`.
- Never log raw tokens. Log only the claims you used to authorize (`oid`, `tid`, `azp`, `roles`, `scp`).

---

## Sources

- Microsoft identity platform overview — [learn.microsoft.com/entra/identity-platform/v2-overview](https://learn.microsoft.com/entra/identity-platform/v2-overview)
- On-Behalf-Of flow — [learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow](https://learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow)
- Client credentials flow — [learn.microsoft.com/entra/identity-platform/v2-oauth2-client-creds-grant-flow](https://learn.microsoft.com/entra/identity-platform/v2-oauth2-client-creds-grant-flow)
- Managed identities for Azure resources — [learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview](https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview)
- Workload identity federation — [learn.microsoft.com/entra/workload-id/workload-identity-federation](https://learn.microsoft.com/entra/workload-id/workload-identity-federation)
- Microsoft.Identity.Web — [github.com/AzureAD/microsoft-identity-web](https://github.com/AzureAD/microsoft-identity-web)
- Azure.Identity for .NET — [github.com/Azure/azure-sdk-for-net/tree/main/sdk/identity/Azure.Identity](https://github.com/Azure/azure-sdk-for-net/tree/main/sdk/identity/Azure.Identity)
- MSAL.NET — [github.com/AzureAD/microsoft-authentication-library-for-dotnet](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet)
- Token caching in MSAL.NET — [learn.microsoft.com/entra/msal/dotnet/how-to/token-cache-serialization](https://learn.microsoft.com/entra/msal/dotnet/how-to/token-cache-serialization)
- RFC 6749 — The OAuth 2.0 Authorization Framework — [rfc-editor.org/rfc/rfc6749](https://www.rfc-editor.org/rfc/rfc6749)
- RFC 8693 — OAuth 2.0 Token Exchange — [rfc-editor.org/rfc/rfc8693](https://www.rfc-editor.org/rfc/rfc8693)
- dotnet-engineering-guide ch02 §10 — auth doctrine — [github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz)
