# Environments

Four-tier ladder. Three are deployed to Azure; one (`local`) is the
developer-machine tier and never touches the cloud.

| Tier      | Where                                              | `ASPNETCORE_ENVIRONMENT` | Trigger                                                  | Gate                                                                   | Idle cost               |
| --------- | -------------------------------------------------- | ------------------------ | -------------------------------------------------------- | ---------------------------------------------------------------------- | ----------------------- |
| **local** | developer laptop (`dotnet run` / `docker compose`) | `Development`            | manual                                                   | none                                                                   | $0                      |
| **ci**    | `rg-ftgo-ci-eastus`                                | `Staging`                | every push to `main`                                     | none                                                                   | ~$0 (ACA scale-to-zero) |
| **ppe**   | `rg-ftgo-ppe-eastus`                               | `Staging`                | manual `workflow_dispatch` (`environment=ppe` or `prod`) | ci success                                                             | $0 when not deployed    |
| **prod**  | `rg-ftgo-prod-eastus`                              | `Production`             | manual `workflow_dispatch` (`environment=prod`)          | ppe success **and** required reviewer on the `prod` GitHub Environment | $0 when not deployed    |

> **Why "ci" not "dev"?** "dev" colloquially means "a developer's
> machine", and we already have one of those (the `local` tier).
> Naming the auto-deployed first cloud tier `ci` honestly describes
> what's running there: build artifacts produced by CI, deployed
> without a human gate. See [`glossary.md`](../glossary.md) for the
> ppe definition (Microsoft pre-production environment, analogous to
> "staging" elsewhere).

> **Runtime env decoupled from tier name.** `ASPNETCORE_ENVIRONMENT`
> is set by Bicep at deploy time, not derived from the tier name.
> Both `ci` and `ppe` set it to `Staging` (so app behaviour matches
> what prod will see — this is the dotnet-engineering-guide §07
> "config drives behaviour, not env-name branches" rule). Only `prod`
> sets `Production`.

## The `local` tier

Not in Bicep, no GitHub Environment, no CI workflow. Configured via:

- `appsettings.Development.json` (project-local defaults)
- `dotnet user-secrets` (per-developer secrets — never committed)
- `ASPNETCORE_ENVIRONMENT=Development` (set by `dotnet run` automatically)

See [`run-locally.md`](run-locally.md) for the full local setup.

Local code can talk to ci-tier APIs (whitelisted developer credentials —
Azure CLI + VS Code public-client appIds — are accepted on the ci tier
only, see `aca-stack.bicep`). It cannot talk to ppe or prod.

## Promotion model

```
push → build-images (matrix × 4) → tags :sha-XXX, :latest
            │
            ▼
   deploy-ci           (auto on push to main)
            │
            ▼ (only if `workflow_dispatch` was invoked with environment=ppe or prod)
   deploy-ppe          (concurrency-group: ppe)
            │
            ▼ (only if `workflow_dispatch` was invoked with environment=prod)
   deploy-prod         (concurrency-group: prod, env reviewer required)
```

- **ci is fully automatic** on every push to `main`. No human gate.
- **ppe and prod are manual-only.** Operators promote a known-good SHA via `gh workflow run cd.yml -f environment=ppe` (deploys ci → ppe) or `-f environment=prod` (deploys ci → ppe → prod). This keeps idle Azure spend to ~$0/month — only ci runs continuously between merges; ppe and prod are spun up on demand.
- **Single image digest** is deployed to all three Azure tiers — built once, promoted many.
- `workflow_dispatch` accepts an `imageTag` input to redeploy a previously-built SHA without rebuilding.
- `cancel-in-progress: false` on ppe/prod so a follow-up dispatch never interrupts a running deploy.

> **Single-digest implication.** Because the *same* image digest flows
> ci → ppe → prod, a regression caught in ci **blocks ppe and prod
> for the same SHA**. There is no "skip ci, ship a hotfix straight to
> prod" path — by design, prod can only ever run an image that ppe ran
> and ppe can only ever run one that ci ran. Plan rollbacks
> accordingly: `gh workflow run cd.yml -f environment=prod -f imageTag=sha-<known-good>`
> reuses an *older* digest that already passed all three tiers; it does
> **not** re-build. Avoid making "trivial" prod-only doc/config changes
> in CD config without bumping the SHA — they will not deploy until
> ci rebuilds.

