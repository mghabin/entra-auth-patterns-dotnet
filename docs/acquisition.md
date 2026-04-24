# Token Acquisition

Two flows you ever acquire on the server:

| Flow | Who is the token *for* | Claim shape | OAuth grant |
|---|---|---|---|
| **App token** | The calling app/workload | `roles` *(when app roles are assigned/required)*, no `scp`; `idtyp=app` may be present (optional, defense-in-depth) | `client_credentials` (or FIC assertion) |
| **User token** | The signed-in user | `scp` (delegated scopes); identity in `oid` + `tid` (use these for decisions); `name` / `preferred_username` for display | `authorization_code` (client) → API → **OBO** for downstream |

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

var blob = new BlobServiceClient(new Uri("https://acct.blob.core.windows.net"), cred);
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
- MI tokens are issued by `https://login.microsoftonline.com/<tenant>/` with `appid` of the MI's service principal — see [validation](validation.md#mi-tokens).
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

| You are doing… | Use |
|---|---|
| Calling an **Azure resource SDK** | `Azure.Identity` (`TokenCredential`) |
| Calling **your own / a 3rd-party Entra-protected API** from a worker | `MSAL.NET` (`ConfidentialClientApplication`) |
| Calling a downstream API **from inside an ASP.NET Core API** | `Microsoft.Identity.Web` → `ITokenAcquisition` / `IDownstreamApi` |

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

The credential the API uses to authenticate **itself** to the token endpoint during OBO can be **any** of the credentials in §1 (MI, FIC, cert, secret). Configure under `AzureAd:ClientCredentials`:

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

| | Single-tenant | Multi-tenant |
|---|---|---|
| Authority | `https://login.microsoftonline.com/<tenantId>` | `https://login.microsoftonline.com/organizations` (work/school — default for Entra-only APIs). Only use `/common` (work + MSA) if you truly accept personal accounts and filter explicitly. |
| App registration | `signInAudience: AzureADMyOrg` | `AzureADMultipleOrgs` (or `…AndPersonalMicrosoftAccount`) |
| Admin consent | Once, in your tenant | Per-tenant; expose via admin-consent URL |
| App token (`/.default`) | Issued by your tenant | Issued by the **calling tenant** — the app must be provisioned there |
| OBO | Same-tenant | Token is issued in the **user's** home tenant; downstream must accept that issuer |
| MI / FIC | Same | MI is per-resource in your tenant — to call into other tenants you need a **multi-tenant app reg** + cert/FIC, not raw MI |

Key takeaway: **MI is single-tenant by nature.** For cross-tenant S2S, use a multi-tenant app registration with a cert or FIC, or use MI to call your own multi-tenant app reg's federated credential.

---

## 4. Token caching — don't get this wrong

- **ASP.NET Core API**: `AddDistributedTokenCaches()` backed by Redis/SQL when running multi-instance. `AddInMemoryTokenCaches()` is fine for single-instance/dev.
- **Worker**: MSAL's in-memory cache per `IConfidentialClientApplication` instance is fine; **reuse the instance** (singleton). For app tokens, MSAL caches per `(authority, scope)` automatically.
- **Azure.Identity**: `TokenCredential` already caches; do **not** wrap in your own cache. Reuse a single credential instance.
- Never cache raw tokens yourself. Never log them.
