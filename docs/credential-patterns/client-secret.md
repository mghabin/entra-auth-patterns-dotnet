# Client secret (the anti-pattern)

**Don't use this.** Almost ever. This page exists so you recognise
the shape of the problem when reviewing legacy code.

## What it is

You generate a string ("secret"), paste it into the Entra app reg's
**Certificates & secrets** blade, copy it back out, and give it to
your workload. The workload includes it as `client_secret` in every
token request. Entra compares it to the stored hash and issues a
token if they match.

It's a shared password.

## Code (.NET)

```csharp
var credential = new ClientSecretCredential(
    tenantId, clientId,
    Environment.GetEnvironmentVariable("FTGO_CLIENTSECRET"));
```

## Why this is bad

1. **Shared symmetric secret.** Anyone with read access to the host's
   env vars / config / logs / process memory can mint tokens as your
   service.
2. **Rotation is a maintenance burden** that gets neglected. Most
   leaked-secret incidents involve a secret older than the engineer
   who left it.
3. **Limited TTL.** Entra caps client-secret lifetimes at **24 months
   maximum** (see [Microsoft Learn — App registration credential best
   practices](https://learn.microsoft.com/entra/identity-platform/app-registration-best-practices#client-secret-lifetime)),
   recently shortened from "no enforced cap." **Rotate at 6 months
   even if Entra permits longer** — the maximum exists to bound a
   leak, not to recommend long lifetimes. Forced rotation cycles
   regularly. With MI or FIC there's nothing to rotate.
4. **Audit story is bad.** A token request signed by a secret tells
   the audit log nothing about *which workload* made the request.
   The MI assertion or FIC subject claim does.
5. **Easy to commit by accident.** `git push` of `appsettings.json`
   with the secret still in it. The whole class of secret-scanning
   tooling exists because of this pattern.

## When it's actually OK

Three documented exceptions, in priority order. **If your case isn't on this list, you don't have an exception.**

- **Local dev** where the workload genuinely cannot use MI / FIC and
  doing so would block iteration. Prefer `DefaultAzureCredential` →
  `az login` first; secret only if that path is also unavailable.
- **Quick spike / proof-of-concept** code with a 1-hour TTL secret in
  a scratch tenant. Delete after.
- **Migrating a legacy system**: secret stays as the "before" while
  you build the cert/FIC/MI replacement; secret gets revoked the
  moment the migration cuts over.

### Hybrid runtime workloads (cloud + on-prem fallback)

If the same workload runs in cloud *and* falls back to on-prem (DR
site, edge POP, regulated tenant), **must not** share a single client
secret across environments to "simplify config." That collapses the
blast radius of a leak from one environment to all of them, and makes
rotation an all-or-nothing outage.

- **Should** use FIC where the runtime can present an OIDC token (the
  cloud half almost always can).
- **May** fall back to a certificate credential for the on-prem half
  (per [`cert.md`](cert.md)).
- **Must not** share secrets across cloud and on-prem. Different
  environments → different app registrations → different credentials.

## Why we used to ship this and don't anymore

The deleted `Ftgo.NotificationService` was deliberately built around
`ClientSecretCredential` to demonstrate the worst-case. Even with the
big "DON'T DO THIS" comment on it, having it deployed in cloud risked
new contributors copying the pattern. Removing it removes the
copy-paste vector. The pattern survives here as a reference.

If you find this pattern in your codebase, the migration path is:
1. Identify the workload's runtime (Azure compute → MI; non-Azure
   compute with OIDC → FIC; truly unable to do either → cert).
2. Add the new credential alongside the secret.
3. Cut over and revoke the secret in Entra.
4. Delete the env var, the secret-fetching code, and the secret
   from any KV / config store.

## Cross-references

- Acquisition wiring (last-resort path, library choices) — [`acquisition.md` §1d](../acquisition.md#1d-client-secret--last-resort).
- Receiver-side validation (`azp` allow-list applies regardless of credential) — [`validation.md` §4](../validation.md#4-app-token-specific-checks).
- Rotation as a cross-cutting operational concern — [`best-practices.md` — Credentials](../best-practices.md#credentials).
- Picker decision (when MI vs FIC vs cert vs secret) — [`index.md`](index.md#decision-matrix).
- Sibling credential patterns — [`managed-identity.md`](managed-identity.md), [`federated-identity.md`](federated-identity.md), [`cert.md`](cert.md).
- Auth-policy doctrine (delegated vs app-only) — dotnet-engineering-guide [ch02 §10](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz).

---

## Sources

- App registration credential best practices (24-month max, rotation) — [learn.microsoft.com/entra/identity-platform/app-registration-best-practices#client-secret-lifetime](https://learn.microsoft.com/entra/identity-platform/app-registration-best-practices#client-secret-lifetime)
- Microsoft Entra — Sign-in logs (audit trail for client-credentials grants) — [learn.microsoft.com/entra/identity/monitoring-health/concept-sign-ins](https://learn.microsoft.com/entra/identity/monitoring-health/concept-sign-ins)
- Microsoft Entra — Audit logs — [learn.microsoft.com/entra/identity/monitoring-health/concept-audit-logs](https://learn.microsoft.com/entra/identity/monitoring-health/concept-audit-logs)
- `ClientSecretCredential` reference — [learn.microsoft.com/dotnet/api/azure.identity.clientsecretcredential](https://learn.microsoft.com/dotnet/api/azure.identity.clientsecretcredential)
- GitHub — About secret scanning — [docs.github.com/code-security/secret-scanning/about-secret-scanning](https://docs.github.com/code-security/secret-scanning/about-secret-scanning)
- GitHub — Push protection for secrets — [docs.github.com/code-security/secret-scanning/push-protection-for-repositories-and-organizations](https://docs.github.com/code-security/secret-scanning/push-protection-for-repositories-and-organizations)
- OWASP — Secrets Management Cheat Sheet — [cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html](https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html)
- PCI DSS v4.0 — Requirement 3 (protect stored account data) and 8 (authentication) — [pcisecuritystandards.org/document_library/](https://www.pcisecuritystandards.org/document_library/)
- RFC 6749 §2.3.1 — Client Password (the symmetric-secret threat model) — [rfc-editor.org/rfc/rfc6749#section-2.3.1](https://www.rfc-editor.org/rfc/rfc6749#section-2.3.1)
