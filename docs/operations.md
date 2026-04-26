# Operations runbook

This page is the on-call cheat sheet for the FTGO sample environments.
It covers the routine maintenance flows that aren't part of the normal
push-to-main → CD pipeline.

## Environments

| Env  | RG (eastus)               | KV soft-delete | Purge protection | Delete lock |
|------|---------------------------|----------------|------------------|-------------|
| dev  | `rg-ftgo-dev-eastus`      | 7d             | off              | none        |
| ppe  | `rg-ftgo-ppe-eastus`      | 7d             | off              | none        |
| prod | `rg-ftgo-prod-eastus`     | 90d            | **on**           | **CanNotDelete** |

The prod RG has a `Microsoft.Authorization/locks` deployed with `level:
CanNotDelete` (see `infra/bicep/azure.bicep`). Deletes — including
`az group delete` — are blocked until the lock is removed.

## Bicep what-if previews

Preview a deploy without making changes:

```bash
WHAT_IF=1 ENV=dev IMAGE_TAG=preview ./scripts/provision-apps.sh
```

Behavior:

* **Cold env** (no kitchen container app yet): previews the cold
  `azure.bicep` deploy and exits. The tenant + wire steps depend on
  ACA-derived values that don't exist yet, so they're skipped.
* **Warm env**: previews the cold-skip + tenant `main.bicep` deploy
  and exits before any side effects (FIC creation, GH variable write,
  wire deploy).

## Branch protection

Source of truth: `.github/branch-protection/main.json`. Apply or diff
via the `branch-protection` workflow:

```bash
# Apply the committed config to refs/heads/main
gh workflow run branch-protection.yml -f branch=main

# Drift report runs nightly; trigger it on demand:
gh workflow run branch-protection.yml
```

The drift job will fail if anyone has changed protection rules in the
GitHub UI without updating `main.json`.

### One-time setup: BRANCH_PROTECTION_TOKEN secret

The default `GITHUB_TOKEN` cannot manage branch protection — the API
requires repo-administration permission, which the automatic workflow
token cannot grant. Provision a fine-grained PAT (or, preferably, a
GitHub App installation token) with **Administration: Read and write**
scope on this repo, then store it as the repo secret
`BRANCH_PROTECTION_TOKEN`. The apply and drift jobs both need it.

Until that secret is set, the nightly drift cron will fail with a
clear error. Tracked in issue #67.

## Nightly cost-safety

`cd-cleanup.yml` runs at 03:00 UTC and scales every container app in
`rg-ftgo-{dev,ppe}-eastus` down to `min=0 max=3`. Prod is excluded
intentionally. Manual run:

```bash
gh workflow run cd-cleanup.yml
```

If a workload genuinely needs a higher floor, set it in
`infra/bicep/modules/aca-stack.bicep` (declarative) so the next
deploy re-asserts it after cleanup runs.

## Tearing down a non-prod env

```bash
# Confirm what will go
az resource list --resource-group "rg-ftgo-dev-eastus" --query '[].name' -o tsv

# Delete the RG (Key Vault enters soft-delete for 7 days; same-name
# re-provision in that window must use --recover, not create).
az group delete --name "rg-ftgo-dev-eastus" --yes --no-wait
```

For prod: don't. If genuinely required, this is a multi-person decision
that involves removing the delete lock first:

```bash
az lock delete --name ftgo-prod-rg-delete-lock --resource-group rg-ftgo-prod-eastus
```

## Re-running CD against an existing env

```bash
gh workflow run cd.yml \
  -f environment=dev \
  -f image_tag=$(git rev-parse --short HEAD)
```

Smoke-test polls `/health/live` for up to 180s after the deploy
completes. If that fails, check the container app revision logs:

```bash
az containerapp logs show \
  --name ftgo-dev-apigateway-eus \
  --resource-group rg-ftgo-dev-eastus \
  --type system --follow
```

## Provisioning a brand-new env

1. Create the matching GitHub Environment (`dev`/`ppe`/`prod`) with
   the standard env vars/secrets (`AZURE_CLIENT_ID`,
   `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`).
2. Create the federated credential on the bootstrap app reg for that
   environment (one-time, see `infra/bicep/bootstrap.bicep`).
3. Run `provision-apps.sh ENV=<env> IMAGE_TAG=<tag>`.
4. The script writes `vars.ENTRA_CONFIG_JSON` for the env so future CD
   runs are fully declarative.

## When something is on fire

* **`/scalar/v1` returns AADSTS900021** — `vars.ENTRA_CONFIG_JSON` is
  empty for that env. Re-run `provision-apps.sh ENV=<env>` to populate.
* **CD smoke-test fails after a successful deploy** — check container
  app logs (above); 180s should be enough for cold-start, but image
  pull from a new registry can be slower.
* **Branch protection drift alert** — open `.github/branch-protection/main.json`,
  reconcile against the failure diff in the workflow log, commit a fix,
  then re-run `branch-protection.yml` to apply.
* **CD `build-images` fails with Trivy CRITICAL/HIGH** — open the SARIF
  upload in the Security tab to see the CVE list. Fix order:
  bump the base image (Dockerfile FROM tag) and let Dependabot's
  docker ecosystem PR land, OR rebuild after upstream pushes a fix.
  Unfixable CVEs are already filtered (`ignore-unfixed: true`); a
  failure means there *is* a fix available somewhere in the dep tree.
* **CD aborts with "Refusing to deploy unattested images"** — image was
  pushed before the SLSA provenance pipeline was added, or the
  attestation got pruned. Re-run `cd.yml`'s `build-images` job to
  rebuild and re-attest.
