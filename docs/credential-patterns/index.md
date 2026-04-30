# Credential patterns for Entra ID

When a workload acquires a token from Microsoft Entra to call a protected
API, it has to prove its identity. The "credential pattern" is *how* it
proves that identity. Picking the right one is the single most important
operational decision in any Entra-integrated app.

This sample is opinionated: **on Azure compute, use Managed Identity.
For everything else, use Workload Identity Federation. Avoid certs and
client secrets unless you genuinely cannot avoid them.** This mirrors
the dotnet-engineering-guide [ch02 §10 auth-policy doctrine](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz):
the credential type is part of the policy, not an operational detail.

## Decision matrix

| Where does your workload run?               | Credential                                                                                                                                             |
|---------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------|
| Azure (App Service, ACA, AKS, Functions, …) | **must** use [Managed Identity](managed-identity.md)                                                                                                   |
| GitHub Actions                              | **must** use [Workload Identity Federation](federated-identity.md)                                                                                     |
| GKE / EKS / on-prem K8s with SPIFFE / OIDC  | **must** use [Workload Identity Federation](federated-identity.md)                                                                                     |
| On-prem service with no IdP / HSM-bound key | **should** use [Certificate](cert.md)                                                                                                                  |
| Anything else                               | you almost certainly don't need a [client secret](client-secret.md) — see that page for the three documented exceptions; **strongly prefer** MI or FIC |

Cross-references:

- The token-acquisition wiring (which library, which `TokenCredential`,
  caching) lives in [`acquisition.md`](../acquisition.md) §1.
- The validation side (what the *receiving* API checks on these tokens
  — `azp` allow-list, `roles`, MI `idtyp`) lives in
  [`validation.md`](../validation.md) §4 and §5.
- Per-scenario picker (which credential for which sample service) lives
  in [`matrix.md`](../matrix.md); the credential rule is owned here per
  [`coverage-map.md`](../../coverage-map.md).

## Why this sample stopped using credential menagerie services

Earlier revisions of this sample shipped four worker services
(`AccountingService`, `DeliveryService`, `NotificationService`) — one per credential type — to teach the patterns
side-by-side. We deleted three of them after realising they were
either broken in cloud (FIC env vars don't exist on Azure Container
Apps; secret env var was never set) or had dead env-var wiring (the
worker code never read the `AzureAd:ClientCredentials` config we
populated; it had its own custom `IAppTokenProvider`).

The kept worker, **`Ftgo.Kitchen.Worker`**, uses the canonical Azure
pattern: `ManagedIdentityCredential` with `api://orders/.default`,
admin-consented at the MI's service principal. It is the credential
pattern any new Azure-resident worker should follow.

The other three patterns survive as 1-page references here.

The rationale for deleting the deployed services (rather than keeping
them as a "menagerie") is recorded in the commit history of the
`docs(cred-patterns)` series and in the per-page "Why we used to ship
this and don't anymore" sections of [`cert.md`](cert.md#why-we-used-to-ship-this-and-dont-anymore)
and [`client-secret.md`](client-secret.md#why-we-used-to-ship-this-and-dont-anymore).
The rule the sample now enforces: every deployed service in this repo
demonstrates a credential pattern that a new Azure workload should
copy. Patterns that exist only as warnings live in docs, not in
deployed code.

---

## Sources

- Microsoft Entra credential types — application authentication overview — [learn.microsoft.com/entra/identity-platform/authentication-flows-app-scenarios](https://learn.microsoft.com/entra/identity-platform/authentication-flows-app-scenarios)
- Managed identities for Azure resources — overview — [learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview](https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview)
- Workload identity federation — [learn.microsoft.com/entra/workload-id/workload-identity-federation](https://learn.microsoft.com/entra/workload-id/workload-identity-federation)
- Certificate credentials on app registrations — [learn.microsoft.com/entra/identity-platform/certificate-credentials](https://learn.microsoft.com/entra/identity-platform/certificate-credentials)
- OWASP — Secrets Management Cheat Sheet — [cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html](https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html)
- OWASP — Cryptographic Storage Cheat Sheet — [cheatsheetseries.owasp.org/cheatsheets/Cryptographic_Storage_Cheat_Sheet.html](https://cheatsheetseries.owasp.org/cheatsheets/Cryptographic_Storage_Cheat_Sheet.html)
- NIST SP 800-57 Part 1 Rev. 5 — Recommendation for Key Management — [nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-57pt1r5.pdf](https://nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-57pt1r5.pdf)
- NIST SP 800-63B — Digital Identity Guidelines (authenticator types) — [pages.nist.gov/800-63-3/sp800-63b.html](https://pages.nist.gov/800-63-3/sp800-63b.html)
- RFC 8693 — OAuth 2.0 Token Exchange — [rfc-editor.org/rfc/rfc8693](https://www.rfc-editor.org/rfc/rfc8693)
- dotnet-engineering-guide — ch02 §10 (auth-policy doctrine) — [github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz)
