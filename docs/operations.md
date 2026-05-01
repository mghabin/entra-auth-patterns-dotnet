# Operations runbook

This page is the on-call cheat sheet for the FTGO sample environments.
It covers the routine maintenance flows that aren't part of the normal
push-to-main → CD pipeline.

## Environments

| Env    | RG (eastus)                 | KV soft-delete   | Purge protection   | Delete lock      |
| ------ | --------------------------- | ---------------- | ------------------ | ---------------- |
| ci     | `rg-ftgo-ci-eastus`         | 7d               | off                | none             |
| ppe    | `rg-ftgo-ppe-eastus`        | 7d               | off                | none             |
| prod   | `rg-ftgo-prod-eastus`       | 90d              | **on**             | **CanNotDelete** |

The prod RG has a `Microsoft.Authorization/locks` deployed with `level:
CanNotDelete` (see `infra/bicep/azure.bicep`). Deletes — including
`az group delete` — are blocked until the lock is removed.

## Bicep what-if previews

Preview a deploy without making changes:

```bash
WHAT_IF=1 ENV=ci IMAGE_TAG=preview ./scripts/provision-apps.sh
```

Behavior:

- **Cold env** (no kitchen container app yet): previews the cold
  `azure.bicep` deploy and exits. The tenant + wire steps depend on
  ACA-derived values that don't exist yet, so they're skipped.
- **Warm env**: previews the cold-skip + tenant `main.bicep` deploy
  and exits before any side effects (FIC creation, GH variable write,
  wire deploy).

## Branch protection (Repository Rulesets)

Source of truth: `.github/rulesets/main.json`. The `repo-rulesets` workflow runs **drift detection only** (nightly + on PRs that touch `.github/rulesets/`). The default `GITHUB_TOKEN` has read access to rulesets, so no PAT is required for drift.

Applying changes is a manual maintainer action — repository administration isn't exposed as a workflow permission scope, so an "apply" job would need an admin PAT or GitHub App and we keep CI free of admin secrets:

```bash
REPO=mghabin/entra-auth-patterns-dotnet
NAME=$(jq -r .name .github/rulesets/main.json)
ID=$(gh api "repos/$REPO/rulesets" --jq ".[]|select(.name==\"$NAME\")|.id")

# Update existing ruleset
jq 'del(._comment)' .github/rulesets/main.json \
  | gh api -X PUT "repos/$REPO/rulesets/$ID" --input -

# Or create new (first time only)
jq 'del(._comment)' .github/rulesets/main.json \
  | gh api -X POST "repos/$REPO/rulesets" --input -

# Drift report on demand
gh workflow run repo-rulesets.yml
```

The drift job fails if anyone changes ruleset settings in the GitHub UI without updating `main.json`.

(This replaced the legacy classic-branch-protection setup, which required a fine-grained `BRANCH_PROTECTION_TOKEN` PAT in CI for the apply step. The new setup needs no CI secrets at all.)

## Nightly cost-safety

`cd-cleanup.yml` runs at 03:00 UTC and scales every container app in
`rg-ftgo-{ci,ppe}-eastus` down to `min=0 max=3`. Prod is excluded
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
az resource list --resource-group "rg-ftgo-ci-eastus" --query '[].name' -o tsv

# Delete the RG (Key Vault enters soft-delete for 7 days; same-name
# re-provision in that window must use --recover, not create).
az group delete --name "rg-ftgo-ci-eastus" --yes --no-wait
```

For prod: don't. If genuinely required, this is a multi-person decision
that involves removing the delete lock first:

```bash
az lock delete --name ftgo-prod-rg-delete-lock --resource-group rg-ftgo-prod-eastus
```

## Re-running CD against an existing env

```bash
gh workflow run cd.yml \
  -f environment=ci \
  -f imageTag=$(git rev-parse --short HEAD)
```

Smoke-test polls `/health/live` for up to 180s after the deploy
completes. The canonical health-probe contract — three endpoints
(`/health/live`, `/health/ready`, `/health/startup`), tag-filtered, mapped
explicitly (NOT via Aspire ServiceDefaults `MapDefaultEndpoints()` which
only exposes them in Development) — is owned by **dotnet-engineering-guide
[ch06 §10](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/06-cloud-native.md#10-health-checks--three-endpoints-for-k8s-not-what-servicedefaults-gives-you)**.
The smoke-test expects HTTP `200` with body `{"status":"Healthy"}` on
`/health/live`; anything else (including `200` with `Degraded`/`Unhealthy`
body, or any `5xx`) fails the deploy. If that fails, check the container
app revision logs:

```bash
az containerapp logs show \
  --name ftgo-ci-apigateway-eus \
  --resource-group rg-ftgo-ci-eastus \
  --type system --follow
