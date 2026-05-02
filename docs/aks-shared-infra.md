# Shared Auth Platform across Products & Services

*Audience: staff engineers, product tech leads, platform team. Context: ~10 products, ~20 services on shared AKS infra, with some cross-service calls. Each service today carries its own auth middleware.*

## The real problem
Surface form: *"Can we centralize auth into one layer for all services?"*
What's actually broken: **the same JwtBearer + `[Authorize]` + tenant-allow-list + claims-handling plumbing is re-implemented in every repo**, and the implementations have drifted across products.

That's a **code/config duplication problem**, not a runtime traffic problem. The right fix is a **shared platform** (libraries + IaC + standards), **not a shared service** that requests proxy through.

## Position
> **Centralize the *implementation* of auth across all products — not the *runtime path*.**
> One supported way to do auth in our org, owned by a small platform team, consumed by every product as a library + IaC module. Per-request enforcement stays in each service (zero-trust, no SPOF). The edge handles uniform first-line policy.

## Doctrine — named auth schemes (must)

- Code **MUST** branch on scheme name when an API serves more than one identity provider or audience type.
- A single `[Authorize]` attribute **MUST NEVER** silently accept multiple issuers, audiences, or token shapes — register each as a distinct named `JwtBearer` scheme and select it per route or per controller.
- Multiple `AddJwtBearer(...)` calls under the same default scheme name silently *fall through* to the first one that validates. That is a privilege-escalation bug, not a feature. Avoid.
- Owner: [`validation.md`](validation.md) §3 (multi-tenant + audience) and the .NET auth-policy doctrine in dotnet-engineering-guide [ch02 §10](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz). The library wires this for free; teams **MUST NOT** call `AddJwtBearer(...)` directly.

## What "centralized auth" actually is — four artifacts

### 1. `EntraAuth.Auth` shared NuGet
A product team's auth wiring becomes:

```csharp
// Program.cs in any of the 20 services
builder.Services.AddEntraAuth(builder.Configuration);
```
```jsonc
// appsettings.json — only the per-service variance:
"EntraAuth": {
  "ProductId":         "lms",
  "ApiClientId":       "<this-api-app-id>",
  "AllowedClientApps": [ "<reporting-svc-app-id>", "<grading-svc-app-id>" ],
  "Tenancy":           "MultiTenant",         // or "SingleTenant"
  "UserSchemes":       [ "Workforce", "External" ]   // pick what this API serves
}
```

The library owns:
- `AddMicrosoftIdentityWebApi(...)` per named scheme (`Workforce`, `External`).
- Standard `IssuerValidator` enforcing the **central tenant allow-list** (loaded from config / Key Vault).
- Standard `Forbid` handler emitting `WWW-Authenticate: Bearer error="insufficient_claims", claims="..."` for ACRS/CAE.
- OTel-based claim logging (`oid`, `tid`, `azp`, `roles`, `scp`) — never the raw token.
- Auth-failure metrics with a single name across all services.
- Helpers: `[RequireUserScope("...")]`, `[RequireAppRole("...")]`, `RequireClientApp("...")` for `azp` allow-listing.
- Pre-wired `IDownstreamApi` factories with the org's preferred client-credential chain (MI → FIC → cert).
- Distributed token cache (Redis) with safe defaults; no team configures cache by hand.

### 2. `EntraAuth.Auth.Edge` IaC module (Bicep + Helm)
Drop-in module that installs the JWT-validation policy at the edge — APIM `validate-jwt` policy or AKS ingress JWT filter — with the same tenant allow-list, the same expected audiences, and uniform WAF rules. One module, every product instantiates it for its own product slice.

The edge does **not replace** the library. It is the uniform first-line filter:
- Drops malformed / wrong-issuer / wrong-audience tokens before they hit any pod.
- Single place to publish the **tenant allow-list** for SaaS (the library re-checks — defense in depth).
- Central audit log of authN failures across all 10 products.
- Security reviews one policy bundle in PR, not 20 service repos.

### 3. `EntraAuth.Auth.Standards` (short markdown)
- App-registration naming: `<env>-<product>-<service>-<purpose>`.
- App-role taxonomy: `Product.Resource.Action` (e.g., `Grading.Submission.Write`). Reviewed at PR time.
- Audience model: v2 client ID; document v1 acceptance only during migration.
- Per-environment tenant model.
- Secret/cert/FIC policy and rotation cadence.
- On-call ownership; deprecation policy for the NuGet.

