# Workload Identity Federation (FIC)

**Use this when your workload runs outside Azure but on a platform
that issues OIDC tokens.** Examples: GitHub Actions, GitLab Pipelines,
Bitbucket Pipelines, GKE, EKS, on-prem Kubernetes with SPIFFE.

## What it is

You register a **federated identity credential** on an Entra app reg
that says: "trust any OIDC token whose issuer is `<your platform>`
and whose subject is `<your specific workload>`." Then your workload
presents that OIDC token to Entra and gets back an access token.
**No secret crosses the wire.**

## Code (GitHub Actions ↔ Azure)

`.github/workflows/wi-demo.yml` (the live example in this repo):

```yaml
permissions:
  id-token: write   # let GHA mint OIDC tokens

steps:
  - uses: azure/login@v3
    with:
      tenant-id: ${{ secrets.AZURE_TENANT_ID }}
      client-id: ${{ secrets.AZURE_CLIENT_ID }}   # the FTGO Entra app reg
      allow-no-subscriptions: true

  - run: az account get-access-token --resource api://restaurants/.default
```

Behind the scenes:
1. `azure/login` calls `actions/get-id-token` to mint an OIDC JWT
   issued by `https://token.actions.githubusercontent.com` with
   subject `repo:owner/repo:ref:refs/heads/main` (or similar).
2. It exchanges that JWT at `login.microsoftonline.com` for an Entra
   access token, scoped to the app reg pointed at by `client-id`.
3. The exchange succeeds because the app reg has a federated identity
   credential matching that issuer + subject.

## Setup

```bash
az ad app federated-credential create \
  --id "$APP_OBJECT_ID" \
  --parameters '{
    "name": "github-main-branch",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:owner/repo:ref:refs/heads/main",
    "audiences": ["api://AzureADTokenExchange"]
  }'
```

The `subject` claim is the most security-critical setting. Use the
narrowest possible value:

- For a specific branch: `repo:owner/repo:ref:refs/heads/main` — **must** use this for production.
- For a specific environment: `repo:owner/repo:environment:prod` — **must** combine with a GitHub environment that has required-reviewers / branch-restriction rules.
- **Never** use wildcard subjects (`repo:owner/repo:*`, `repo:owner/repo:pull_request`, or claim-mapping that ignores `sub`) for any credential that can reach a non-throwaway tenant.

**Why wildcards are dangerous.** A wildcard subject says "trust *any*
OIDC token from this repository, regardless of which branch / PR /
workflow minted it." That collapses the entire branch-protection
model: any contributor who can open a pull request can author a
workflow file in their PR branch that runs on `pull_request` events,
mints an OIDC token with subject `repo:owner/repo:pull_request`, and
exchanges it for a prod Entra access token — with no review, no
required approvers, no environment gate. The FIC subject is the *only*
thing standing between "anyone with PR rights" and "anyone with prod
credentials." Pin it. The same rule applies to GitLab (`project_path`,
`ref`, `ref_type`), Bitbucket (`workspaceUuid:repositoryUuid:…`),
and Kubernetes service accounts (`system:serviceaccount:ns:sa-name`).

## Where this sample uses FIC

1. **`.github/workflows/wi-demo.yml`** — the canonical demo. A
   real GitHub workflow exchanging an OIDC token for an Entra app
   token to call this sample's RestaurantsApi.
2. **CD pipeline** — `azure/login` in `cd.yml` uses FIC to deploy
   bicep without storing a service principal secret.
