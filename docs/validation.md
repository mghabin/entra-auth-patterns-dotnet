# Token Validation (server-side)

Validation is **independent of how the token was acquired**. Your API just sees a JWT and decides:
1. Is the signature valid (signed by Entra for the expected tenant)?
2. Is the **audience** *me*?
3. Is the **issuer** one I trust?
4. Does it carry the **permission** required for this endpoint (`scp` for users, `roles` for apps)?

---

## 1. Baseline — ASP.NET Core 10 + Microsoft.Identity.Web

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
```

```jsonc
"AzureAd": {
  "Instance":  "https://login.microsoftonline.com/",
  "TenantId":  "<tenant-guid>",          // single-tenant; for SaaS use "organizations" (avoid "common" unless MSA intended)
  "ClientId":  "<api-app-id>",            // for v2 tokens this client ID is the default expected audience
  "Audience":  "<api-app-id>"             // override only if you intentionally accept a different aud (e.g. App ID URI for v1, or both during migration)
}
```

Microsoft.Identity.Web wires up `JwtBearerOptions` with the right `TokenValidationParameters` (issuer, audience, signing keys via OIDC metadata, clock skew), so you usually don't touch them. Override only when needed:

```csharp
builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
{
    o.TokenValidationParameters.ValidAudiences = new[]
    {
        "<api-app-id-guid>",          // v2 default: the API's client ID
        "api://<api-app-id-uri>"      // v1 may use the App ID URI; include during v1↔v2 migration
    };
});
```

---

## 2. Authorization — `scp` vs `roles`

| Token type | Claim | Source |
|---|---|---|
| User (delegated) | `scp` (space-separated) | Scopes the user consented to |
| App (S2S) | `roles` (array) | App roles you defined on the API and granted to the client app |

Endpoint policy:

```csharp
[Authorize]
[RequiredScope("Files.Read")]              // user token must carry scp=Files.Read
public IActionResult ReadAsUser() => …

[Authorize(Roles = "Tasks.Process.All")]   // app token must carry that role
public IActionResult ProcessAsApp() => …
```

Distinguish them in code (when an endpoint accepts both):

```csharp
var idtyp = User.FindFirst("idtyp")?.Value;     // "app" for S2S, absent for user
var hasScp   = User.HasClaim(c => c.Type == "scp");
var hasRoles = User.HasClaim(c => c.Type == "roles");
```

> **Never** treat the absence of `scp` as "no auth required". An app token has no `scp` by design — gate on `roles` explicitly.

---

## 3. Issuer & audience — `v1` vs `v2`, single vs multi-tenant

### Token versions
- **v1** (`iss = https://sts.windows.net/<tid>/`): `aud` is the API's **client ID** *or* its resource URI (`api://…`); `ver: "1.0"`.
- **v2** (`iss = https://login.microsoftonline.com/<tid>/v2.0`): `aud` is the API's **client ID** (GUID); `ver: "2.0"`.

The token version is controlled by the **resource API's** app manifest field **`requestedAccessTokenVersion`** (`null`/`1` ⇒ v1, `2` ⇒ v2). Pick one and stick with it. During migration, accept **both** issuers and **both** audiences. Microsoft.Identity.Web's issuer validator handles v1 (`sts.windows.net`) and v2 (`login.microsoftonline.com/.../v2.0`) for a configured tenant when set up correctly.

### Single-tenant
```jsonc
"TenantId": "<tenant-guid>"
// Microsoft.Identity.Web validates issuer against this tenant for both v1 (sts.windows.net/<tid>/) and v2 (login.microsoftonline.com/<tid>/v2.0).
```

### Multi-tenant
```jsonc
"TenantId": "organizations"   // work/school accounts — default for Entra-only APIs
// "common" only if you intentionally accept personal Microsoft accounts and have explicit tenant/account filtering.
```

Microsoft.Identity.Web replaces the simple `ValidIssuer` check with `IssuerValidator` that:
- Accepts `https://login.microsoftonline.com/{tenantId}/v2.0` for **any** tenant.
- Verifies that `tid` claim matches the issuer's tenant segment.