### 4. `EntraAuth.Auth.TestKit` NuGet
Token-forging helpers (signed by a test JWKS) and ready-made negative-test fixtures: wrong audience, wrong issuer, missing role, expired, app token where user expected, user where app expected. Removes the "every team writes their own auth tests badly" problem.

## Library vs Service — decision rationale

| Concern                               | Shared library + IaC (recommended)                                  | Shared auth service (the proposal)                                            |
| ------------------------------------- | ------------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| Solves duplication across 20 services | ✅ One implementation, semver'd                                      | ✅ Only if every service migrates *and stays migrated*                         |
| Solves drift across 10 products       | ✅ Defaults + lint enforce standards                                 | ⚠️ Standards leak into bespoke proxy logic                                    |
| Runtime cost                          | None — in-process, JWKS cached                                      | +1 hop per request; p99 hit; SPOF when it hiccups                             |
| Operational cost                      | Owned like any internal NuGet                                       | New tier-0 service: capacity, on-call, regional HA, DR                        |
| Conway's-law risk                     | Low — teams self-serve via package upgrade                          | High — every product's auth change queues behind one team                     |
| Token re-issuance temptation          | None                                                                | Inevitable — you become an IdP                                                |
| OBO / delegated downstream            | Lives in the calling service, supported by lib helpers              | **Cannot** be centralized — needs the calling service's credential & audience |
| Cross-product S2S                     | App tokens with `roles`, validated locally; lib handles boilerplate | The proxy adds nothing                                                        |
| Failure blast radius                  | Bad release rolled back per-service, gradual canary                 | Auth service down ⇒ every product down                                        |
| Rollout                               | Canary one service, fan out; pin versions per product               | Big-bang or risky dual-stack                                                  |

The library approach gives you everything you wanted (one authoritative implementation) without inheriting the downsides of a runtime auth service.

## Education-domain specifics
- **Two user populations**: staff/admins (Entra **workforce**) vs students/parents (Entra **External ID** / B2C, or SAML/OIDC federation through Clever/ClassLink). The library supports **multiple identity providers per product** via named `JwtBearer` schemes, with policies selecting the right scheme per route.
- **Multi-tenant by district/university**: `TenantId: organizations` for staff; tenant onboarding/offboarding becomes a single config change propagated to both edge and library.
- **Compliance** (FERPA / COPPA / GDPR / regional residency): centralized claim logging via the library makes audits straightforward. Raw tokens never logged.

