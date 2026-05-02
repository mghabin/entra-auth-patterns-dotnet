# Scope — who this guide is for, and who it is not for

This guide is opinionated.
The opinions only hold inside a specific technical and organisational envelope.
This page names that envelope so readers can decide, in 60 seconds, whether the rest of the guide will help them or mislead them.

If your situation falls outside this envelope, individual chapters may still be useful as background — but the *defaults* will be wrong for you, and you should treat the recommendations as input, not verdict.

The companion .NET engineering guide ([mghabin/dotnet-engineering-guide](https://github.com/mghabin/dotnet-engineering-guide)) owns the broader .NET 10 / ASP.NET Core / EF Core doctrine; this repo specialises that doctrine to **Microsoft Entra ID** authentication for server-side .NET ([learn.microsoft.com/entra/identity-platform/v2-overview](https://learn.microsoft.com/entra/identity-platform/v2-overview)).

---

## 1. Who it's for

Concrete reader profiles.
If two or more apply, you are the target reader.

- A senior or staff .NET backend engineer owning one or more long-lived ASP.NET Core services on .NET 10 that authenticate against Microsoft Entra ID.
- A solutions or application architect choosing the default token-acquisition library (Azure.Identity / MSAL.NET / Microsoft.Identity.Web) and the credential type (Managed Identity / Federated Identity Credential / certificate / secret) for a new bounded context ([learn.microsoft.com/entra/identity-platform/msal-overview](https://learn.microsoft.com/entra/identity-platform/msal-overview)).
- A platform / SRE engineer standardising server-side Entra validation, app-registration shape, and credential rotation across **5 or more** services in the same tenant ([learn.microsoft.com/entra/identity-platform/quickstart-register-app](https://learn.microsoft.com/entra/identity-platform/quickstart-register-app)).
- A staff engineer owning a shared `EntraAuth.*` library or "auth platform" and trying to stop every product team re-implementing JWT validation ([`docs/aks-shared-infra.md`](./docs/aks-shared-infra.md)).
- An engineer wiring a new service that must call other services on behalf of a signed-in user (OBO) or as an app-only worker, and choosing where the credential lives ([`docs/acquisition.md`](./docs/acquisition.md)).
- A security-minded reviewer enforcing the "no client secrets, no private keys on disk" baseline on Azure compute ([learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview](https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview)).

---

## 2. Organisational and technical shape it assumes

The defaults assume a *cloud-native .NET shop* with at least one shared auth surface.
Specifically:

- **.NET 10 GA** on the current LTS / STS cadence; ASP.NET Core 10 with `Microsoft.Identity.Web` as the default JWT-bearer pipeline ([learn.microsoft.com/entra/identity-platform/microsoft-identity-web](https://learn.microsoft.com/entra/identity-platform/microsoft-identity-web), [github.com/AzureAD/microsoft-identity-web](https://github.com/AzureAD/microsoft-identity-web)).
- **Microsoft Entra ID** (formerly Azure AD) as the workforce and workload identity provider; v2.0 endpoint for new app registrations ([learn.microsoft.com/entra/identity-platform/v2-overview](https://learn.microsoft.com/entra/identity-platform/v2-overview)).
- **Azure as the deployment target.** Azure Container Apps, AKS, App Service, Azure Functions on the isolated worker model — anywhere Managed Identity is available ([learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview](https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview)).
- **Multi-team estate**: at least 5 services, one shared auth library or platform team. Single-app shops can use the chapters but won't need [`docs/aks-shared-infra.md`](./docs/aks-shared-infra.md).
- **Multi-tenant SaaS or B2B preferred.** Single-tenant guidance is called out where defaults differ ([learn.microsoft.com/entra/identity-platform/howto-convert-app-to-be-multi-tenant](https://learn.microsoft.com/entra/identity-platform/howto-convert-app-to-be-multi-tenant)).
- **Engineering owns the app registrations.** App-reg lifecycle, role taxonomy, FIC creation, and rotation cadence are owned by the service team — not handed to a separate identity-ops group ([`docs/sample-setup.md`](./docs/sample-setup.md)).
- **CI/CD via GitHub Actions** with OIDC into Azure (no stored client secrets in GitHub) ([learn.microsoft.com/entra/workload-id/workload-identity-federation](https://learn.microsoft.com/entra/workload-id/workload-identity-federation)).

---

## 3. Who it's NOT for

- **Authors of general OAuth 2.0 / OIDC tutorials.** This guide is Entra-specific; it cites RFC 6749 ([rfc-editor.org/rfc/rfc6749](https://www.rfc-editor.org/rfc/rfc6749)) and OIDC Core ([openid.net/specs/openid-connect-core-1_0.html](https://openid.net/specs/openid-connect-core-1_0.html)) as substrate, not as the topic.
- **Consumer Microsoft account (MSA) developers.** The personal-account `consumers` authority and live.com sign-in patterns are out of scope ([learn.microsoft.com/entra/identity-platform/howto-convert-app-to-be-multi-tenant#who-can-sign-in-to-your-app](https://learn.microsoft.com/entra/identity-platform/howto-convert-app-to-be-multi-tenant)).
- **On-prem IIS / .NET Framework legacy estates.** WS-Federation, WIF, and `System.IdentityModel` on full framework are not covered.
- **Identity governance teams (IGA, access reviews, entitlement management).** That's [Microsoft Entra ID Governance](https://learn.microsoft.com/entra/id-governance/identity-governance-overview), a different product surface.
- **Mobile-client auth engineers.** MSAL on iOS/Android, broker integration, and device-bound tokens are out of scope; MSAL.NET on a mobile target is mentioned only as background.
- **Identity providers other than Entra ID.** Auth0, Okta, AWS Cognito, Google Identity, and Keycloak are not covered, even where the OIDC primitives overlap.

---

## 4. Explicit non-goals

These are deliberately out of scope.
Call them out so readers don't expect them.

- **Not a UI / client-side guide.** Browser sign-in UX, MSAL.js, MSAL on mobile, and Blazor WebAssembly token flows are out of scope; the API gateway in the sample validates browser-issued bearer tokens (in-browser Auth Code + PKCE is performed client-side by the Scalar UI), it does not run a server-side cookie session ([`docs/sample-setup.md`](./docs/sample-setup.md), [learn.microsoft.com/aspnet/core/blazor/security/server](https://learn.microsoft.com/aspnet/core/blazor/security/server)).
- **Not an IdP implementation guide.** This guide consumes Entra; it does not document how to build one (no OpenIddict, no IdentityServer, no Keycloak deployment).
- **Not Azure AD Connect / hybrid identity.** Directory sync, AD FS migration, password hash sync, and seamless SSO are out of scope ([learn.microsoft.com/entra/identity/hybrid](https://learn.microsoft.com/entra/identity/hybrid)).
- **Not compliance mapping (FedRAMP / HIPAA / PCI-DSS / SOC 2).** The guide names the Entra-level controls; mapping them to a specific control framework is downstream.
- **Not an Entra ID Governance manual.** Access reviews, entitlement management, lifecycle workflows, and PIM-for-groups are out of scope.
- **Not a Microsoft Graph SDK tutorial.** Calling Graph from a .NET app appears only as an example consumer of a token; deep Graph guidance lives at [learn.microsoft.com/graph/sdks/sdks-overview](https://learn.microsoft.com/graph/sdks/sdks-overview).

---

## 5. "If you are X, this guide is Y for you"

| Reader archetype                                                            | Recommendation                                                                                                                                                                                 |
| --------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Staff .NET engineer at a 200-person SaaS, multi-team, cloud-native on Azure | **Primary reader.** Read [`docs/decision-trees.md`](./docs/decision-trees.md) → SCOPE → [`docs/matrix.md`](./docs/matrix.md) → [`docs/best-practices.md`](./docs/best-practices.md).           |
| Platform / auth-library owner (`EntraAuth.*` shared NuGet, multi-product)   | **Primary reader.** Start at [`docs/aks-shared-infra.md`](./docs/aks-shared-infra.md), then [`docs/acquisition.md`](./docs/acquisition.md) and [`docs/validation.md`](./docs/validation.md).   |
| Architect picking credential type for a new service                         | **Primary reader.** [`docs/decision-trees.md`](./docs/decision-trees.md) Tree 2 + Tree 5 → [`docs/credential-patterns/index.md`](./docs/credential-patterns/index.md).                         |
| Solo developer, one app, pre-PMF                                            | **Skim only.** [`docs/run-locally.md`](./docs/run-locally.md) and [`docs/best-practices.md`](./docs/best-practices.md) are worth an hour; the platform / shared-infra chapters are premature.  |
| ASP.NET Core engineer needing JWT validation defaults only                  | **Targeted reader.** [`docs/validation.md`](./docs/validation.md) is the chapter; the rest is background.                                                                                      |
| Mobile / SPA developer                                                      | **Wrong guide.** Read MSAL.js or MSAL mobile docs first; come back when you own a server-side .NET API.                                                                                        |
| IGA / access-review engineer                                                | **Wrong guide.** Use [Microsoft Entra ID Governance](https://learn.microsoft.com/entra/id-governance/identity-governance-overview); this guide is for app developers, not directory operators. |
| Compliance / GRC reviewer mapping controls                                  | **Reference, not source of truth.** Use this as the engineering-side counterpart to your control catalog; mapping work is yours.                                                               |

---

## 6. Reading paths

Pick the path that matches why you opened the guide.
Each path is ordered; do not skip steps.

### 6.1 First 90 days on a new Entra-protected service

- Start at [`docs/decision-trees.md`](./docs/decision-trees.md) — the synthesis entrypoint that routes you to the chapter that owns each decision.
- Then this [`SCOPE.md`](./SCOPE.md) to confirm the envelope matches your situation.
- Then [`docs/matrix.md`](./docs/matrix.md) for the scenario-by-scenario decision table.
- Then [`docs/best-practices.md`](./docs/best-practices.md) as the one-page review card.
- Then [`docs/acquisition.md`](./docs/acquisition.md) and [`docs/validation.md`](./docs/validation.md) for the load-bearing chapters.

### 6.2 Deep-dive (read everything)

- All of [`docs/`](./docs/) in the order listed in [`coverage-map.md`](./coverage-map.md): acquisition, validation, matrix, best-practices, credential-patterns, aks-shared-infra, sample-setup, run-locally, environments, deploy-cloud, operations.

### 6.3 Implementer (running the sample)

- [`docs/sample-setup.md`](./docs/sample-setup.md) — app registrations, role taxonomy, audience model.
- [`docs/run-locally.md`](./docs/run-locally.md) — `az login` + `DefaultAzureCredential` against the cloud APIs.
- [`docs/deploy-cloud.md`](./docs/deploy-cloud.md) — CI/CD, OIDC, image promotion, free-tier ACA deployment.
- [`docs/operations.md`](./docs/operations.md) — what-if previews, branch-protection, "on fire" runbook.

---

## 7. Out-of-scope topics with redirects

If your question lives in one of these areas, the right pointer is named below.

- **Consumer Microsoft account (MSA) sign-in** → [Microsoft account documentation](https://learn.microsoft.com/entra/identity-platform/howto-convert-app-to-be-multi-tenant) and [the `consumers` authority](https://learn.microsoft.com/entra/identity-platform/v2-protocols).
- **Identity governance, access reviews, entitlement management** → [Microsoft Entra ID Governance](https://learn.microsoft.com/entra/id-governance/identity-governance-overview).
- **Hybrid identity, AD FS, Azure AD Connect** → [Microsoft Entra hybrid identity](https://learn.microsoft.com/entra/identity/hybrid).
- **MSAL on mobile / SPA / desktop clients** → [MSAL overview](https://learn.microsoft.com/entra/identity-platform/msal-overview) and per-platform MSAL docs.
- **Microsoft Graph API surface** → [Microsoft Graph documentation](https://learn.microsoft.com/graph/overview).
- **Conditional Access policy authoring** (admin side) → [Conditional Access documentation](https://learn.microsoft.com/entra/identity/conditional-access/overview); this guide only covers the *app-side* CAE / claims-challenge contract ([`docs/validation.md`](./docs/validation.md) §6).
- **PIM, privileged role assignments** → [Microsoft Entra Privileged Identity Management](https://learn.microsoft.com/entra/id-governance/privileged-identity-management/pim-configure).
- **B2C / External Identities / CIAM** → [Microsoft Entra External ID](https://learn.microsoft.com/entra/external-id/external-identities-overview); the workforce-tenant patterns here partially apply but the policy / branding surface is different.
- **General .NET / ASP.NET Core / EF Core doctrine** → [`mghabin/dotnet-engineering-guide`](https://github.com/mghabin/dotnet-engineering-guide). Auth-policy shape (delegated vs app-only) lives in [ch02 §10](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz) and is mirrored here.

---

## 8. Maintenance posture

- **Targets the current GA Entra v2.0 endpoint and Microsoft.Identity.Web on .NET 10.** When .NET 11 ships, guidance moves with it; the previous LTS is called out inline only where defaults differ.
- **Pre-GA features are labeled.** Anything in preview (CAE preview surfaces, ACRS preview) is marked `preview` inline and is not a default.
- **Citations are primary-source.** Microsoft Learn (`learn.microsoft.com/entra/*`), the IETF RFCs ([6749](https://www.rfc-editor.org/rfc/rfc6749), [7519](https://www.rfc-editor.org/rfc/rfc7519), [8693](https://www.rfc-editor.org/rfc/rfc8693)), the [OIDC Core 1.0](https://openid.net/specs/openid-connect-core-1_0.html) spec, and the official Microsoft GitHub repos for [Microsoft.Identity.Web](https://github.com/AzureAD/microsoft-identity-web), [MSAL.NET](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet), and [Azure SDK / Azure.Identity](https://github.com/Azure/azure-sdk-for-net) are the only acceptable sources for a normative claim.
- **No Medium posts, random blogs, or YouTube** count as a primary source.

---

## Sources

- Microsoft identity platform overview — [learn.microsoft.com/entra/identity-platform/v2-overview](https://learn.microsoft.com/entra/identity-platform/v2-overview)
- Managed identities for Azure resources — [learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview](https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview)
- Workload identity federation — [learn.microsoft.com/entra/workload-id/workload-identity-federation](https://learn.microsoft.com/entra/workload-id/workload-identity-federation)
- Microsoft.Identity.Web — [learn.microsoft.com/entra/identity-platform/microsoft-identity-web](https://learn.microsoft.com/entra/identity-platform/microsoft-identity-web), [github.com/AzureAD/microsoft-identity-web](https://github.com/AzureAD/microsoft-identity-web)
- MSAL.NET — [learn.microsoft.com/entra/identity-platform/msal-overview](https://learn.microsoft.com/entra/identity-platform/msal-overview), [github.com/AzureAD/microsoft-authentication-library-for-dotnet](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet)
- RFC 6749 — The OAuth 2.0 Authorization Framework — [rfc-editor.org/rfc/rfc6749](https://www.rfc-editor.org/rfc/rfc6749)
- RFC 7519 — JSON Web Token (JWT) — [rfc-editor.org/rfc/rfc7519](https://www.rfc-editor.org/rfc/rfc7519)
- OpenID Connect Core 1.0 — [openid.net/specs/openid-connect-core-1_0.html](https://openid.net/specs/openid-connect-core-1_0.html)
