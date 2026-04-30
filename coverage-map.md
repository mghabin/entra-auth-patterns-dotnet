<!-- markdownlint-disable MD024 -->
# Coverage map — who owns what across the Entra auth guide

Companion to [`README.md`](./README.md), [`SCOPE.md`](./SCOPE.md), and [`docs/best-practices.md`](./docs/best-practices.md).
Every concept is owned by exactly one chapter.
Sibling chapters reference (link) the owner; they do not re-decide.
This page is the master "who owns what" matrix — if two chapters look like they overlap, the [conflict-resolution](#conflict-resolution) rule names the owner.

Primary sources for ownership: the chapters themselves under [`docs/`](./docs/), [Microsoft.Identity.Web](https://learn.microsoft.com/entra/identity-platform/microsoft-identity-web), [Microsoft Entra app roles](https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps), and the underlying [Microsoft identity platform](https://learn.microsoft.com/entra/identity-platform/v2-overview) docs.

---

## Conflict resolution

- If two chapters appear to overlap, the **acquisition / validation pair owns the protocol surface**, and the **operational chapters own the deployment surface**.
- Example: [`acquisition.md`](./docs/acquisition.md) owns the *credential picker for token requests*; [`credential-patterns/`](./docs/credential-patterns/) owns the *credential type itself* (MI vs FIC vs cert vs secret). Acquisition links to credential-patterns, never re-derives it.
- Example: [`validation.md`](./docs/validation.md) owns *how a server validates a JWT*; [`best-practices.md`](./docs/best-practices.md) restates the rule in checklist form but never adds new doctrine.
- Example: [`matrix.md`](./docs/matrix.md) owns the *scenario-by-scenario picker*; it links to acquisition / validation / credential-patterns and never re-decides any cell.
- Example: [`sample-setup.md`](./docs/sample-setup.md) owns *this sample's* app registrations and role taxonomy; [`deploy-cloud.md`](./docs/deploy-cloud.md) owns *how those registrations get into dev / ppe / prod*.
- A chapter may **demonstrate** a deferred concept in a code sample, but it must not redefine the rule. The rule lives in the owner.

---

## Ownership matrix

One row per concept. The Owner is the single source of truth; siblings link, never re-decide.

| Concept | Owner | Sibling references |
|---|---|---|
| App tokens (S2S / daemon) — `client_credentials`, MI, FIC, cert, secret | [`acquisition.md`](./docs/acquisition.md) §1 | [`matrix.md`](./docs/matrix.md), [`credential-patterns/index.md`](./docs/credential-patterns/index.md), [`best-practices.md`](./docs/best-practices.md) |
| User tokens (delegated) — auth-code + PKCE on the client, validation on the API | [`acquisition.md`](./docs/acquisition.md) §2 | [`validation.md`](./docs/validation.md), [`matrix.md`](./docs/matrix.md) |
| On-Behalf-Of (OBO) flow | [`acquisition.md`](./docs/acquisition.md) §2b | [`matrix.md`](./docs/matrix.md), [`sample-setup.md`](./docs/sample-setup.md) |
| Token caching (MSAL / Microsoft.Identity.Web in-memory + distributed) | [`acquisition.md`](./docs/acquisition.md) | [`best-practices.md`](./docs/best-practices.md), [`aks-shared-infra.md`](./docs/aks-shared-infra.md) |
| Library picks: Azure.Identity vs MSAL.NET vs Microsoft.Identity.Web | [`acquisition.md`](./docs/acquisition.md) §1e | [`README.md`](./README.md) cheat-sheet, [`matrix.md`](./docs/matrix.md) |
| JWT signature, audience, issuer, lifetime validation | [`validation.md`](./docs/validation.md) §1 | [`best-practices.md`](./docs/best-practices.md), [`matrix.md`](./docs/matrix.md) |
| `scp` (delegated scope) vs `roles` (app role) split | [`validation.md`](./docs/validation.md) §2 | [`matrix.md`](./docs/matrix.md), [`best-practices.md`](./docs/best-practices.md), dotnet-guide [ch02 §10](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz) |
| v1 vs v2 audiences, token-version detection | [`validation.md`](./docs/validation.md) §3 | [`acquisition.md`](./docs/acquisition.md), [`sample-setup.md`](./docs/sample-setup.md) |
| Multi-tenant `IssuerValidator` (`AadIssuerValidator`) + `tid` allow-list | [`validation.md`](./docs/validation.md) §3 | [`best-practices.md`](./docs/best-practices.md), [`matrix.md`](./docs/matrix.md) |
| App-token `azp` / `appid` allow-list (per-client gate) | [`validation.md`](./docs/validation.md) §4 | [`best-practices.md`](./docs/best-practices.md), dotnet-guide [ch02 §10](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz) |
| CAE (Continuous Access Evaluation) and ACRS (claims challenges) wiring | [`validation.md`](./docs/validation.md) §6 | [`best-practices.md`](./docs/best-practices.md) |
| One-page numbered checklist (distillation only; no new doctrine) | [`best-practices.md`](./docs/best-practices.md) | [`README.md`](./README.md), [`matrix.md`](./docs/matrix.md) |
| Scenario-by-scenario decision table | [`matrix.md`](./docs/matrix.md) | All other chapters link here for "which scenario am I in?" |
| Credential picker (MI / FIC / cert / secret) | [`credential-patterns/index.md`](./docs/credential-patterns/index.md) | [`acquisition.md`](./docs/acquisition.md), [`matrix.md`](./docs/matrix.md), [`best-practices.md`](./docs/best-practices.md) |
| Managed Identity — UAMI vs SAMI, IMDS endpoint, `DefaultAzureCredential` | [`credential-patterns/managed-identity.md`](./docs/credential-patterns/managed-identity.md) | [`acquisition.md`](./docs/acquisition.md), [`run-locally.md`](./docs/run-locally.md), [`deploy-cloud.md`](./docs/deploy-cloud.md) |
| Federated Identity Credential (FIC), RFC 8693 Token Exchange, GitHub OIDC subject pinning, `SignedAssertionFromManagedIdentity` (clarified as **not** OIDC-FIC) | [`credential-patterns/federated-identity.md`](./docs/credential-patterns/federated-identity.md) | [`deploy-cloud.md`](./docs/deploy-cloud.md), [`sample-setup.md`](./docs/sample-setup.md), [`acquisition.md`](./docs/acquisition.md) |
| Certificate credential — when acceptable, `EphemeralKeySet`, rotation cadence | [`credential-patterns/cert.md`](./docs/credential-patterns/cert.md) | [`best-practices.md`](./docs/best-practices.md), [`matrix.md`](./docs/matrix.md) |
| Client secret — anti-pattern + the three documented exceptions | [`credential-patterns/client-secret.md`](./docs/credential-patterns/client-secret.md) | [`best-practices.md`](./docs/best-practices.md), [`matrix.md`](./docs/matrix.md) |
| Shared platform doctrine for N services × M products on AKS | [`aks-shared-infra.md`](./docs/aks-shared-infra.md) | [`SCOPE.md`](./SCOPE.md), [`best-practices.md`](./docs/best-practices.md) |
| This sample's app registrations, role taxonomy, audience model | [`sample-setup.md`](./docs/sample-setup.md) | [`run-locally.md`](./docs/run-locally.md), [`deploy-cloud.md`](./docs/deploy-cloud.md), [`acquisition.md`](./docs/acquisition.md) |
| CI/CD, image promotion by SHA, per-env provisioning | [`deploy-cloud.md`](./docs/deploy-cloud.md) | [`environments.md`](./docs/environments.md), [`operations.md`](./docs/operations.md) |
| Operations runbook — what-if previews, branch protection, env teardown, "on fire" | [`operations.md`](./docs/operations.md) | [`deploy-cloud.md`](./docs/deploy-cloud.md), [`environments.md`](./docs/environments.md) |
| Local development — `az login` + `DefaultAzureCredential`, OIDC sign-in browser flow | [`run-locally.md`](./docs/run-locally.md) | [`sample-setup.md`](./docs/sample-setup.md), [`credential-patterns/managed-identity.md`](./docs/credential-patterns/managed-identity.md) |
| Promotion model (dev → ppe → prod), per-env config, production guardrails | [`environments.md`](./docs/environments.md) | [`deploy-cloud.md`](./docs/deploy-cloud.md), [`operations.md`](./docs/operations.md) |

---

## Cross-cutting concerns and their owner chapter

When a topic surfaces in more than one chapter, exactly one chapter owns it.
The others reference; they do not re-decide.

| Concern | Owner | Why it lives there |
|---|---|---|
| Auth policy shape (delegated `scp` vs app-only `roles` + `azp`) | [`validation.md`](./docs/validation.md) §2 + §4 | The validation pipeline is where the policy is enforced; mirrors dotnet-guide [ch02 §10](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz). |
| Multi-tenant validation (`organizations` authority + `IssuerValidator` + `tid` allow-list) | [`validation.md`](./docs/validation.md) §3 | Single decision point; both signature and tenant gating are here. |
| Credential type per host (Azure compute → MI; OIDC issuer → FIC; portable hardware → cert) | [`credential-patterns/index.md`](./docs/credential-patterns/index.md) | The picker is the single owner; per-credential pages own the *how*. |
| `SignedAssertionFromManagedIdentity` (BFF token-exchange) — **not** OIDC-FIC | [`credential-patterns/federated-identity.md`](./docs/credential-patterns/federated-identity.md) | Same chapter as FIC but explicitly disambiguates the two — mistaking them swaps the credential model. |
| Token cache backing store (in-memory vs distributed Redis vs SQL) | [`acquisition.md`](./docs/acquisition.md) | The cache is part of how a token is acquired; sibling chapters link here. |
| App registration shape, audience, role taxonomy for *this sample* | [`sample-setup.md`](./docs/sample-setup.md) | Sample-specific; the doctrine for "what an app reg should look like in general" lives in [`acquisition.md`](./docs/acquisition.md) and [`validation.md`](./docs/validation.md). |
| Per-env app-reg provisioning and FIC creation in CI | [`deploy-cloud.md`](./docs/deploy-cloud.md) | Operational; the *credential type* doctrine is owned by credential-patterns. |
| Local dev credential model (`DefaultAzureCredential` chain) | [`run-locally.md`](./docs/run-locally.md) | Operational; the *credential type* doctrine is owned by credential-patterns. |

---

## Rule

> Every doctrine bullet appears in exactly one owner; siblings link, never re-decide.

If you find yourself writing the same `must` / `should` / `avoid` rule in two chapters, one of them is wrong.
Open a PR that deletes the duplicate and replaces it with a link to the owner.

---

## Sources

- Microsoft.Identity.Web — [learn.microsoft.com/entra/identity-platform/microsoft-identity-web](https://learn.microsoft.com/entra/identity-platform/microsoft-identity-web)
- Microsoft.Identity.Web (repo + samples) — [github.com/AzureAD/microsoft-identity-web](https://github.com/AzureAD/microsoft-identity-web)
- Add app roles to your application — [learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps](https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps)
- Access token claims reference — [learn.microsoft.com/entra/identity-platform/access-token-claims-reference](https://learn.microsoft.com/entra/identity-platform/access-token-claims-reference)
- ID token claims reference — [learn.microsoft.com/entra/identity-platform/id-token-claims-reference](https://learn.microsoft.com/entra/identity-platform/id-token-claims-reference)
- Microsoft identity platform overview — [learn.microsoft.com/entra/identity-platform/v2-overview](https://learn.microsoft.com/entra/identity-platform/v2-overview)