You still need to decide **which tenants you allow**. For SaaS, store the list of customer tenants and reject others:

```csharp
o.TokenValidationParameters.IssuerValidator = (issuer, token, parameters) =>
{
    var tid = ((JsonWebToken)token).GetPayloadValue<string>("tid");
    if (!_allowedTenants.Contains(tid))
        throw new SecurityTokenInvalidIssuerException("Tenant not provisioned.");
    return issuer;
};
```

---

## 4. App-token specific checks

Beyond `roles`, apply:

- **`azp` / `appid` allow-list** (primary defense) — the calling client's app ID. Maintain an allow-list of client app IDs that may call this endpoint. Don't rely solely on roles if the role is broad.
- **No `scp` claim** — defense in depth (a true app token never has `scp`).
- **`idtyp == "app"`** — *optional* additional signal; the claim is not always emitted, so don't rely on it as a required check.

Note on `roles`: an app-only token only contains `roles` if you've defined app roles on the API and granted them to the calling SP (and `appRoleAssignmentRequired = true` is recommended). Don't assume `roles` will be present just because the token is app-only.

```csharp
var appId = User.FindFirst("azp")?.Value      // v2
         ?? User.FindFirst("appid")?.Value;   // v1
if (!_allowedClientApps.Contains(appId)) return Forbid();
```

---

## 5. MI tokens

A token acquired via Managed Identity is just a normal Entra app token from the MI's service principal. Server-side validation is identical to §1–§4:
- `iss` = `https://login.microsoftonline.com/<your-tenant>/v2.0` (or v1 sts.windows.net)
- `aud` = whatever resource you requested (your API's App ID URI, `https://graph.microsoft.com`, etc.)
- `appid` / `azp` = the **MI's** client ID — allow-list it.
- `roles` = whatever app roles you granted to the MI's SP (use Graph PowerShell / `New-MgServicePrincipalAppRoleAssignment`).

There is nothing magical to validate about MI tokens; treat them like any S2S caller.

---

## 6. Cross-cutting: keys, clock, CAE, ACRS

- **Signing keys**: pulled from `https://login.microsoftonline.com/<tenant>/v2.0/.well-known/openid-configuration` and rotated automatically. Do not pin keys.
- **Clock skew**: default 5 min — leave it.
- **CAE (Continuous Access Evaluation)**: opt-in, lets Entra invalidate tokens early on conditional-access events. Microsoft.Identity.Web supports it via `WithClientCapabilities(["cp1"])` on the client and by surfacing `WWW-Authenticate: Bearer error="insufficient_claims"` from the API. Honor it.
- **ACRS / claims challenges**: for step-up auth (e.g., MFA required for a sensitive endpoint), the API must return **401** with a `WWW-Authenticate: Bearer error="insufficient_claims", claims="<base64url-json>"` header so the client re-acquires a token satisfying the policy. Use Microsoft.Identity.Web helpers — e.g. `HttpContext.GetTokenAcquirer().ReplyForbiddenWithWwwAuthenticateHeaderAsync(...)` or build the header via `WwwAuthenticateParameters` — rather than throwing a bare exception (which won't include the `claims` parameter).
- **Multi-audience APIs**: list every accepted audience in `ValidAudiences`. Never disable audience validation.
- **Don't** disable `ValidateIssuer`, `ValidateAudience`, or `ValidateLifetime`. Ever.

---

## 7. Quick checklist per endpoint

- [ ] `[Authorize]` present.
- [ ] `[RequiredScope]` (user) or `[Authorize(Roles=…)]` (app) — not both implicit.
- [ ] If endpoint accepts both: explicit branching on `idtyp`/`scp`/`roles`.
- [ ] App-only endpoints additionally allow-list `azp`/`appid`.
- [ ] Multi-tenant: tenant allow-list enforced in `IssuerValidator`.
- [ ] Audience set explicitly to App ID URI (and GUID during v1↔v2 migration).
- [ ] CAE enabled if calling Entra-protected downstream APIs.
