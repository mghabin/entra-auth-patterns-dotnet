# Best Practices

Short, opinionated. Defaults that should hold unless you have a specific reason.

## Credentials
1. **Order of preference**: Managed Identity → Workload Identity Federation (FIC) → Certificate (KV-backed) → Client Secret. Treat secret as a smell.
2. Prefer **user-assigned MI** for shared/blue-green infra; system-assigned only when the lifecycle truly matches the resource.
3. **No secrets in code, repos, env vars, or pipeline variables.** Key Vault + reference, or FIC.
4. Rotate certs ≤ 12 months, secrets ≤ 6 months, automate it.
5. One identity per workload — don't share an MI/app reg across unrelated services.

## Acquisition
6. Reuse credential / MSAL client / `TokenCredential` instances (singleton). They cache tokens internally.
7. For app tokens, request `<resource>/.default`, not individual scopes.
8. For user → downstream, use **OBO**; never substitute an app token for a user token.
9. In ASP.NET Core, use **`AddDistributedTokenCaches`** when scaled out. `InMemory` only for single-instance/dev.
10. In workers, use **MSAL.NET** directly; in APIs, use **Microsoft.Identity.Web**; for Azure SDK calls, use **Azure.Identity**.

## Validation
11. Never disable `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`, or signature validation.
12. Pin **audience** explicitly. For **v2** tokens that's the API's **client ID (GUID)**; for **v1** it may be the App ID URI (`api://…`) — accept both during migration.
13. Authorize on **`scp` for user tokens** and **`roles` for app tokens**, explicitly. Don't share one policy that accepts both silently.
14. App-only endpoints: also **allow-list `azp`/`appid`**.
15. Multi-tenant: implement an **`IssuerValidator`** that enforces a tenant allow-list and verifies `tid` matches the issuer's tenant.
16. Don't use `TenantId: "common"` unless you really accept MSA *and* have a tenant filter.
17. Honor **CAE** challenges; surface `WWW-Authenticate: Bearer error="insufficient_claims"` from the API and re-acquire on the client.
18. Use **claims challenges / ACRS** for step-up (MFA, compliant device) on sensitive endpoints — don't bake it into UI logic alone.

## App registration / tenant hygiene
19. Define **app roles** for S2S; don't reuse delegated scopes for app permissions.
20. Grant the minimum app role to the minimum SP (the MI's SP, the worker's app reg) — least privilege.
21. Use separate app registrations for separate environments (dev/test/prod). Don't share secrets across rings.
22. For multi-tenant SaaS: keep an **explicit list** of provisioned tenants; provision via admin consent, deprovision on offboarding.

## Operational
23. Log **claims you used to authorize** (`oid`, `tid`, `azp`, `roles`, `scp`) — never log the raw token.
24. Emit metrics on auth failures by reason (`invalid_audience`, `invalid_issuer`, `missing_role`, `cae_challenge`).
25. Alert on sudden drops in token-cache hit-rate (often a misconfigured singleton).
26. Test the validation pipeline with **negative tests**: wrong audience, wrong tenant, expired, missing role, app token where user expected, user token where app expected.
