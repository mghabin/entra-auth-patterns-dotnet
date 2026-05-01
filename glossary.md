# Glossary

This glossary defines the terms used across the Entra Auth Patterns guide.
Definitions are opinionated: where the Entra / OAuth ecosystem uses a term inconsistently, the entry calls out the disambiguation and pins this guide to a specific primary source.
Citations point at primary sources (Microsoft Learn, IETF RFCs, the OpenID Connect Core spec, the official Microsoft GitHub repos for Microsoft.Identity.Web / MSAL.NET / Azure SDK) — not at blog summaries.

**Terms with strong opinions in this guide** (read these entries carefully): *azp*, *appid*, *roles*, *scp*, *DefaultAzureCredential*, *Federated Identity Credential*, *SignedAssertionFromManagedIdentity* (often confused with FIC), *IssuerValidator*, *v1 vs v2 token*.

**Deprecated or to-be-avoided in new work**: client secrets on app registrations (use *Managed Identity* or *Federated Identity Credential* instead), v1.0 endpoint for new app registrations (use v2.0), `idtyp` checks as a substitute for the *roles* + *azp* allow-list (the substitution is unsafe).

---

## A

### ACRS (Authentication Context Class Reference Set)

Microsoft Entra mechanism that lets a resource API demand a *Conditional Access* authentication context (e.g., MFA, compliant device) from the caller via a `claims` challenge. The API returns a 401 with a `WWW-Authenticate: Bearer claims="…"` header carrying the required `acrs` value; the client re-acquires a token with the satisfied claim. Owned in [`docs/validation.md`](./docs/validation.md) §6. [Conditional Access — auth context](https://learn.microsoft.com/entra/identity/conditional-access/concept-conditional-access-cloud-apps#authentication-context).

### app-only token / application permission

A token issued via the OAuth 2.0 *client_credentials* grant ([RFC 6749 §4.4](https://www.rfc-editor.org/rfc/rfc6749#section-4.4)) that represents the *application itself*, not a signed-in user. Carries `roles` (app role) and `azp` (authorized party); **no** `scp` claim. Permissions are "Application permissions" in the Entra app-registration UI and require admin consent. [Microsoft identity platform — application permissions](https://learn.microsoft.com/entra/identity-platform/permissions-consent-overview).

### appid (claim, v1.0)

The Entra v1.0-token claim identifying the **client application** that obtained the token. Equivalent to *azp* in v2.0 tokens. Use *appid* / *azp* — not *scp* or *roles* — to gate per-client policy. [Access token claims reference](https://learn.microsoft.com/entra/identity-platform/access-token-claims-reference).

### Azure.Identity

The Azure SDK package (`Azure.Identity`, [github.com/Azure/azure-sdk-for-net](https://github.com/Azure/azure-sdk-for-net/tree/main/sdk/identity/Azure.Identity)) that exposes `TokenCredential` implementations (`ManagedIdentityCredential`, `WorkloadIdentityCredential`, `ClientCertificateCredential`, *DefaultAzureCredential*, …) for calling **Azure resources** (Storage, Key Vault, Cosmos, Service Bus). Not for arbitrary OAuth flows; does not implement *OBO*. [Azure.Identity overview](https://learn.microsoft.com/dotnet/api/overview/azure/identity-readme).

### azp (Authorized Party claim)

The OIDC standard claim ([OpenID Connect Core §2](https://openid.net/specs/openid-connect-core-1_0.html#IDToken)) carrying the **client_id of the application** the token was issued to. In Entra v2.0 tokens it identifies the calling app; combined with *roles* it forms the per-client allow-list pattern in [`docs/validation.md`](./docs/validation.md) §4. v1.0-token equivalent is *appid*. [Access token claims reference](https://learn.microsoft.com/entra/identity-platform/access-token-claims-reference).

---

## B

### BFF (Backend-for-Frontend)

Server-side façade that performs the OIDC sign-in on behalf of a browser SPA, holds the user's refresh token in a server-side cache, and brokers downstream API calls (often via *OBO*). Pattern named in Sam Newman's *Building Microservices* and called out by Microsoft as the recommended browser-app shape. [Microsoft identity platform — BFF pattern](https://learn.microsoft.com/azure/architecture/patterns/backends-for-frontends).

> **In this sample**, `Ftgo.ApiGateway` is named after — and demonstrates the *downstream* half of — the BFF pattern (server-side OBO + S2S fan-out, no client secrets via `SignedAssertionFromManagedIdentity`). It is **not** a strict BFF: the user OIDC sign-in is performed **client-side** by the Scalar UI using Auth Code + PKCE, and the gateway only validates the bearer JWT (`AddMicrosoftIdentityWebApi`) — there is no server-side cookie session and no refresh-token cache. Treat it as an *API gateway with token aggregation*; for the full BFF (cookie session + server-side token store), front it with [Duende.BFF](https://docs.duendesoftware.com/identityserver/v7/bff/) or YARP-with-cookies.

---

## C

### CAE (Continuous Access Evaluation)

Entra mechanism that lets a resource API revoke or re-validate a token between issuance and expiry by responding 401 with a `claims` challenge; the client re-acquires a CAE-aware token. Long-lived (24h) tokens become safe because revocation is near-real-time. Owned in [`docs/validation.md`](./docs/validation.md) §6. [Continuous access evaluation](https://learn.microsoft.com/entra/identity/conditional-access/concept-continuous-access-evaluation).

### Conditional Access

Entra policy engine that evaluates signals (user, device, location, risk, app) at sign-in time and during *CAE* re-evaluation, then grants, blocks, or steps up the session (MFA, compliant device). App developers consume CA via *ACRS* claims challenges; CA *authoring* is out of scope for this guide. [Conditional Access overview](https://learn.microsoft.com/entra/identity/conditional-access/overview).

### consent (admin vs user)

The Entra step where a tenant authorises an app to receive specific permissions. **User consent** is per-user and limited to delegated permissions the tenant marks as user-consentable; **admin consent** is tenant-wide and required for application permissions and any admin-only scopes. Multi-tenant apps require admin consent in each tenant before any non-trivial scope works. [Permissions and consent overview](https://learn.microsoft.com/entra/identity-platform/permissions-consent-overview).

---

## D

### DefaultAzureCredential

`Azure.Identity` chained `TokenCredential` that tries, in order: environment variables, *Workload Identity*, *Managed Identity*, Visual Studio / VS Code, Azure CLI, Azure PowerShell, IntelliJ, interactive browser. Default for *calling Azure resources* in this sample; in local dev it falls through to `az login`. [DefaultAzureCredential reference](https://learn.microsoft.com/dotnet/api/azure.identity.defaultazurecredential).

### delegated permission

A permission granted to an app to act *on behalf of a signed-in user*. Surfaces in the access token as the *scp* claim (space-separated scopes). Contrast with *application permission* (which surfaces as *roles*). [Permissions and consent overview](https://learn.microsoft.com/entra/identity-platform/permissions-consent-overview).

---

## E

### Entra ID (formerly Azure AD)

Microsoft's cloud identity provider. Renamed from "Azure Active Directory" in July 2023; the OIDC / OAuth 2.0 endpoints and tokens are unchanged. v2.0 endpoint is the default for new app registrations. [Microsoft identity platform overview](https://learn.microsoft.com/entra/identity-platform/v2-overview), [Azure AD → Microsoft Entra ID rename](https://learn.microsoft.com/entra/fundamentals/new-name).

---

## F

### Federated Identity Credential (FIC)

Entra app-registration credential type that lets an external OIDC issuer (GitHub Actions, GitLab, Kubernetes service accounts, another Entra tenant) mint tokens that Entra accepts in place of a client secret or certificate, via [RFC 8693 OAuth 2.0 Token Exchange](https://www.rfc-editor.org/rfc/rfc8693). Default credential model for non-Azure compute. Configured under **Certificates & secrets → Federated credentials** on the app reg. [Workload identity federation](https://learn.microsoft.com/entra/workload-id/workload-identity-federation), [Configure a federated identity credential](https://learn.microsoft.com/entra/workload-id/workload-identity-federation-create-trust).

---

## I

### idtyp (id-type claim)

Optional Entra access-token claim added when configured via an [optional claims policy](https://learn.microsoft.com/entra/identity-platform/optional-claims). Value is `app` for app-only tokens, `user` for delegated tokens. Useful as a *secondary* signal but **not** a substitute for the *roles* + *azp* allow-list — `idtyp` alone does not pin which client app may call you. [Access token claims reference](https://learn.microsoft.com/entra/identity-platform/access-token-claims-reference).

### IssuerValidator (`AadIssuerValidator`)

`Microsoft.IdentityModel.Validators.AadIssuerValidator` — the validator that resolves the per-tenant issuer URL for a multi-tenant app and confirms the token's `iss` matches the cloud-and-tenant the token claims to come from. Required when `TenantId="organizations"` or `"common"`; pair with a `tid` allow-list to restrict *which* tenants you accept. [`AadIssuerValidator` reference](https://learn.microsoft.com/dotnet/api/microsoft.identitymodel.validators.aadissuervalidator), [Microsoft.Identity.Web multi-tenant guidance](https://github.com/AzureAD/microsoft-identity-web/wiki/multi-tenant-web-apis).

---

## M

### Managed Identity (MI) — system-assigned (SAMI), user-assigned (UAMI)

Azure-platform-issued service principal automatically rotated by the platform, accessible from inside Azure compute via the IMDS endpoint. **System-assigned** is bound 1:1 to a single resource lifecycle; **user-assigned** is a standalone resource that can be attached to many compute instances. Default credential for any code running on Azure compute. **In this sample**: the four runtime container apps each use a *system-assigned* MI (one per app, lifecycle-bound), while the GitHub Actions CD identity is a *user-assigned* MI per env (`ftgo-{env}-cd-mi`) so it can outlive any single resource. [Managed identities overview](https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview), [System- vs user-assigned](https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/managed-identities-faq).

### Microsoft.Identity.Web

ASP.NET Core integration package wrapping *MSAL.NET* + `JwtBearer` middleware. Owns the JWT validation pipeline (`AddMicrosoftIdentityWebApi`), OBO (`GetTokenForUserAsync`), token-cache plumbing, and the `[RequiredScope]` / `[RequiredScopeOrAppPermission]` filters. Default library for ASP.NET Core APIs and web apps in this guide. [Microsoft.Identity.Web docs](https://learn.microsoft.com/entra/identity-platform/microsoft-identity-web), [github.com/AzureAD/microsoft-identity-web](https://github.com/AzureAD/microsoft-identity-web).

### MSAL.NET

`Microsoft.Identity.Client` — the .NET implementation of the Microsoft Authentication Library. Lower-level than *Microsoft.Identity.Web*; use directly in non-ASP.NET hosts (workers, libraries) when you need MSAL features Microsoft.Identity.Web does not surface (claims challenges with custom UX, brokered auth, advanced token-cache wiring). [MSAL overview](https://learn.microsoft.com/entra/identity-platform/msal-overview), [github.com/AzureAD/microsoft-authentication-library-for-dotnet](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet).

### multi-tenant (Entra app)

App registration whose `signInAudience` accepts users / apps from any Entra tenant (`AzureADMultipleOrgs` or `AzureADandPersonalMicrosoftAccount`). The API authority is `organizations` (or `common`); validation requires *IssuerValidator* + a *tid* allow-list. Owned in [`docs/validation.md`](./docs/validation.md) §3. [Convert app to multi-tenant](https://learn.microsoft.com/entra/identity-platform/howto-convert-app-to-be-multi-tenant).

---

## O

### OBO (On-Behalf-Of)

OAuth 2.0 flow ([RFC 8693 OAuth 2.0 Token Exchange](https://www.rfc-editor.org/rfc/rfc8693), Microsoft variant) in which a middle-tier API exchanges the inbound user access token for a new access token to call a downstream API as the same user. Implemented in `Microsoft.Identity.Web` via `IDownstreamApi` / `ITokenAcquisition.GetAccessTokenForUserAsync`. [Microsoft identity platform — OBO flow](https://learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow).

### OIDC (OpenID Connect)

Identity layer on top of OAuth 2.0 ([RFC 6749](https://www.rfc-editor.org/rfc/rfc6749)) defined in [OpenID Connect Core 1.0](https://openid.net/specs/openid-connect-core-1_0.html). Standardises the *id_token* (a JWT identifying the user), the `userinfo` endpoint, and discovery (`/.well-known/openid-configuration`). Entra implements the OIDC v1.0 and v2.0 endpoints.

---

## R

### RequiredScope / RequiredScopeOrAppPermission

`Microsoft.Identity.Web` authorization filter attributes that enforce a required *scp* (delegated) or *roles* (app) value on an endpoint. Use *separate* attributes for separate policies — never compose into an OR-claims policy. [Microsoft.Identity.Web — RequiredScope](https://github.com/AzureAD/microsoft-identity-web/wiki/web-apis#protect-aspnet-core-web-apis).

### roles (claim, app role)

JWT claim emitted by Entra carrying the **app roles** assigned to the calling principal (user or app). For app-only tokens, `roles` is the application-permission name; for delegated tokens, `roles` is the user's app-role assignment. Defined per app registration via "App roles" blade. [Add app roles in your application](https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps).

---

## S

### scp (claim, scope)

JWT claim emitted by Entra in **delegated** access tokens carrying the space-separated list of OAuth scopes the user consented to. Absent in app-only tokens. Pair with *roles* / *azp* checks to keep delegated and app-only policies separate. [Access token claims reference](https://learn.microsoft.com/entra/identity-platform/access-token-claims-reference).

### SignedAssertionFromManagedIdentity

`Microsoft.Identity.Web.SignedAssertionFromManagedIdentity` — a delegate that uses a Managed Identity to mint a signed JWT, then submits that JWT to Entra as the `client_assertion` for an app registration's `client_credentials` grant. **This is not OIDC Federated Identity Credential**: there is no external OIDC issuer; the assertion is a Managed-Identity-signed JWT used to *replace a client secret* on the app registration via the FIC trust on the same MI. Used by the BFF in this sample. [Microsoft.Identity.Web — Managed identity as a federated credential](https://github.com/AzureAD/microsoft-identity-web/wiki/Using-managed-identity-as-a-federated-identity-credential).

### SPIFFE / SVID

[Secure Production Identity Framework for Everyone](https://spiffe.io/) — CNCF spec for assigning cryptographic workload identities (SPIFFE Verifiable Identity Documents, *SVIDs*) across Kubernetes / mesh / cloud boundaries. Comparable in role to *Managed Identity* and *Federated Identity Credential* but vendor-neutral; mentioned for cross-platform comparison only. [SPIFFE overview](https://spiffe.io/docs/latest/spiffe-about/overview/).

---

## T

### tenant / tid (claim)

A tenant is an instance of *Entra ID* (one directory). The `tid` claim in a JWT carries the tenant the principal authenticated against. In multi-tenant validation, `tid` is the gate: keep an explicit allow-list of tenant GUIDs; never accept "any tenant" silently. [Access token claims reference](https://learn.microsoft.com/entra/identity-platform/access-token-claims-reference), [Tenant fundamentals](https://learn.microsoft.com/entra/fundamentals/whatis).

---

## V

### v1 vs v2 token (audience format)

Entra emits two token-format generations. **v1.0** tokens use the `aud` claim populated with the resource App ID URI (e.g., `api://orders`); the issuer is `https://sts.windows.net/{tid}/`. **v2.0** tokens use the resource's *App ID* GUID as `aud` (or the App ID URI if configured); the issuer is `https://login.microsoftonline.com/{tid}/v2.0`. Pick v2.0 for new app regs (`accessTokenAcceptedVersion: 2`). The format the API receives must match what its `JwtBearerOptions` expects. [Access tokens — versions](https://learn.microsoft.com/entra/identity-platform/access-tokens), [App manifest reference — `accessTokenAcceptedVersion`](https://learn.microsoft.com/entra/identity-platform/reference-app-manifest).

---

## W

### Workload Identity Federation

Entra-platform feature that lets an external OIDC-compliant identity provider (GitHub Actions, GitLab, Kubernetes service-account issuer, another cloud's OIDC IdP) act as the credential for an Entra app registration or user-assigned managed identity, via [RFC 8693 OAuth 2.0 Token Exchange](https://www.rfc-editor.org/rfc/rfc8693). The on-the-wire credential is a *Federated Identity Credential* (FIC). Default credential pattern for non-Azure compute. [Workload identity federation overview](https://learn.microsoft.com/entra/workload-id/workload-identity-federation).

---

## Sources

- Microsoft identity platform overview — [learn.microsoft.com/entra/identity-platform/v2-overview](https://learn.microsoft.com/entra/identity-platform/v2-overview)
- Access token claims reference — [learn.microsoft.com/entra/identity-platform/access-token-claims-reference](https://learn.microsoft.com/entra/identity-platform/access-token-claims-reference)
- ID token claims reference — [learn.microsoft.com/entra/identity-platform/id-token-claims-reference](https://learn.microsoft.com/entra/identity-platform/id-token-claims-reference)
- Optional claims — [learn.microsoft.com/entra/identity-platform/optional-claims](https://learn.microsoft.com/entra/identity-platform/optional-claims)
- Permissions and consent overview — [learn.microsoft.com/entra/identity-platform/permissions-consent-overview](https://learn.microsoft.com/entra/identity-platform/permissions-consent-overview)
- App roles — [learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps](https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps)
- On-Behalf-Of flow — [learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow](https://learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow)
- Continuous access evaluation — [learn.microsoft.com/entra/identity/conditional-access/concept-continuous-access-evaluation](https://learn.microsoft.com/entra/identity/conditional-access/concept-continuous-access-evaluation)
- Conditional Access — auth context — [learn.microsoft.com/entra/identity/conditional-access/concept-conditional-access-cloud-apps](https://learn.microsoft.com/entra/identity/conditional-access/concept-conditional-access-cloud-apps)
- Managed identities for Azure resources — [learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview](https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview)
- Workload identity federation — [learn.microsoft.com/entra/workload-id/workload-identity-federation](https://learn.microsoft.com/entra/workload-id/workload-identity-federation)
- Configure a federated identity credential — [learn.microsoft.com/entra/workload-id/workload-identity-federation-create-trust](https://learn.microsoft.com/entra/workload-id/workload-identity-federation-create-trust)
- Microsoft.Identity.Web — [learn.microsoft.com/entra/identity-platform/microsoft-identity-web](https://learn.microsoft.com/entra/identity-platform/microsoft-identity-web), [github.com/AzureAD/microsoft-identity-web](https://github.com/AzureAD/microsoft-identity-web)
- MSAL.NET — [learn.microsoft.com/entra/identity-platform/msal-overview](https://learn.microsoft.com/entra/identity-platform/msal-overview), [github.com/AzureAD/microsoft-authentication-library-for-dotnet](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet)
- Azure.Identity — [learn.microsoft.com/dotnet/api/overview/azure/identity-readme](https://learn.microsoft.com/dotnet/api/overview/azure/identity-readme), [github.com/Azure/azure-sdk-for-net](https://github.com/Azure/azure-sdk-for-net/tree/main/sdk/identity/Azure.Identity)
- DefaultAzureCredential — [learn.microsoft.com/dotnet/api/azure.identity.defaultazurecredential](https://learn.microsoft.com/dotnet/api/azure.identity.defaultazurecredential)
- Convert app to multi-tenant — [learn.microsoft.com/entra/identity-platform/howto-convert-app-to-be-multi-tenant](https://learn.microsoft.com/entra/identity-platform/howto-convert-app-to-be-multi-tenant)
- Access tokens — versions — [learn.microsoft.com/entra/identity-platform/access-tokens](https://learn.microsoft.com/entra/identity-platform/access-tokens)
- App manifest reference — [learn.microsoft.com/entra/identity-platform/reference-app-manifest](https://learn.microsoft.com/entra/identity-platform/reference-app-manifest)
- `AadIssuerValidator` — [learn.microsoft.com/dotnet/api/microsoft.identitymodel.validators.aadissuervalidator](https://learn.microsoft.com/dotnet/api/microsoft.identitymodel.validators.aadissuervalidator)
- Microsoft.Identity.Web — Managed identity as a federated credential — [github.com/AzureAD/microsoft-identity-web/wiki/Using-managed-identity-as-a-federated-identity-credential](https://github.com/AzureAD/microsoft-identity-web/wiki/Using-managed-identity-as-a-federated-identity-credential)
- Backends-for-Frontends pattern — [learn.microsoft.com/azure/architecture/patterns/backends-for-frontends](https://learn.microsoft.com/azure/architecture/patterns/backends-for-frontends)
- SPIFFE — [spiffe.io/docs/latest/spiffe-about/overview/](https://spiffe.io/docs/latest/spiffe-about/overview/)
- RFC 6749 — The OAuth 2.0 Authorization Framework — [rfc-editor.org/rfc/rfc6749](https://www.rfc-editor.org/rfc/rfc6749)
- RFC 7519 — JSON Web Token (JWT) — [rfc-editor.org/rfc/rfc7519](https://www.rfc-editor.org/rfc/rfc7519)
- RFC 8693 — OAuth 2.0 Token Exchange — [rfc-editor.org/rfc/rfc8693](https://www.rfc-editor.org/rfc/rfc8693)
- OpenID Connect Core 1.0 — [openid.net/specs/openid-connect-core-1_0.html](https://openid.net/specs/openid-connect-core-1_0.html)
