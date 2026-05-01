# `$0/month` at idle — cost-zero design

This sample is built to cost **~$0/month when idle** so it can sit in a personal Azure subscription as a long-lived reference without burning credit. This page documents the per-resource decisions that produce that floor, so you don't accidentally regress them.

> **Doctrine source.** The "$0 idle" target is an explicit application of the FinOps doctrine in [`infra-engineering-guide`](https://github.com/mghabin/infra-engineering-guide) → [`docs/10-finops.md`](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/10-finops.md) (scale-to-zero, dailyCap on logs, no fixed-fee SKUs in non-prod). When in doubt, that doc wins; this page records the *concrete* settings used here. See [`DOCTRINE.md`](../DOCTRINE.md) for the full upstream-owner table.

## Idle cost ceiling per env

| Resource | SKU / config | Idle bill | Reason it stays at $0 |
|---|---|---|---|
| Azure Container Apps environment | **Consumption** plan (not Workload Profiles) | $0 | Consumption ACA charges only per-second vCPU + memory + per-request; **no fixed environment fee**. Workload-Profiles plans bill the profile flat-rate even when idle — we deliberately avoid them. |
| ACA app: `apigateway`, `orders-api`, `restaurants-api`, `kitchen-worker` | `minReplicas=0`, `maxReplicas=1`, `co.cooldownPeriod=300` | $0 | At `minReplicas=0` ACA scales to zero after the cooldown — no replicas means no vCPU-seconds billed. First request after idle takes a cold-start hit (~1-3s for a chiseled .NET image). |
| Log Analytics workspace | `PerGB2018`, `dailyQuotaGb=1`, `retentionInDays=30` | $0 | First **5 GB/month** of ingest is free per Azure subscription. The 1 GB daily cap is a hard guard against runaway log spam blowing the free tier. |
| Application Insights | **Workspace-based**, sampling at default | $0 | Workspace-based AI bills via the Log Analytics meter, not separately — covered by the same 5 GB free tier. Classic (non-workspace) AI has its own meter and is **not** free; do not switch back. |
| Key Vault | **Standard** SKU, RBAC, no HSM | ~$0 | Standard KV bills ~$0.03 per 10k operations. With MI-first design (no secrets stored), idle ops ≈ 0. RBAC has no extra charge over access policies. **Premium SKU (HSM-backed)** has a fixed monthly fee — do not enable it for this sample. |
| Container registry | **GHCR** (`ghcr.io/mghabin/ftgo-*`) public | $0 | Public images on GHCR are free with no pull limits for a public repo. We do **not** provision Azure Container Registry — even Basic ACR is ~$5/mo flat. |
| GitHub Actions CI/CD | Public repo, GitHub-hosted runners | $0 | Public repos get unlimited Actions minutes on standard runners. |
| Azure Front Door / Application Gateway / WAF | **Not deployed** | $0 | These have a non-zero hourly base price even with no traffic. Out of scope until prod traffic justifies them. |
| Azure DNS / custom domain | **Not deployed** | $0 | Sample uses the auto-issued `*.azurecontainerapps.io` hostname, which is free. |

**Total idle floor: ~$0/month per env.** Three envs idle ⇒ still ~$0.

## What you actually pay for

Per-second vCPU + memory above the [free grant](https://azure.microsoft.com/pricing/details/container-apps/) (currently 180k vCPU-seconds + 360k GiB-seconds per subscription per month, plus 2M requests). With all four apps cold and `minReplicas=0`, the meter does not tick at all — the free grant is irrelevant for an idle env.

Real-world example bills (rough order-of-magnitude, US East, Nov-2024 list prices):

| Workload | Approx /mo |
|---|---|
| 3 envs idle 24/7 | **~$0** |
| Dev hit ~100 req/day, ppe/prod idle | **<$1** |
| Prod 1 replica per app warm 24/7 (defeats scale-to-zero) | ~$3-5 |
| Sustained 10 req/s prod (autoscale 1-3) | ~$15-25 |

## Required guardrails (don't regress these)

These are enforced today and **must not** be relaxed without a corresponding cost-budget update:

- **Bicep**: `infra/bicep/modules/container-app.bicep` defaults `scale.minReplicas=0` and `scale.maxReplicas=maxReplicas` (parameterized — caller defaults to 1). Workers (`Ftgo.Kitchen.Worker`) are also `minReplicas=0` — they wake on the next message poll.
- **Bicep**: `infra/bicep/modules/log-analytics.bicep` sets `properties.workspaceCapping.dailyQuotaGb=1` and `retentionInDays=30`.
- **Bicep**: `infra/bicep/modules/key-vault.bicep` pins `sku.name='standard'` and `enableRbacAuthorization=true`.
- **Bicep**: no `Microsoft.ContainerRegistry/registries` resource exists — pull comes from GHCR.
- **CD**: only `deploy-dev` runs on push (see [`environments.md`](environments.md#promotion-model)). ppe and prod require `gh workflow run -f environment=...` so we don't accidentally hold replicas warm in higher envs.
- **CD**: `cd.yml` does not provision Front Door / App Gateway / WAF / custom domains. Adding any of these requires updating this page.
- **prod RG delete-lock**: prevents accidental teardown of the prod env, *not* a cost guard. Use the teardown procedure below.

## Teardown (true zero)

If you want the bill to be unconditionally $0 — including the cents from Key Vault metadata storage and LAW retention — delete the resource groups:

```bash
# dev / ppe — no lock
az group delete --name rg-ftgo-dev-eastus --yes --no-wait
az group delete --name rg-ftgo-ppe-eastus --yes --no-wait

# prod — remove the delete-lock first
LOCK_ID=$(az lock list --resource-group rg-ftgo-prod-eastus --query "[?name=='prod-rg-delete-lock'].id" -o tsv)
[ -n "$LOCK_ID" ] && az lock delete --ids "$LOCK_ID"
az group delete --name rg-ftgo-prod-eastus --yes --no-wait
```

Re-bootstrapping is idempotent: `./scripts/bootstrap-env.sh ENV=dev && ./scripts/provision-apps.sh ENV=dev` brings the env back. The Entra app regs themselves are tenant-scoped and survive RG deletion (`provision-apps.sh` reconciles them in place).

## Periodic checks

Light-touch, no extra cost:

- **Cost Management → Cost analysis**, scope to each `rg-ftgo-{env}-eastus`, group-by *Resource type*. Anything non-zero on an idle env is drift; chase it.
- **Log Analytics → Usage and estimated costs**: confirm "Daily cap" is enabled and the 30-day rolling ingest stays under 5 GB per subscription (across all workspaces).
- **Container Apps → Metrics → Replica count**: confirm replicas drop to 0 within ~5 minutes of last request. If they don't, check for a stuck health-probe loop.

## When to break the $0 rule

This is a *sample* posture. For a real production workload, the trade-offs invert:

- **`minReplicas=1`** to eliminate cold start (worth the few dollars/month).
- **Front Door + WAF** for L7 protection (flat hourly fee + per-request).
- **Premium Key Vault** if you have HSM compliance requirements.
- **Reserved capacity / savings plans** once usage is predictable — see infra-guide ch10 §3.

Each of those is a deliberate departure from this design, with a budget line attached.
