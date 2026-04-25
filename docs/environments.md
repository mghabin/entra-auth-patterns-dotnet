# Environments

Three environments share one Azure subscription and one Entra tenant:

| Env | RG | Trigger | Gate |
|---|---|---|---|
| **dev** | `rg-ftgo-dev-eastus` | every push to `main` | dev success |
| **ppe** | `rg-ftgo-ppe-eastus` | dev success | (none — auto-promote) |
| **prod** | `rg-ftgo-prod-eastus` | ppe success | required reviewer |

## Promotion model

```
push → build-images (matrix × 7) → tags :sha-XXX, :latest
            │
            ▼
   deploy-dev  (always-on path)
            │ (success)
            ▼
   deploy-ppe  (concurrency-group: ppe)
            │ (success)
            ▼
   deploy-prod (concurrency-group: prod, env reviewer required)
```

- **Single image digest** is deployed to all three envs — built once, promoted many.
- `workflow_dispatch` accepts an `imageTag` input to redeploy a previously-built SHA without rebuilding.
- `cancel-in-progress: false` on ppe/prod so a follow-up push never interrupts a running deploy.

## Per-env configuration

What lives where:

| Layer | Source | Per-env? |
|---|---|---|
| Image build (registry, tag) | `cd.yml` | no |
| Azure infra | `infra/bicep/azure.bicep` + `azure.{env}.bicepparam` | yes |
| Entra app regs | `infra/bicep/main.bicep` + `main.{env}.bicepparam` | yes |
| ACA scale, ingress, env vars | `container-app.bicep` (driven by `aca-stack.bicep`) | shape only — values uniform |
| Application Insights connection | injected by Bicep at deploy time | yes |
| `AZURE_CLIENT_ID`, `AZURE_SUBSCRIPTION_ID` | GitHub Environment **variables** | yes |
| `AZURE_TENANT_ID` | repo **secret** | no (single tenant) |

The CD identity per env (`ftgo-{env}-cd-mi`) is scoped to its own resource group only — no cross-env access.

## Adding a 4th environment (e.g., `staging`)

1. Add `infra/bicep/azure.staging.bicepparam` and `infra/bicep/main.staging.bicepparam`.
2. `./scripts/bootstrap-env.sh ENV=staging`.
3. Add a `deploy-staging` job to `cd.yml`, modeled on `deploy-ppe`, with `needs:` set to the upstream env you want it promoted from.
4. `./scripts/provision-apps.sh ENV=staging` after the first deploy.

## Production guardrails

- **Required reviewer** on the `prod` GitHub Environment (configured by `bootstrap-env.sh`).
- **Concurrency group `prod`** with `cancel-in-progress: false`.
- **No nightly scale-reset** for prod (the cleanup workflow skips it).
- **Min replicas = 0** for prod web apps too. To pin one replica to eliminate cold-start, override `cpu`/`memory` and `minReplicas` via the bicepparam file (left as a knob; default is scale-to-zero everywhere for free-tier safety).

## Cleanup

Nightly at 03:00 UTC, `cd-cleanup.yml` resets every dev and ppe container app to `min=0/max=3`. This is a defensive measure against forgotten always-on overrides; it never touches prod.

To tear down an env entirely:

```bash
az group delete --name rg-ftgo-dev-eastus --yes --no-wait
gh api -X DELETE repos/OWNER/REPO/environments/dev
```

The federated credential and the user-assigned MI go away with the resource group.
