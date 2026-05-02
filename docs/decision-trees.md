# Decision Trees — Entra Auth One-Hour Synthesis

This file summarises and routes; canonical wording lives in [`../docs/`](../docs/) (acquisition, validation, credential-patterns) and in the dotnet-engineering-guide [ch02 §10](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz).
Where this file and a chapter disagree, the chapter wins.

This page collects the highest-leverage decisions a .NET / platform engineer actually makes when designing or reviewing a Microsoft Entra ID integration on a new or migrating server-side service.
Each tree gives the trigger, the cost of the wrong call, the Mermaid flow, and a pointer back to the owning chapter — go there before you argue with the tree.

---

## 1. Who is calling? — user token + OBO vs app token

- Trigger: a new endpoint, or a new outbound call from a service to another Entra-protected API.
- Cost of wrong call: a daemon that ships a user's refresh token (no user is present, the token will expire and the worker will silently break); or a user-facing endpoint that demands an app-only token (every interactive call now needs an app reg per browser, which doesn't make sense).
- Default per [`acquisition.md`](../docs/acquisition.md): if a *user* is on the wire, acquire a delegated user token on the client (auth-code + PKCE) and use *OBO* on the middle tier. If no user is on the wire, acquire an app-only token via `client_credentials`.

```mermaid
flowchart TD
    A[New call to Entra-protected API] --> B{Is a signed-in user on the wire?}
    B -->|Yes, interactive client → API| C[Delegated USER token<br/>auth-code + PKCE on the client — acquisition.md §2]
    C --> D{This API needs to call<br/>a downstream API as the user?}
    D -->|Yes| E[On-Behalf-Of OBO<br/>middle tier exchanges user token — acquisition.md §2b]
    D -->|No| F[Validate user token<br/>scp + audience — validation.md §2]
    B -->|No, daemon / worker / cron| G[App-only token<br/>client_credentials — acquisition.md §1]
    G --> H[Validate app token<br/>roles + azp allow-list — validation.md §4]
```