## Why ppe and prod are manual

- **Cost first.** This sample runs on consumption-tier Azure Container Apps with `minReplicas=0` end-to-end so an idle environment bills near-zero. Auto-promoting every push through ppe and prod would warm three environments continuously and break the $0-idle promise. See [`cost-zero.md`](cost-zero.md).
- **Reproducibility second.** Manual promotion forces operators to think about which SHA they're shipping and capture the rollback target before pressing the button. This matches the dotnet-engineering-guide ch07 cloud-native posture: humans approve the move into shared environments; automation owns the actual deploy mechanics.
- **prod is the gate that matters.** A bad SHA reaching ppe is a paged on-call event; a bad SHA reaching prod is a customer-impacting incident. The required reviewer on the `prod` GitHub Environment is the last human checkpoint.
- **Adjust per organisation.** If your org runs ppe continuously (active soak testing, partner integration), drop the `if: github.event_name == 'workflow_dispatch'` guards on the `deploy-ppe` job in `cd.yml` and accept the cost. The trade-off is **deliberate** in this sample: speed for $0 idle.

## Per-tier configuration

What lives where:

| Layer                                      | Source                                                | Per-tier?                   |
| ------------------------------------------ | ----------------------------------------------------- | --------------------------- |
| Image build (registry, tag)                | `cd.yml`                                              | no                          |
| Azure infra                                | `infra/bicep/azure.bicep` + `azure.{tier}.bicepparam` | yes                         |
| Entra app regs                             | `infra/bicep/main.bicep` + `main.{tier}.bicepparam`   | yes                         |
| ACA scale, ingress, env vars               | `container-app.bicep` (driven by `aca-stack.bicep`)   | shape only — values uniform |
| Application Insights connection            | injected by Bicep at deploy time                      | yes                         |
| `AZURE_CLIENT_ID`, `AZURE_SUBSCRIPTION_ID` | GitHub Environment **variables**                      | yes                         |
| `AZURE_TENANT_ID`                          | repo **secret**                                       | no (single tenant)          |

The CD identity per tier (`ftgo-{tier}-cd-mi`) is scoped to its own resource group only — no cross-tier access.

## Adding a 5th tier (e.g., `staging`)

1. Add `infra/bicep/azure.staging.bicepparam` and `infra/bicep/main.staging.bicepparam`.
1. `./scripts/bootstrap-env.sh ENV=staging`.
1. Add a `deploy-staging` job to `cd.yml`, modeled on `deploy-ppe`, with `needs:` set to the upstream tier you want it promoted from.
1. `./scripts/provision-apps.sh ENV=staging` after the first deploy.

## Production guardrails

- **Required reviewer** on the `prod` GitHub Environment (configured by `bootstrap-env.sh`).
- **Concurrency group `prod`** with `cancel-in-progress: false`.
- **No nightly scale-reset** for prod (the cleanup workflow skips it).
- **Min replicas = 0** for prod web apps too. To pin one replica to eliminate cold-start, raise the `maxReplicas` parameter on `container-app.bicep` *and* edit the module to surface a `minReplicas` parameter (currently hard-coded to `0` to keep idle cost at $0 — see [`docs/cost-zero.md`](cost-zero.md)).

## Cleanup

Nightly at 03:00 UTC, `cd-cleanup.yml` resets every ci and ppe container app to `min=0/max=3`. This is a defensive measure against forgotten always-on overrides; it never touches prod. For complete teardown of a tier, see [`operations.md`](operations.md#teardown).

To tear down a tier entirely:

```bash
az group delete --name rg-ftgo-ci-eastus --yes --no-wait
gh api -X DELETE repos/OWNER/REPO/environments/ci
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
- ASP.NET Core environments — [learn.microsoft.com/aspnet/core/fundamentals/environments](https://learn.microsoft.com/aspnet/core/fundamentals/environments)