```

## CD identity (UAMI) recovery

The per-env CD identity (`ftgo-{env}-cd-mi`) is a user-assigned managed
identity created by `infra/bicep/bootstrap.bicep` and
[`scripts/bootstrap-env.sh`](../scripts/bootstrap-env.sh). Its
`principalId` and `clientId` are stable for the lifetime of the resource
— but if anyone deletes and re-creates the UAMI (manual portal action,
RG teardown without `az group delete --no-wait` finishing the FIC
cleanup, or running bootstrap with a fresh subscription), **both IDs
change**, and every artefact pinned to the old IDs (the GitHub
Environment `AZURE_CLIENT_ID`, the federated identity credential subject
on the UAMI, any `roleAssignments` referencing the principal) becomes
stale.

Recovery procedure (per env, idempotent):

1. **Rerun bootstrap** for the affected env — it is the only supported
   creator and is safe to re-run:

   ```bash
   ./scripts/bootstrap-env.sh ENV=<env>
   ```

   This re-creates the UAMI if missing, re-asserts the federated identity
   credential bound to `repo:OWNER/REPO:environment:<env>`, re-applies
   `Contributor` (and on prod, `User Access Administrator`) on the RG,
   and re-publishes the new `AZURE_CLIENT_ID` to the GitHub Environment.

2. **Re-publish env wiring** so CD picks up the new identity:

   ```bash
   ./scripts/provision-apps.sh ENV=<env>
   ```

   This refreshes `vars.ENTRA_CONFIG_JSON` and re-federates the BFF ACA
   system MI to its app reg.

3. **Re-run CD** to confirm the new identity can deploy:

   ```bash
   gh workflow run cd.yml -f environment=<env>
   ```

If the FIC subject still references the *old* UAMI (visible in the
"Federated credentials" tab on the Entra app reg) AAD will reject the
new OIDC exchange with `AADSTS70021: No matching federated identity
record found`. The bootstrap script removes orphan FICs on re-run; if
the workflow still fails with that code, check that
`bootstrap.bicep` ran to completion — the comment block at the top of
`infra/bicep/cd-bootstrap.bicep` documents the exact assertion order.

## Provisioning a brand-new env

1. Create the matching GitHub Environment (`ci`/`ppe`/`prod`) with
   the standard env vars/secrets (`AZURE_CLIENT_ID`,
   `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`).
2. Create the federated credential on the bootstrap app reg for that
   environment (one-time, see [`infra/bicep/bootstrap.bicep`](../infra/bicep/bootstrap.bicep)
   and the wrapper [`scripts/bootstrap-env.sh`](../scripts/bootstrap-env.sh)).
3. Run `provision-apps.sh ENV=<env> IMAGE_TAG=<tag>`.
4. The script writes `vars.ENTRA_CONFIG_JSON` for the env so future CD
   runs are fully declarative.

## When something is on fire

- **`/scalar/v1` returns AADSTS900021** — `vars.ENTRA_CONFIG_JSON` is
  empty for that env. Re-run `provision-apps.sh ENV=<env>` to populate.
- **CD smoke-test fails after a successful deploy** — check container
  app logs (above); 180s should be enough for cold-start, but image
  pull from a new registry can be slower.
- **Ruleset drift alert** — open `.github/rulesets/main.json`,
  reconcile against the failure diff in the workflow log, commit a fix,
  then apply manually using the `gh api` snippet in the
  "Branch protection (Repository Rulesets)" section above.
- **CD `build-images` fails with Trivy CRITICAL/HIGH** — open the SARIF
  upload in the Security tab to see the CVE list. Fix order:
  bump the base image (Dockerfile FROM tag) and let Dependabot's
  docker ecosystem PR land, OR rebuild after upstream pushes a fix.
  Unfixable CVEs are already filtered (`ignore-unfixed: true`); a
  failure means there *is* a fix available somewhere in the dep tree.
- **CD aborts with "Refusing to deploy unattested images"** — image was
  pushed before the SLSA provenance pipeline was added, or the
  attestation got pruned. Re-run `cd.yml`'s `build-images` job to
  rebuild and re-attest.

## Sources

- Azure CLI command reference — [learn.microsoft.com/cli/azure/reference-index](https://learn.microsoft.com/cli/azure/reference-index)
- `az containerapp logs show` — [learn.microsoft.com/cli/azure/containerapp/logs#az-containerapp-logs-show](https://learn.microsoft.com/cli/azure/containerapp/logs#az-containerapp-logs-show)
- Bicep `what-if` — preview deployments — [learn.microsoft.com/azure/azure-resource-manager/bicep/deploy-what-if](https://learn.microsoft.com/azure/azure-resource-manager/bicep/deploy-what-if)
- Resource Manager locks (`CanNotDelete`) — [learn.microsoft.com/azure/azure-resource-manager/management/lock-resources](https://learn.microsoft.com/azure/azure-resource-manager/management/lock-resources)
- Azure Key Vault soft-delete and purge protection — [learn.microsoft.com/azure/key-vault/general/soft-delete-overview](https://learn.microsoft.com/azure/key-vault/general/soft-delete-overview)
- GitHub Actions — viewing workflow run logs — [docs.github.com/actions/monitoring-and-troubleshooting-workflows/using-workflow-run-logs](https://docs.github.com/actions/monitoring-and-troubleshooting-workflows/using-workflow-run-logs)
- GitHub Repository Rulesets API — [docs.github.com/rest/repos/rules](https://docs.github.com/rest/repos/rules)
- Workload identity federation — error reference (`AADSTS70021` etc.) — [learn.microsoft.com/entra/identity-platform/reference-error-codes](https://learn.microsoft.com/entra/identity-platform/reference-error-codes)
- dotnet-engineering-guide ch06 §10 (canonical `/health/{live,ready,startup}` contract) — [github.com/mghabin/dotnet-engineering-guide/blob/main/docs/06-cloud-native.md#10-health-checks--three-endpoints-for-k8s-not-what-servicedefaults-gives-you](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/06-cloud-native.md#10-health-checks--three-endpoints-for-k8s-not-what-servicedefaults-gives-you)
- infra-engineering-guide ch05 (observability — SLO-driven alerting, structured logging) — [github.com/mghabin/infra-engineering-guide/blob/main/docs/05-observability.md](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/05-observability.md)