References: [`docs/acquisition.md#1-app-tokens-s2s-daemons-workers`](../docs/acquisition.md#1-app-tokens-s2s-daemons-workers), [`docs/acquisition.md#2-user-tokens-delegated`](../docs/acquisition.md#2-user-tokens-delegated), [`docs/acquisition.md#2b-calling-a-downstream-api-as-that-user-obo`](../docs/acquisition.md#2b-calling-a-downstream-api-as-that-user-obo).

---

## 2. Where does the workload run? — credential type per host

- Trigger: a new deployable that needs to acquire an Entra token.
- Cost of wrong call: a client secret in App Service config that nobody rotates; a cert on disk on a node that nobody pinned the rotation cadence on; or burning a week wiring a Service Principal password into GitHub Actions when *FIC* would have removed the secret entirely.
- Default per [`credential-patterns/index.md`](../docs/credential-patterns/index.md): on Azure compute, *Managed Identity*. On a non-Azure platform that issues OIDC tokens (GitHub Actions, GitLab, Kubernetes), *Federated Identity Credential*. On a portable host with a hardware credential, certificate. Client secret only as a documented exception.

```mermaid
flowchart TD
    A[New workload acquiring an Entra token] --> B{Where does it run?}
    B -->|Azure compute<br/>App Service / ACA / AKS / VM / Functions| C[Managed Identity — credential-patterns/managed-identity.md]
    B -->|GitHub Actions / GitLab CI / K8s SA<br/>OIDC issuer available| D[Federated Identity Credential FIC — credential-patterns/federated-identity.md]
    B -->|Other cloud / on-prem / portable| E{Hardware-protected<br/>cert available?}
    E -->|Yes| F[Certificate credential — credential-patterns/cert.md]
    E -->|No| G[Client secret — last resort<br/>document the exception<br/>rotate aggressively — credential-patterns/client-secret.md]
```

References: [`docs/credential-patterns/index.md#decision-matrix`](../docs/credential-patterns/index.md#decision-matrix), [`docs/credential-patterns/managed-identity.md`](../docs/credential-patterns/managed-identity.md), [`docs/credential-patterns/federated-identity.md`](../docs/credential-patterns/federated-identity.md), [`docs/credential-patterns/cert.md`](../docs/credential-patterns/cert.md), [`docs/credential-patterns/client-secret.md`](../docs/credential-patterns/client-secret.md).

---

## 3. Single-tenant or multi-tenant API?

- Trigger: standing up a new Entra-protected API and choosing the audience model.
- Cost of wrong call: a single-tenant API that silently accepts tokens from any tenant because validation defaults to "trust the issuer Entra hands me"; or a multi-tenant API that crashes at first foreign-tenant sign-in because `IssuerValidator` was never configured.
- Default per [`validation.md`](../docs/validation.md) §3: if you serve exactly one tenant, pin `TenantId` to that GUID. If you serve more than one, set `TenantId="organizations"`, register `AadIssuerValidator`, and gate on a `tid` allow-list. **Never** accept `common` without a `tid` allow-list — `common` permits personal Microsoft accounts unless the app reg's `signInAudience` excludes them.

```mermaid
flowchart TD
    A[New Entra-protected API] --> B{One tenant or many?}
    B -->|Exactly one tenant| C[Pin TenantId to the tenant GUID<br/>default JwtBearer validation is enough — validation.md §3]
    B -->|More than one tenant| D[TenantId = organizations<br/>+ register AadIssuerValidator<br/>+ tid allow-list — validation.md §3]
    D --> E{New tenant onboards?}
    E -->|Yes| F[Add tid to allow-list<br/>require admin consent in the new tenant — validation.md §3]
    E -->|No| G[Reject token<br/>HTTP 401 with diagnostic — validation.md §3]
```

References: [`docs/validation.md#3-issuer-audience-v1-vs-v2-single-vs-multi-tenant`](../docs/validation.md#3-issuer-audience-v1-vs-v2-single-vs-multi-tenant).

---

## 4. Does this endpoint accept delegated, app-only, or both?

- Trigger: protecting a new endpoint that may be hit by signed-in users (`scp`), app-only callers (`roles` + `azp`), or both.
- Cost of wrong call: a single OR-claims policy that lets an app-only token reach a user-only endpoint (no `azp` allow-list, no `scp` check), or a user token satisfy an app-only endpoint by carrying an unrelated `scp`. Both are real privilege-escalation bugs in production APIs. Mirrors dotnet-guide [ch02 §10](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz).
- Default per [`validation.md`](../docs/validation.md) §4 + dotnet-guide ch02 §10: **two separate named policies on two separate authorisation attributes**, never an OR-claims policy. Delegated → `[Authorize] + [RequiredScope("orders.read")]`. App-only → `[Authorize(Policy = OrdersAuthorizationPolicies.App)]` (one named policy combining role + `azp` allow-list + `scp`-rejection). Both → list both attributes; each enforces its own invariants. **Never `[Authorize(Roles=...)]`** — it silently no-ops on `MapInboundClaims=false` schemes; see [DOCTRINE.md](../DOCTRINE.md#authorization-policy-doctrine-canonical).

```mermaid
flowchart TD
    A[New protected endpoint] --> B{Caller identity model?}
    B -->|Delegated user only| C["[Authorize] + [RequiredScope(scope)]<br/>reject if roles present without scp — validation.md §4"]
    B -->|App-only / daemon only| D["[Authorize(Policy=*App)]<br/>via AddAppPolicy: role + azp allow-list<br/>+ reject if scp present — validation.md §4"]
    B -->|Both flows in scope| E[Two separate named policies<br/>on two separate authorizations<br/>NEVER one OR-claims policy — validation.md §4]
    C --> F[Each request: presence of scp<br/>+ specific scope value — validation.md §4]
    D --> G[Each request: presence of roles<br/>+ azp / appid in allow-list<br/>+ absence of scp — validation.md §4]
    E --> H[Document which paths each flow may reach;<br/>each policy enforces its own invariants — validation.md §4]
```

References: [`docs/validation.md#4-app-token-specific-checks`](../docs/validation.md#4-app-token-specific-checks), dotnet-engineering-guide [`docs/02-aspnetcore.md#10-authnauthz`](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz).

---

## 5. Can you use MI / FIC, or do you need cert / secret?

- Trigger: revisiting credential type during a credential-rotation event, a new environment, or an architecture review.
- Cost of wrong call: shipping a client secret to a workload that *could* use Managed Identity ("we'll fix it later" never happens); or putting a long-lived cert on a Container App that can do MI for free.
- Default per [`credential-patterns/index.md`](../docs/credential-patterns/index.md): MI on Azure compute. FIC if an OIDC issuer exists on the platform. Cert if you must run somewhere with a hardware-protected key store. Secret only as a written, rotated, time-boxed exception.

```mermaid
flowchart TD
    A[Need to authenticate as an app] --> B{On Azure compute?}
    B -->|Yes| C[Managed Identity UAMI<br/>preferred over SAMI in shared infra — credential-patterns/managed-identity.md]
    B -->|No| D{Platform issues OIDC tokens?<br/>GitHub Actions / GitLab / K8s SA}
    D -->|Yes| E[Federated Identity Credential FIC<br/>RFC 8693 token exchange — credential-patterns/federated-identity.md]
    D -->|No| F{Hardware-protected key<br/>HSM / TPM / smart card?}
    F -->|Yes| G[Certificate credential<br/>EphemeralKeySet, rotation cadence — credential-patterns/cert.md]
    F -->|No| H[Client secret — last resort<br/>three documented exceptions only<br/>rotate aggressively — credential-patterns/client-secret.md]
    C --> I{BFF that mints<br/>client_assertion via MI?}
    I -->|Yes| J["SignedAssertionFromManagedIdentity<br/>NOT OIDC-FIC — different mechanism — credential-patterns/federated-identity.md"]
    I -->|No| K[Done — credential-patterns/managed-identity.md]
```

References: [`docs/credential-patterns/index.md#decision-matrix`](../docs/credential-patterns/index.md#decision-matrix), [`docs/credential-patterns/managed-identity.md`](../docs/credential-patterns/managed-identity.md), [`docs/credential-patterns/federated-identity.md`](../docs/credential-patterns/federated-identity.md), [`docs/credential-patterns/cert.md`](../docs/credential-patterns/cert.md), [`docs/credential-patterns/client-secret.md`](../docs/credential-patterns/client-secret.md).

---

## Closing rule

Every tree above picks a side.
Disagree by reading the cited chapter and finding a different rule there — not by adding a branch.
This page is synthesis, not a menu.

Cross-links to companion guides:

- General .NET auth-policy doctrine (delegated vs app-only, no OR-claims policies) — dotnet-engineering-guide [`docs/02-aspnetcore.md#10-authnauthz`](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz).
- Workload identity at the infrastructure layer (no static credentials, OIDC from CI, SPIFFE comparison) — infra-engineering-guide [`docs/06-security-supply-chain.md#3-workload-identity--the-no-static-credentials-rule`](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/06-security-supply-chain.md#3-workload-identity--the-no-static-credentials-rule).

---

## Sources

- Microsoft identity platform overview — [learn.microsoft.com/entra/identity-platform/v2-overview](https://learn.microsoft.com/entra/identity-platform/v2-overview)
- Access token claims reference — [learn.microsoft.com/entra/identity-platform/access-token-claims-reference](https://learn.microsoft.com/entra/identity-platform/access-token-claims-reference)
- On-Behalf-Of flow — [learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow](https://learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow)
- Managed identities for Azure resources — [learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview](https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview)
- Workload identity federation — [learn.microsoft.com/entra/workload-id/workload-identity-federation](https://learn.microsoft.com/entra/workload-id/workload-identity-federation)
- Microsoft.Identity.Web — multi-tenant web APIs — [github.com/AzureAD/microsoft-identity-web/wiki/multi-tenant-web-apis](https://github.com/AzureAD/microsoft-identity-web/wiki/multi-tenant-web-apis)
- App roles — [learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps](https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps)
- RFC 6749 — The OAuth 2.0 Authorization Framework — [rfc-editor.org/rfc/rfc6749](https://www.rfc-editor.org/rfc/rfc6749)
- RFC 7519 — JSON Web Token (JWT) — [rfc-editor.org/rfc/rfc7519](https://www.rfc-editor.org/rfc/rfc7519)
- RFC 8693 — OAuth 2.0 Token Exchange — [rfc-editor.org/rfc/rfc8693](https://www.rfc-editor.org/rfc/rfc8693)
- OpenID Connect Core 1.0 — [openid.net/specs/openid-connect-core-1_0.html](https://openid.net/specs/openid-connect-core-1_0.html)