3. **`SignedAssertionFromManagedIdentity` (BFF)** — superficially
   "federation," but **not** OIDC-FIC. This is
   `Microsoft.Identity.Web.SignedAssertionFromManagedIdentity`: a
   delegate that uses a Managed Identity to sign a JWT assertion which
   the BFF then submits as the `client_assertion` for an app
   registration's `client_credentials` grant. **The issuer of that
   assertion is the tenant STS, not an external OIDC provider.** The
   app registration carries a federated-identity-credential entry
   trusting the MI's issuer/subject (intra-tenant). Concretely:
   - True OIDC-FIC (cases 1 and 2 above): issuer is *outside* Entra
     (`token.actions.githubusercontent.com`, `gitlab.com`,
     `kubernetes.default.svc`, …), audience is
     `api://AzureADTokenExchange`, the federated source is a separate
     identity provider.
   - `SignedAssertionFromManagedIdentity` (this case): issuer is
     `https://login.microsoftonline.com/{tenant}/v2.0` (the tenant's
     own STS), the federated source is an Azure Managed Identity in the
     same tenant, and the FIC on the app reg is configured with that
     MI as the trusted issuer. It lets a confidential web client
     running on Azure perform OBO without a stored client secret, by
     proxying the MI's identity into a `client_assertion`.

   See [`glossary.md` — SignedAssertionFromManagedIdentity](../../glossary.md#signedassertionfrommanagedidentity)
   for the canonical definition. Mistaking the two swaps the
   credential model — see [`coverage-map.md` row "SignedAssertionFromManagedIdentity (BFF token-exchange) — not OIDC-FIC"](../../coverage-map.md).

## Why this is the default for non-Azure compute

- **No secrets.** Same benefit as MI, exported to non-Azure platforms.
- **Audit trail.** Every token exchange is logged with the original
  OIDC issuer and subject in Entra's sign-in logs.
- **Granular scoping.** Subject claim can pin trust to a specific
  branch / environment / pod / cluster — much finer than "this
  service has the secret."

## Cross-references

- Acquisition wiring (which library, scope strings, when to use OBO) — [`acquisition.md` §1b](../acquisition.md#1b-workload-identity-federation-fic--preferred-outside-azure).
- Receiver-side validation of FIC-acquired app tokens (`azp`, `roles`) — [`validation.md` §4](../validation.md#4-app-token-specific-checks).
- Per-environment app-reg / FIC creation in CI — [`deploy-cloud.md`](../deploy-cloud.md).
- Picker decision (when MI vs FIC vs cert) — [`index.md`](index.md#decision-matrix).
- Sibling credential patterns — [`managed-identity.md`](managed-identity.md), [`cert.md`](cert.md), [`client-secret.md`](client-secret.md).
- Glossary disambiguation — [`glossary.md` — Federated Identity Credential](../../glossary.md#federated-identity-credential-fic) vs [`glossary.md` — SignedAssertionFromManagedIdentity](../../glossary.md#signedassertionfrommanagedidentity).
- Auth-policy doctrine (delegated vs app-only, never OR-claims) — dotnet-engineering-guide [ch02 §10](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz).

---

## Sources

- Workload identity federation — overview — [learn.microsoft.com/entra/workload-id/workload-identity-federation](https://learn.microsoft.com/entra/workload-id/workload-identity-federation)
- Configure a federated identity credential on an app — [learn.microsoft.com/entra/workload-id/workload-identity-federation-create-trust](https://learn.microsoft.com/entra/workload-id/workload-identity-federation-create-trust)
- Federated identity credential considerations (subject formats, supported issuers) — [learn.microsoft.com/entra/workload-id/workload-identity-federation-considerations](https://learn.microsoft.com/entra/workload-id/workload-identity-federation-considerations)
- GitHub — About security hardening with OpenID Connect — [docs.github.com/actions/deployment/security-hardening-your-deployments/about-security-hardening-with-openid-connect](https://docs.github.com/actions/deployment/security-hardening-your-deployments/about-security-hardening-with-openid-connect)
- GitHub — Configuring OpenID Connect in Azure — [docs.github.com/actions/deployment/security-hardening-your-deployments/configuring-openid-connect-in-azure](https://docs.github.com/actions/deployment/security-hardening-your-deployments/configuring-openid-connect-in-azure)
- Microsoft.Identity.Web — Managed identity as a federated credential — [github.com/AzureAD/microsoft-identity-web/wiki/Using-managed-identity-as-a-federated-identity-credential](https://github.com/AzureAD/microsoft-identity-web/wiki/Using-managed-identity-as-a-federated-identity-credential)
- RFC 8693 — OAuth 2.0 Token Exchange — [rfc-editor.org/rfc/rfc8693](https://www.rfc-editor.org/rfc/rfc8693)
- OpenID Connect Core 1.0 — [openid.net/specs/openid-connect-core-1_0.html](https://openid.net/specs/openid-connect-core-1_0.html)
- SPIFFE — Secure Production Identity Framework for Everyone — [spiffe.io/docs/latest/spiffe-about/overview](https://spiffe.io/docs/latest/spiffe-about/overview)
- SPIFFE — SVID specification — [github.com/spiffe/spiffe/blob/main/standards/SPIFFE-ID.md](https://github.com/spiffe/spiffe/blob/main/standards/SPIFFE-ID.md)