## Cross-service (east-west) communication
You said "some cross-service calls." The boring, correct answer:
- Caller acquires an **app token** (client credentials) for the callee's API, scope `<callee-app-id>/.default`. Use Workload Identity / FIC in AKS — see [acquisition.md](acquisition.md).
- Callee **MUST** validate **both** `roles` (app-role gate) **AND** `azp` / `appid` (per-client allow-list). Either alone is insufficient: `roles` without `azp` admits any tenant SP that was granted the role; `azp` without `roles` admits any token from that client regardless of permission. Owner: [`validation.md` §4](validation.md#4-app-token-specific-checks) and dotnet-engineering-guide [ch02 §10](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz).
- In code that means `[Authorize(Roles = "Grading.Submission.Write")]` **and** `RequireClientApp("<caller-app-id>")` from the library — **never one without the other** on an app-only endpoint.
- That's it. **No mesh required for this.**

A service mesh (Istio/Linkerd) is the right end-state for **mTLS + traffic management**; it is *not* the reason to centralize auth. Defer until the org is ready to operate one.

## Migration plan (20 snowflakes → 1 library, 20 adopters)
1. **Inventory & triage** the 20 services' current auth behavior. Patch dangerously-wrong ones (open multi-tenant, missing audience pin, missing role check) **in place** first.
2. **Ship `EntraAuth.Auth` v0** with feature parity for the most common shape (single-tenant Web API). Adopt in 1–2 willing pilot services.
3. **Add multi-tenant + External ID/B2C scheme** based on pilot feedback; release v1.
4. **Mandate** for all *new* services. Existing services adopt during normal maintenance; track adoption as a platform KPI.
5. **Ship `EntraAuth.Auth.Edge`** module; turn it on per product as adoption reaches it.
6. **Deprecate** in-repo `JwtBearer` boilerplate via a Roslyn analyzer once adoption is high enough.
7. **Quarterly review** of standards, app-role taxonomy, and tenant allow-list governance.

### Rollback strategy (must)

- Every adopter pins the `EntraAuth.Auth` version in their csproj; no floating `*` versions. A bad library release is rolled back per-service by reverting that pin and redeploying — no platform-team coordination needed.
- The library follows **semver**: breaking config or behaviour changes go in a major; deprecations ship one major in advance with a Roslyn analyzer warning.
- Each release is canaried in **one pilot service first**, observed for at least one business cycle on the auth-failure metrics emitted by the library itself, and only then promoted as the recommended version.
- The edge module (`EntraAuth.Auth.Edge`) is rolled back via the same Bicep/Helm pipeline that deployed it — the previous chart digest is the rollback target. The library re-validates on the pod, so an edge rollback **MUST NOT** loosen what the library still enforces (defense-in-depth holds during rollback).
- Roslyn analyzer enforcement (step 6) ships as a **warning first, error later** — never both in the same release — so a bad analyzer rule cannot block every product's build at once.

## Risks & anti-patterns
- ❌ Building a runtime auth service. Covered above.
- ❌ Library too opinionated → teams fork it. Mitigate with extension points (`Action<JwtBearerOptions>` hooks) and a small RFC process.
- ❌ Library too thin → drift returns. Mitigate with sane defaults *and* an analyzer flagging direct `AddJwtBearer(...)` outside the library.
- ❌ Tenant allow-list living only at the edge or only in the library. Defense-in-depth: **both**, fed from one source of truth.
- ❌ Multiple IdPs bolted on as `if` branches. Use named auth schemes from day one.
- ❌ App roles invented per product without taxonomy. Mandate `Product.Resource.Action`, review at PR time.
- ❌ Treating cross-service calls as a special case. They're just app tokens.
- ❌ Adopting a mesh "for auth." Wrong reason. Adopt mesh for mTLS / traffic management; auth is a bonus.
- ❌ Minting internal JWTs at a gateway. You've become an IdP — almost always a regret.

## Explicitly out of scope
- Building a runtime auth service.
- Adopting a service mesh as part of this initiative.
- Replacing Entra ID with an internal IdP.

## Related
- [acquisition.md](acquisition.md) — token acquisition (MI / FIC / cert / secret) used by the library's client-credential chain and by workers.
- [validation.md](validation.md) — what the library wires up under the hood.
- [matrix.md](matrix.md) — per-scenario picks.
- [best-practices.md](best-practices.md) — rules the library enforces by default.

## Sources

- Microsoft Entra ID best practices — [learn.microsoft.com/entra/identity/fundamentals/identity-best-practices-checklist](https://learn.microsoft.com/entra/identity/fundamentals/identity-best-practices-checklist)
- Microsoft Entra app roles — [learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps](https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps)
- Microsoft.Identity.Web — multiple authentication schemes — [github.com/AzureAD/microsoft-identity-web/wiki/Multiple-Authentication-Schemes](https://github.com/AzureAD/microsoft-identity-web/wiki/Multiple-Authentication-Schemes)
- Continuous Access Evaluation (CAE) — [learn.microsoft.com/entra/identity/conditional-access/concept-continuous-access-evaluation](https://learn.microsoft.com/entra/identity/conditional-access/concept-continuous-access-evaluation)
- Azure Kubernetes Service — workload identity — [learn.microsoft.com/azure/aks/workload-identity-overview](https://learn.microsoft.com/azure/aks/workload-identity-overview)
- OWASP Zero-Trust Architecture cheat sheet — [cheatsheetseries.owasp.org/cheatsheets/Zero_Trust_Architecture_Cheat_Sheet.html](https://cheatsheetseries.owasp.org/cheatsheets/Zero_Trust_Architecture_Cheat_Sheet.html)
- NIST SP 800-207 Zero Trust Architecture — [nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-207.pdf](https://nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-207.pdf)
- infra-engineering-guide ch04 (containers/Kubernetes — service mesh "when not to") — [github.com/mghabin/infra-engineering-guide/blob/main/docs/04-containers-k8s.md](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/04-containers-k8s.md)
- dotnet-engineering-guide ch02 §10 (auth doctrine) — [github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz)
