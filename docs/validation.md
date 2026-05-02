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
        "<api-app-id-guid>",          // v2 default: the API's client ID (GUID)
        "api://<api-app-id-uri>"      // v1 default: the App ID URI — include both during v1↔v2 migration
    };
});
```

---

## 2. Authorization — `scp` vs `roles`

| Token type       | Claim                   | Source                                                         |
| ---------------- | ----------------------- | -------------------------------------------------------------- |
| User (delegated) | `scp` (space-separated) | Scopes the user consented to                                   |
| App (S2S)        | `roles` (array)         | App roles you defined on the API and granted to the client app |

Endpoint policy (FTGO sample):

```csharp
[Authorize]
[RequiredScope("orders.read")]                                 // user token must carry scp=orders.read
public IActionResult ReadAsUser() => …

[Authorize(Policy = OrdersAuthorizationPolicies.App)]          // app token: roles=Orders.Process AND azp ∈ allow-list
public IActionResult ProcessAsApp() => …
```

> **Never** `[Authorize(Roles = "...")]` on a Microsoft.Identity.Web JWT scheme. Those schemes set `MapInboundClaims = false` (defense-in-depth), so the framework's role check looks for `ClaimTypes.Role` while Entra emits the role claim under the short name `roles`. The check silently fails. Always use a named policy via `AddAppPolicy(...)` — see [DOCTRINE.md § "Authorization-policy doctrine"](../DOCTRINE.md#authorization-policy-doctrine-canonical) for the canonical statement.

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

You still need to decide **which tenants you allow**. For SaaS, store the list of customer tenants and reject others. The rejection contract is explicit: if `tid` is not in the allow-list (or doesn't match the issuer's tenant segment), throw `SecurityTokenInvalidIssuerException` — Microsoft.Identity.Web translates this to **HTTP 401 Unauthorized** (not 403; 403 is reserved for "authenticated but lacks permission"):

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

Beyond the standard signature/audience/issuer/lifetime checks, an app-only endpoint enforces authorization in two layers:

- **`roles` (PRIMARY — RBAC)** — the app role(s) you defined on the API and granted to the calling SP. This is the authorization decision. Configure `appRoleAssignmentRequired = true` on the API's enterprise app so Entra refuses to mint a token without an assignment. Don't assume `roles` will be present just because the token is app-only — only granted roles appear.
- **`azp` / `appid` allow-list (SECONDARY — defense in depth)** — the calling client's app ID. Maintain an allow-list of client app IDs that may call this endpoint. This catches mis-grants where a role was assigned more broadly than intended.
- **No `scp` claim** — additional defense in depth: a true app token never has `scp`. Reject if present.
- **`idtyp == "app"`** — *optional* signal only; the claim is not always emitted, so **never** rely on it as a required check. Use it as a hint, not a gate.

Doctrine: `roles` is the authorization decision; `azp` is a circuit-breaker. Mirrors [dotnet-engineering-guide ch02 §10](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz) — separate named policies per identity model, **never** an OR-claims policy.

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
- **CAE (Continuous Access Evaluation)**: opt-in, lets Entra invalidate tokens early on conditional-access events. Microsoft.Identity.Web supports it via `WithClientCapabilities(["cp1"])` on the client and by surfacing `WWW-Authenticate: Bearer error="insufficient_claims"` from the API per [RFC 6750 §3.1](https://www.rfc-editor.org/rfc/rfc6750#section-3.1). Honor it.
- **ACRS / claims challenges**: for step-up auth (e.g., MFA required for a sensitive endpoint), the API must return **401** with a `WWW-Authenticate: Bearer error="insufficient_claims", claims="<base64url-json>"` header so the client re-acquires a token satisfying the policy. Format follows [RFC 6750 §3.1](https://www.rfc-editor.org/rfc/rfc6750#section-3.1); error bodies should follow [RFC 9457 Problem Details](https://www.rfc-editor.org/rfc/rfc9457). Use Microsoft.Identity.Web helpers — e.g. `HttpContext.GetTokenAcquirer().ReplyForbiddenWithWwwAuthenticateHeaderAsync(...)` or build the header via `WwwAuthenticateParameters` — rather than throwing a bare exception (which won't include the `claims` parameter).
- **Multi-audience APIs**: list every accepted audience in `ValidAudiences`. Never disable audience validation.
- **Must always validate** `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`, and signature. **No sanctioned exception.** If you think you need one, file an issue first — the answer is almost always "no, you have a bug elsewhere."

---

## 7. Quick checklist per endpoint

- [ ] `[Authorize]` present.
- [ ] `[RequiredScope]` (user) or `[Authorize(Policy="...")]` (app) — never `[Authorize(Roles=...)]` on a JWT scheme with `MapInboundClaims=false` (it silently no-ops; see [DOCTRINE.md](../DOCTRINE.md#authorization-policy-doctrine-canonical)).
- [ ] Mixed-claims policies: each named policy rejects tokens that carry the *other* claim type (so a user token never satisfies an app policy and vice versa).
- [ ] App-only endpoints additionally allow-list `azp`/`appid`.
- [ ] Multi-tenant: tenant allow-list enforced in `IssuerValidator`.
- [ ] Audience set explicitly to App ID URI (and GUID during v1↔v2 migration).
- [ ] CAE enabled if calling Entra-protected downstream APIs.

---

## Sources

- RFC 7519 — JSON Web Token (JWT) — [rfc-editor.org/rfc/rfc7519](https://www.rfc-editor.org/rfc/rfc7519)
- RFC 6750 — The OAuth 2.0 Authorization Framework: Bearer Token Usage — [rfc-editor.org/rfc/rfc6750](https://www.rfc-editor.org/rfc/rfc6750) (esp. §3 `WWW-Authenticate` Response Header Field)
- RFC 9457 — Problem Details for HTTP APIs — [rfc-editor.org/rfc/rfc9457](https://www.rfc-editor.org/rfc/rfc9457)
- OpenID Connect Core 1.0 — [openid.net/specs/openid-connect-core-1_0.html](https://openid.net/specs/openid-connect-core-1_0.html)
- Microsoft Entra access token claims reference — [learn.microsoft.com/entra/identity-platform/access-token-claims-reference](https://learn.microsoft.com/entra/identity-platform/access-token-claims-reference)
- Token version (`requestedAccessTokenVersion`) — [learn.microsoft.com/entra/identity-platform/access-tokens#token-formats](https://learn.microsoft.com/entra/identity-platform/access-tokens#token-formats)
- Microsoft.Identity.Web — multi-tenant web APIs — [github.com/AzureAD/microsoft-identity-web/wiki/multi-tenant-web-apis](https://github.com/AzureAD/microsoft-identity-web/wiki/multi-tenant-web-apis)
- Microsoft.Identity.Web — [github.com/AzureAD/microsoft-identity-web](https://github.com/AzureAD/microsoft-identity-web)
- Continuous Access Evaluation (CAE) — [learn.microsoft.com/entra/identity/conditional-access/concept-continuous-access-evaluation](https://learn.microsoft.com/entra/identity/conditional-access/concept-continuous-access-evaluation)
- Claims challenges, claims requests, and client capabilities — [learn.microsoft.com/entra/identity-platform/claims-challenge](https://learn.microsoft.com/entra/identity-platform/claims-challenge)
- App roles — [learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps](https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps)
- dotnet-engineering-guide ch02 §10 — auth doctrine — [github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz)
