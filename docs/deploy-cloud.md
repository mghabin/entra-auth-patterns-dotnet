# Cloud deployment

Deploys the **4 FTGO services** to **Azure Container Apps** in three environments — **ci → ppe → prod** — promoted by GitHub Actions OIDC. Zero stored client secrets, free-tier-friendly, **~$0/mo at idle** (see [`cost-zero.md`](cost-zero.md) for the full breakdown).

## Architecture

```
GitHub repo (push to main)
  │
  └─ .github/workflows/cd.yml
       1. Build 4 images → ghcr.io/mghabin/ftgo-*:sha-XXX     (free, public)
       2. Deploy ci   (OIDC, no secrets)        ← auto on push
       3. Promote to ppe                          ← manual `gh workflow run cd.yml -f environment=ppe`
       4. Promote to prod (required reviewer)     ← manual `gh workflow run cd.yml -f environment=prod`

Per-env Azure resources (resource group rg-ftgo-{env}-eastus):
  ├─ Log Analytics workspace               (free tier: 5 GB/mo, dailyCap=1 GB)
  ├─ Application Insights                  (workspace-based)
  ├─ Container Apps managed environment    (Consumption plan — no platform fee)
  │    ├─ 3 web apps  (apigateway, orders-api, restaurants-api)
  │    │   scale 0–1, http-concurrency rule, /health probes (live/ready/startup)
  │    └─ 1 worker    (kitchen-worker)
  │        scale 0–1, CPU rule, no ingress
  └─ Key Vault                             (RBAC, optional — empty by default in MI-first design)
```

Identity:

- **CD identity:** one user-assigned MI per env (`ftgo-{env}-cd-mi`), federated to GitHub Actions via `repo:OWNER/REPO:environment:{env}`.
- **Service identity:** each ACA app gets a **system-assigned** MI. The BFF's MI is federated to its Entra app registration so the BFF uses `SignedAssertionFromManagedIdentity` for downstream calls — no certs, no secrets. Other apps use the system-assigned MI directly to acquire downstream tokens.

## One-time bootstrap (per environment)

Each environment needs its CD identity and GitHub Environment created **once** before the workflow can deploy. Run from a workstation logged in to Azure (Owner on the subscription) and `gh` (admin on the repo):

```bash
./scripts/bootstrap-env.sh ENV=ci
./scripts/bootstrap-env.sh ENV=ppe
./scripts/bootstrap-env.sh ENV=prod   # also configures required-reviewer rule
```

What this does (per env, idempotent):

1. Creates `rg-ftgo-{env}-eastus`.
1. Creates `ftgo-{env}-cd-mi` user-assigned MI in that RG.
1. Creates a federated identity credential bound to `repo:OWNER/REPO:environment:{env}`.
1. Grants Contributor on the resource group; **all envs** also get User Access Administrator (RG-scoped) so the deploy pipeline can assign Key Vault RBAC roles to per-app system-MIs created by `azure.bicep`. The role is RG-scoped — the CD identity cannot assign roles outside its own env's RG.
1. Creates the GitHub Environment, sets `AZURE_CLIENT_ID` / `AZURE_SUBSCRIPTION_ID` env variables, and (one-time) the repo-scoped `AZURE_TENANT_ID` secret.
1. For prod, requires a single reviewer before deploys can proceed.

Re-running on an already-bootstrapped env is a no-op.

## First deploy

After bootstrapping `ci`:

```bash
git push origin main
```

The workflow auto-deploys to ci. Watch progress under **Actions → CD → deploy-ci**. The job summary prints the BFF FQDN and Scalar URL.

## Provisioning per-env Entra app registrations

The CD identity is intentionally scoped to ARM only (no Microsoft Graph). Entra app regs and the resolved env-var wiring are provisioned **out-of-band**, once per env (or whenever app regs change).

