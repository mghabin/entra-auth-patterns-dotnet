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
- For a specific branch: `repo:owner/repo:ref:refs/heads/main`
- For a specific environment: `repo:owner/repo:environment:prod`
- **Never** use `repo:owner/repo:*` or other wildcards in production —
  any PR could acquire prod tokens.

## Where this sample uses FIC

1. **`.github/workflows/wi-demo.yml`** — the canonical demo. A
   real GitHub workflow exchanging an OIDC token for an Entra app
   token to call this sample's RestaurantsApi.
2. **CD pipeline** — `azure/login` in `cd.yml` uses FIC to deploy
   bicep without storing a service principal secret.
3. **`SignedAssertionFromManagedIdentity` (BFF)** — this is *also*
   a federated identity credential, with the federation source being
   an Azure Managed Identity instead of an external IdP. The BFF
   uses its MI to mint a token whose issuer is the tenant's STS,
   and the BFF app reg has a FIC trusting that issuer/subject. This
   is how a confidential web client running on Azure can do OBO
   without a secret.

## Why this is the default for non-Azure compute

- **No secrets.** Same benefit as MI, exported to non-Azure platforms.
- **Audit trail.** Every token exchange is logged with the original
  OIDC issuer and subject in Entra's sign-in logs.
- **Granular scoping.** Subject claim can pin trust to a specific
  branch / environment / pod / cluster — much finer than "this
  service has the secret."
