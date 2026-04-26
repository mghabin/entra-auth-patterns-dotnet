# Cloud deployment

Deploys all 7 FTGO services to **Azure Container Apps** in three environments — **dev → ppe → prod** — promoted by GitHub Actions OIDC. Zero stored client secrets, free-tier-friendly, ~$0/mo at idle.

## Architecture

```
GitHub repo (push to main)
  │
  └─ .github/workflows/cd.yml
       1. Build 7 images → ghcr.io/mghabin/ftgo-*:sha-XXX     (free, public)
       2. Deploy dev   (OIDC, no secrets)        ─┐
       3. Promote to ppe                          ├─ same image digest
       4. Promote to prod (required reviewer)    ─┘

Per-env Azure resources (resource group rg-ftgo-{env}-eastus):
  ├─ Log Analytics workspace               (free tier: 5 GB/mo)
  ├─ Application Insights                  (workspace-based)
  ├─ Container Apps managed environment
  │    ├─ 3 web apps  (apigateway, orderservice, restaurantservice)
  │    │   scale 0–3, http-concurrency rule, /health probe
  │    └─ 4 workers   (kitchen, accounting, delivery, notification)
  │        scale 1–3, CPU rule, no ingress
  └─ Key Vault                             (RBAC, optional — empty by default in MI-first design)
```

Identity:
- **CD identity:** one user-assigned MI per env (`ftgo-{env}-cd-mi`), federated to GitHub Actions via `repo:OWNER/REPO:environment:{env}`.
- **Service identity:** each ACA app gets a system-assigned MI. Each MI is federated to its corresponding Entra app registration so the service uses `SignedAssertionFromManagedIdentity` for downstream calls — no certs, no secrets.

## One-time bootstrap (per environment)

Each environment needs its CD identity and GitHub Environment created **once** before the workflow can deploy. Run from a workstation logged in to Azure (Owner on the subscription) and `gh` (admin on the repo):

```bash
./scripts/bootstrap-env.sh ENV=dev
./scripts/bootstrap-env.sh ENV=ppe
./scripts/bootstrap-env.sh ENV=prod   # also configures required-reviewer rule
```

What this does (per env, idempotent):
1. Creates `rg-ftgo-{env}-eastus`.
2. Creates `ftgo-{env}-cd-mi` user-assigned MI in that RG.
3. Creates a federated identity credential bound to `repo:OWNER/REPO:environment:{env}`.
4. Grants Contributor on the resource group; for prod, also User Access Administrator (so it can grant Key Vault RBAC).
5. Creates the GitHub Environment, sets `AZURE_CLIENT_ID` / `AZURE_SUBSCRIPTION_ID` env variables, and (one-time) the repo-scoped `AZURE_TENANT_ID` secret.
6. For prod, requires a single reviewer before deploys can proceed.

Re-running on an already-bootstrapped env is a no-op.

## First deploy

After bootstrapping `dev`:

```bash
git push origin main
```

The workflow auto-deploys to dev. Watch progress under **Actions → CD → deploy-dev**. The job summary prints the BFF FQDN and Scalar URL.

## Provisioning per-env Entra app registrations

The CD identity is intentionally scoped to ARM only (no Microsoft Graph). Entra app regs and the resolved env-var wiring are provisioned **out-of-band**, once per env (or whenever app regs change):

```bash
./scripts/provision-apps.sh ENV=dev
./scripts/provision-apps.sh ENV=ppe
./scripts/provision-apps.sh ENV=prod
```

This:
- Cold-bootstraps `azure.bicep` if no container apps exist yet (`entraConfig={}`).
- Reads each container app's MI principalId.
- Runs `main.bicep` to (re-)create env-suffixed app regs (`ftgo-dev-apigateway`, ...) and grant `Orders.Process` to the kitchen-worker MI.
- Federates the BFF ACA system MI to the BFF app reg (so it can mint client assertions via MI).
- Re-deploys `azure.bicep` with a populated `entraConfig` object — env vars now live in the bicep state, no more drift.
- Publishes the resolved `entraConfig` JSON as the `ENTRA_CONFIG_JSON` env-level GitHub variable, so subsequent CD redeploys pass the same wiring back into bicep.

After this runs once per env, every `git push origin main` fully deploys + wires the env automatically — re-running provision-apps is only needed when the Entra app regs themselves change.

## Promoting to ppe and prod

After the dev deploy succeeds, the same workflow run automatically continues to `deploy-ppe` (no gate) and then `deploy-prod` (waits for a reviewer). The **same image digest** is promoted — no rebuild.

To promote a previously-built SHA on demand (e.g. roll back):

```bash
gh workflow run cd.yml -f environment=prod -f imageTag=sha-abc1234
```

## Verification

Real teams verify production from telemetry, not curl-in-CI. Use:
- **Per-env Scalar UI**: `https://ftgo-{env}-apigateway-eus.<cae-domain>.azurecontainerapps.io/scalar/v1`
- **App Insights live metrics + dependency map** — full OBO/s2s call chain
- **Failure alerts** — wire a free Action Group → email when 5xx exceeds threshold

## Cost estimate

| Scenario | dev | ppe | prod | Total /mo |
|---|---|---|---|---|
| All envs scale-to-zero (idle) | $0 | $0 | $0 | **$0** |
| Prod 1 replica × 7 svcs always-on | $0 | $0 | ~$3-5 | **~$3-5** |
| Sustained 10 req/s prod (scale 1-3) | $0 | $0 | ~$15-25 | ~$15-25 |

Free-tier ceilings (per Azure subscription):
- ACA Consumption: 180k vCPU-s + 360k GiB-s/mo
- Log Analytics: 5 GB ingest/mo
- App Insights: included with workspace-based LAW
- Egress: 100 GB/mo
- ghcr.io public images and GitHub Actions on a public repo: free, unlimited

## File layout

| Path | Purpose |
|---|---|
| `Dockerfile` | Single parameterized multi-service Dockerfile (chiseled, ~95 MB) |
| `infra/bicep/azure.bicep` | Subscription-scope orchestrator (per-env Azure infra) |
| `infra/bicep/azure.{env}.bicepparam` | Per-env parameters |
| `infra/bicep/main.bicep` | Tenant-scope orchestrator (Entra app regs) |
| `infra/bicep/main.{env}.bicepparam` | Per-env Entra app reg params |
| `.github/workflows/cd.yml` | CD pipeline |
| `.github/workflows/cd-cleanup.yml` | Nightly scale-reset for dev/ppe |
| `scripts/bootstrap-env.sh` | One-time per-env bootstrap |
| `scripts/provision-apps.sh` | Per-env Entra app provisioning |

## See also

- [`docs/environments.md`](environments.md) — promotion model, adding a 4th env
- [`docs/run-locally.md`](run-locally.md) — local-dev path (not cloud)