> **"Out-of-band" means run once per env, not on every push.** The Microsoft Graph application API is rate-limited and is **not designed for per-commit churn** — re-creating app regs, FICs, and admin-consented permission grants on every CI run risks `429 Too Many Requests`, partial failures that leave half-wired tenants, and an audit trail that drowns the real changes. Source-of-truth for the *resulting* wiring is the env-level GitHub variable `ENTRA_CONFIG_JSON`, which CD reads on every push.

```bash
./scripts/provision-apps.sh ENV=ci
./scripts/provision-apps.sh ENV=ppe
./scripts/provision-apps.sh ENV=prod
```

This:

- Cold-bootstraps `azure.bicep` if no container apps exist yet (`entraConfig={}`).
- Reads each container app's MI principalId.
- Runs `main.bicep` to (re-)create env-suffixed app regs (`ftgo-ci-apigateway`, ...) and grant `Orders.Process` to the kitchen-worker MI.
- Federates the BFF ACA system MI to the BFF app reg (so it can mint client assertions via MI).
- Re-deploys `azure.bicep` with a populated `entraConfig` object — env vars now live in the bicep state, no more drift.
- Publishes the resolved `entraConfig` JSON as the `ENTRA_CONFIG_JSON` env-level GitHub variable, so subsequent CD redeploys pass the same wiring back into bicep.

After this runs once per tier, every `git push origin main` fully deploys + wires **ci** automatically — re-running `provision-apps.sh` is only needed when the Entra app regs themselves change. ppe and prod require a manual `gh workflow run` (see next section).

### `ENTRA_CONFIG_JSON` source-of-truth and drift

- The **GitHub env variable `vars.ENTRA_CONFIG_JSON` is the source-of-truth** that flows into Bicep on every CD run. CD never reads from Entra directly.
- `scripts/provision-apps.sh` is the **only** writer: it reconciles app regs in the tenant, then re-publishes the resolved JSON back to the env variable. Anything you change manually in the Entra portal (a redirect URI, an app role, a federated credential) is **drift** until you re-run `provision-apps.sh ENV=<env>`, which re-syncs the variable from the live tenant state.
- **Never** hand-edit `vars.ENTRA_CONFIG_JSON` in the GitHub UI; the next provision run will overwrite it. If you must change wiring out of sequence, change it in `infra/bicep/main.bicep` (or the relevant `.bicepparam`) and re-run the script.

## Promoting to ppe and prod

