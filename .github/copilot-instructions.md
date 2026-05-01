# Copilot / AI agent instructions for `mghabin/entra-auth-patterns-dotnet`

> **Read this file before generating code or docs in this repo.** It is the entry point for any AI agent (GitHub Copilot, Claude, Cursor, etc.) working here.

## What this repo is

A **reference implementation** of Microsoft Entra ID auth patterns in .NET. It is **not** a doctrine source. Every architectural rule it follows traces back to one of two upstream guides.

## Search order (mandatory)

When asked to add or change behavior, follow this order **before** writing code:

1. **[`DOCTRINE.md`](../DOCTRINE.md)** — the table that maps each topic to its upstream owner. Start here.
2. **[`coverage-map.md`](../coverage-map.md)** — which file in `docs/` already owns the concept, if it lives in this repo.
3. **[`mghabin/dotnet-engineering-guide`](https://github.com/mghabin/dotnet-engineering-guide)** — language, ASP.NET Core, testing, performance, cloud-native, client. Especially [`docs/02-aspnetcore.md` §10 Authn/Authz](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz) for auth.
4. **[`mghabin/infra-engineering-guide`](https://github.com/mghabin/infra-engineering-guide)** — IaC, CI/CD, containers, observability, security supply-chain, networking, data, reliability, FinOps, platform, identity.
5. **[`docs/`](../docs/)** in this repo — the entra-specific specializations.
6. **Primary sources** — RFC / Microsoft Learn / NIST / OWASP. **Not** Medium / random blog posts.

If steps 1-5 cover the topic, **do not invent new doctrine**. Cite the upstream owner with an anchored link and apply the existing rule. If you must extend doctrine, the new rule belongs in the **upstream guide**, not in this repo.

## Hard rules (non-negotiable)

- **Auth doctrine** is dotnet-guide ch02 §10. Never combine `scp` and `roles` in a single policy. The repo's named policies are `OrdersAuthorizationPolicies.Delegated` (rejects tokens with `roles`) and `.App` (rejects tokens with `scp`). Do not introduce a third "either / or" policy.
- **Deny-by-default** authorization. Endpoints opt out of auth with explicit `[AllowAnonymous]`, never the reverse.
- **JWT defaults** are explicit: `MapInboundClaims = false`, `ValidateIssuer = true`, `ValidateAudience = true`, `ValidateLifetime = true`, `ValidateIssuerSigningKey = true`, `RequireSignedTokens = true`, `RequireExpirationTime = true`. Do not relax these.
- **Multi-tenant** validation uses `AadIssuerValidator` + a `tid` allow-list, never a hand-rolled regex.
- **Credential picker**: MI on Azure compute → FIC across OIDC issuers → cert as portable fallback → client secret only for the three documented exceptions in [`docs/credential-patterns/client-secret.md`](../docs/credential-patterns/client-secret.md).
- **`SignedAssertionFromManagedIdentity`** is **not** OIDC-FIC. Do not conflate them. Issuer is the tenant STS, not an external IdP. See [`glossary.md`](../glossary.md).
- **FIC subjects** are pinned to a single repo + ref (e.g. `repo:mghabin/entra-auth-patterns-dotnet:ref:refs/heads/main` or a specific environment). Never wildcard.
- **CI/CD doctrine** is infra-guide ch03/ch06. All third-party Actions pinned to commit SHA. All workflows pass markdownlint, actionlint, lychee, gitleaks, dependency-review, CodeQL, Scorecard, pinact.
- **Idle cost** is $0. ACA `minReplicas=0` for everything. No premium SKUs. Log Analytics has a daily cap. Teardown via `azd down` or `az group delete` is documented and clean.
- **Markdown** follows `.markdownlint.yaml`: ordered lists restart per section (MD029); no heading skips (MD001); `-` bullets only (MD004). Every doc has a `## Sources` block with primary citations.

## Workflow expectations

- Branch off `main`, push, open PR, watch checks, squash-merge with admin if and only if all checks pass. Never bypass without admin.
- Add/extend tests in `tests/` for any auth- or policy-affecting code change. The bar is the existing 51 passing tests.
- For docs: anchored cross-refs only. Update [`coverage-map.md`](../coverage-map.md) if a concept moves owners. Update [`glossary.md`](../glossary.md) if a new normative term appears.
- For infra: any new resource must declare `dependsOn` explicitly when ordering matters. Comment FIC idempotency hazards. No empty `appClientId` on warm re-runs.

## Co-authorship

Commits authored by an AI agent must include the trailer:

```
Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>
```

## When in doubt

Stop. Ask. Or: read [`DOCTRINE.md`](../DOCTRINE.md) again and pick the upstream owner.
