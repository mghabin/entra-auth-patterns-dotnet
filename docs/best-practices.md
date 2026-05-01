# Best Practices

Short, opinionated. Defaults that should hold unless you have a specific reason.

Strength markers used below: **must** = no exception without an issue filed; **should** = strong default; **prefer** = pick this when both work; **avoid** = real cost, real risk.

Cross-reference: see [decision-trees.md Tree 4](decision-trees.md#4-does-this-endpoint-accept-delegated-app-only-or-both) for the delegated-vs-app-only routing tree.

## Credentials

- **must** follow the order of preference: Managed Identity → Workload Identity Federation (FIC) → Certificate (KV-backed) → Client Secret. Treat secret as a smell. (See [credential-patterns/](credential-patterns/index.md#decision-matrix).)
- **prefer** user-assigned MI for shared/blue-green infra; **should** use system-assigned only when the lifecycle truly matches the resource.
- **must not** put secrets in code, repos, env vars, or pipeline variables. Use Key Vault + reference, or FIC.
- **must** rotate certs ≤ 12 months, secrets ≤ 6 months; **should** automate it.
- **should** use one identity per workload — **avoid** sharing an MI/app reg across unrelated services.

## Acquisition

- **must** reuse credential / MSAL client / `TokenCredential` instances (singleton). They cache tokens internally; a per-request instance defeats the cache.
- **must** request `<resource>/.default` for app tokens, not individual scopes.
- **must** use **OBO** for user → downstream calls; **must not** substitute an app token for a user token.
- **must** use `AddDistributedTokenCaches` in ASP.NET Core when scaled out (OBO cache is per-user — see [acquisition.md §4b](acquisition.md#4b-user-tokens-obo)). `InMemory` only for single-instance/dev.
- **prefer** library by host: workers → **MSAL.NET** directly; APIs → **Microsoft.Identity.Web**; Azure SDK calls → **Azure.Identity**.

## Validation

- **must not** disable `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`, or signature validation. No sanctioned exception. (See [validation.md §6](validation.md#6-cross-cutting-keys-clock-cae-acrs).)
- **must** pin audience explicitly. For **v2** tokens that's the API's **client ID (GUID)**; for **v1** it's the App ID URI (`api://…`) — accept both during migration.
- **must** authorize delegated and app-only on **separate named policies attached to separate authorization attributes** — never one OR-claims policy. Concretely:

  ```csharp
  // Doctrine: two policies, two attributes — never OR'd.
  [Authorize(Policy = "OrdersDelegated")]   // requires scp=orders.read
  [HttpGet("me/orders")]
  public IActionResult GetMyOrders() => …

  [Authorize(Policy = "OrdersApp")]         // requires roles=Orders.Process AND azp ∈ allow-list
  [HttpPost("orders/process")]
  public IActionResult ProcessAll() => …
  ```

  Mirrors [dotnet-engineering-guide ch02 §10](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz). See also [decision-trees.md Tree 4](decision-trees.md#4-does-this-endpoint-accept-delegated-app-only-or-both).
- **must** allow-list `azp`/`appid` on app-only endpoints (defense in depth on top of `roles` — see [validation.md §4](validation.md#4-app-token-specific-checks)).
- **must** implement an `IssuerValidator` for multi-tenant APIs that enforces a `tid` allow-list and verifies `tid` matches the issuer's tenant. (See [validation.md §3](validation.md#3-issuer-audience-v1-vs-v2-single-vs-multi-tenant).)
- **must not** use `TenantId: "common"` unless you really accept MSA *and* have a tenant filter. **Prefer** `"organizations"` for SaaS.
- **must** honor CAE challenges; surface `WWW-Authenticate: Bearer error="insufficient_claims"` from the API and re-acquire on the client (per [RFC 6750 §3](https://www.rfc-editor.org/rfc/rfc6750#section-3)).
- **should** use claims challenges / ACRS for step-up (MFA, compliant device) on sensitive endpoints — **avoid** baking step-up into UI logic alone.

## App registration / tenant hygiene

- **must** define app roles for S2S; **must not** reuse delegated scopes for app permissions.
- **must** grant the minimum app role to the minimum SP (the MI's SP, the worker's app reg) — least privilege.
- **must** use separate app registrations for separate environments (local/ci/test/prod). **Must not** share secrets across rings.
- **must** keep an explicit list of provisioned tenants for multi-tenant SaaS; provision via admin consent, deprovision on offboarding.

## Operational

- **must** log the claims used to authorize (`oid`, `tid`, `azp`, `roles`, `scp`); **must not** log raw tokens.
- **should** emit metrics on auth failures by reason (`invalid_audience`, `invalid_issuer`, `missing_role`, `cae_challenge`).
- **should** alert on sudden drops in token-cache hit-rate (often a misconfigured singleton).
- **must** test the validation pipeline with negative tests: wrong audience, wrong tenant, expired, missing role, app token where user expected, user token where app expected.

---

## Sources

- dotnet-engineering-guide ch02 §10 — auth doctrine (separate policies, no OR-claims) — [github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz)
- infra-engineering-guide ch06 §3 — workload identity / no static credentials — [github.com/mghabin/infra-engineering-guide/blob/main/docs/06-security-supply-chain.md#3-workload-identity--the-no-static-credentials-rule](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/06-security-supply-chain.md#3-workload-identity--the-no-static-credentials-rule)
- Microsoft Entra access token claims reference — [learn.microsoft.com/entra/identity-platform/access-token-claims-reference](https://learn.microsoft.com/entra/identity-platform/access-token-claims-reference)
- Managed identities for Azure resources — [learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview](https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview)
- Workload identity federation — [learn.microsoft.com/entra/workload-id/workload-identity-federation](https://learn.microsoft.com/entra/workload-id/workload-identity-federation)
- App roles — [learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps](https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps)
- Microsoft.Identity.Web — [github.com/AzureAD/microsoft-identity-web](https://github.com/AzureAD/microsoft-identity-web)
- RFC 6749 — OAuth 2.0 Authorization Framework — [rfc-editor.org/rfc/rfc6749](https://www.rfc-editor.org/rfc/rfc6749)
- RFC 6750 — Bearer Token Usage — [rfc-editor.org/rfc/rfc6750](https://www.rfc-editor.org/rfc/rfc6750)
- RFC 7519 — JSON Web Token (JWT) — [rfc-editor.org/rfc/rfc7519](https://www.rfc-editor.org/rfc/rfc7519)
- OWASP ASVS v4 §3 (Session Management) and §4 (Access Control) — [owasp.org/www-project-application-security-verification-standard](https://owasp.org/www-project-application-security-verification-standard/)
