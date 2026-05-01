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

> **Single-digest implication.** Because the *same* image digest flows
> dev → ppe → prod, a regression caught in dev **blocks ppe and prod
> for the same SHA**. There is no "skip dev, ship a hotfix straight to
> prod" path — by design, prod can only ever run an image that ppe ran
> and ppe can only ever run one that dev ran. Plan rollbacks
> accordingly: `gh workflow run cd.yml -f environment=prod -f imageTag=sha-<known-good>`
> reuses an *older* digest that already passed all three envs; it does
> **not** re-build. Avoid making "trivial" prod-only doc/config changes
> in CD config without bumping the SHA — they will not deploy until
> dev rebuilds.

## Why ppe has no human gate

- **dev → ppe is automatic on dev success; only prod requires a reviewer.** The trade-off is **deliberate**: ppe exists to surface regressions that only appear against production-shaped infra (real ACA cold-start, real LAW ingestion, real Entra app-reg quotas) **before** a human is asked to approve prod. Inserting a human between dev and ppe just means ppe lags dev — and an out-of-date ppe catches **fewer** real issues, not more.
- **prod is the gate that matters.** A bad SHA reaching ppe is a paged on-call event for the deploy team; a bad SHA reaching prod is a customer-impacting incident. Spending the human-review budget on the *one* hop where the blast radius justifies it is a deliberate **speed-vs-risk** allocation.
- **Adjust per organisation.** If your org's ppe carries data subject to compliance review (HIPAA, FedRAMP), or is shared with external partners, add a required reviewer on the `ppe` GitHub Environment too — `bootstrap-env.sh` accepts a reviewer list per env. The default in this sample assumes ppe is internal-only.

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

## Sources

- GitHub Actions — using environments for deployment — [docs.github.com/actions/deployment/targeting-different-environments/using-environments-for-deployment](https://docs.github.com/actions/deployment/targeting-different-environments/using-environments-for-deployment)
- GitHub Actions — environment protection rules (required reviewers, wait timers) — [docs.github.com/actions/deployment/targeting-different-environments/managing-environments-for-deployment#environment-protection-rules](https://docs.github.com/actions/deployment/targeting-different-environments/managing-environments-for-deployment#environment-protection-rules)
- GitHub Actions — concurrency — [docs.github.com/actions/writing-workflows/choosing-what-your-workflow-does/control-the-concurrency-of-workflows-and-jobs](https://docs.github.com/actions/writing-workflows/choosing-what-your-workflow-does/control-the-concurrency-of-workflows-and-jobs)
- Container image digests vs tags (immutable promotion) — [docs.docker.com/reference/cli/docker/image/pull/#pull-an-image-by-digest-immutable-identifier](https://docs.docker.com/reference/cli/docker/image/pull/#pull-an-image-by-digest-immutable-identifier)
- Progressive delivery patterns — [martinfowler.com/articles/cd-pipeline-patterns.html](https://martinfowler.com/articles/cd-pipeline-patterns.html)
- Blue/Green deployment (Fowler) — [martinfowler.com/bliki/BlueGreenDeployment.html](https://martinfowler.com/bliki/BlueGreenDeployment.html)
- infra-engineering-guide ch03 (CI/CD — progressive delivery, blue/green & canary) — [github.com/mghabin/infra-engineering-guide/blob/main/docs/03-ci-cd.md](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/03-ci-cd.md)