**ppe and prod are manual-only** — auto-promotion is intentionally disabled to keep idle Azure spend at ~$0/month. See [`environments.md`](environments.md#why-ppe-and-prod-are-manual) for the design rationale.

```bash
# Promote latest ci SHA to ppe (build → deploy-ci → deploy-ppe)
gh workflow run cd.yml -f environment=ppe

# Promote latest ci SHA to prod (build → deploy-ci → deploy-ppe → deploy-prod, with reviewer gate)
gh workflow run cd.yml -f environment=prod

# Re-deploy a specific previously-built SHA (rollback path)
gh workflow run cd.yml -f environment=prod -f imageTag=sha-abc1234
```

The **same image digest** is promoted across envs — no rebuild between ci and prod.

## Verification

Real teams verify production from telemetry, not curl-in-CI. Use:

- **Per-env Scalar UI**: `https://ftgo-{env}-apigateway-eus.<cae-domain>.azurecontainerapps.io/scalar/v1`
- **App Insights live metrics + dependency map** — full OBO/s2s call chain
- **Failure alerts** — wire a free Action Group → email when 5xx exceeds threshold

## Cost estimate

| Scenario                                        | ci  | ppe | prod    | Total /mo |
| ----------------------------------------------- | --- | --- | ------- | --------- |
| All envs idle (scale-to-zero, no traffic)       | $0  | $0  | $0      | **$0**    |
| Dev continuously hit at low rate, ppe/prod idle | <$1 | $0  | $0      | **<$1**   |
| Prod 1 replica per service always-on (warm)     | $0  | $0  | ~$3-5   | ~$3-5     |
| Sustained 10 req/s prod (scale 1-3)             | $0  | $0  | ~$15-25 | ~$15-25   |

The $0 idle floor relies on:

- ACA Consumption plan (no fixed environment fee — only per-second vCPU/memory billing, which is zero at `minReplicas=0`).
- Log Analytics with `dailyQuotaGb=1` and 30-day retention (well under the 5 GB/mo free tier with no traffic).
- Application Insights workspace-based (billed via the Log Analytics meter, not separately).
- Key Vault Standard SKU, RBAC, no HSM (per-operation billing only — near-zero with no requests).
- GHCR public registry (free, unlimited), no ACR.

See [`cost-zero.md`](cost-zero.md) for the per-resource breakdown and the configuration knobs that keep idle cost at $0.

## File layout

| Path                                 | Purpose                                                          |
| ------------------------------------ | ---------------------------------------------------------------- |
| `Dockerfile`                         | Single parameterized multi-service Dockerfile (chiseled, ~95 MB) |
| `infra/bicep/azure.bicep`            | Subscription-scope orchestrator (per-env Azure infra)            |
| `infra/bicep/azure.{env}.bicepparam` | Per-env parameters                                               |
| `infra/bicep/main.bicep`             | Tenant-scope orchestrator (Entra app regs)                       |
| `infra/bicep/main.{env}.bicepparam`  | Per-env Entra app reg params                                     |
| `.github/workflows/cd.yml`           | CD pipeline                                                      |
| `.github/workflows/cd-cleanup.yml`   | Nightly scale-reset for ci/ppe                                   |
| `scripts/bootstrap-env.sh`           | One-time per-env bootstrap                                       |
| `scripts/provision-apps.sh`          | Per-env Entra app provisioning                                   |

## See also

- [`docs/environments.md`](environments.md) — promotion model, adding a 4th env
- [`docs/run-locally.md`](run-locally.md) — local-dev path (not cloud)
- [`docs/operations.md`](operations.md) — on-call runbook (CD UAMI recovery, what-if previews, env teardown)

## Sources

- GitHub Actions OIDC — configuring OpenID Connect in Azure — [docs.github.com/actions/deployment/security-hardening-your-deployments/configuring-openid-connect-in-azure](https://docs.github.com/actions/deployment/security-hardening-your-deployments/configuring-openid-connect-in-azure)
- Azure workload identity federation (FIC) — [learn.microsoft.com/entra/workload-id/workload-identity-federation](https://learn.microsoft.com/entra/workload-id/workload-identity-federation)
- Azure RBAC — built-in roles — [learn.microsoft.com/azure/role-based-access-control/built-in-roles](https://learn.microsoft.com/azure/role-based-access-control/built-in-roles)
- Microsoft Graph throttling guidance (`429`) — [learn.microsoft.com/graph/throttling](https://learn.microsoft.com/graph/throttling)
- Azure Container Apps — managed identities — [learn.microsoft.com/azure/container-apps/managed-identity](https://learn.microsoft.com/azure/container-apps/managed-identity)
- SLSA — supply-chain levels for software artifacts — [slsa.dev/spec/v1.0/levels](https://slsa.dev/spec/v1.0/levels)
- Sigstore / cosign — keyless signing and attestation — [docs.sigstore.dev/cosign/overview](https://docs.sigstore.dev/cosign/overview)
- GitHub artifact attestations — [docs.github.com/actions/security-for-github-actions/using-artifact-attestations/using-artifact-attestations-to-establish-provenance-for-builds](https://docs.github.com/actions/security-for-github-actions/using-artifact-attestations/using-artifact-attestations-to-establish-provenance-for-builds)
- infra-engineering-guide ch03 (CI/CD — pinned actions, OIDC over secrets, SLSA/Sigstore) — [github.com/mghabin/infra-engineering-guide/blob/main/docs/03-ci-cd.md](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/03-ci-cd.md)
- infra-engineering-guide ch06 (security & supply chain — workload identity, no static credentials) — [github.com/mghabin/infra-engineering-guide/blob/main/docs/06-security-supply-chain.md#3-workload-identity--the-no-static-credentials-rule](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/06-security-supply-chain.md#3-workload-identity--the-no-static-credentials-rule)
