#!/usr/bin/env bash
# scripts/bootstrap-env.sh — one-time per-environment bootstrap for the cloud CD pipeline.
#
# Azure side  (declarative): infra/bicep/bootstrap.bicep deploys
#   1. Resource group       rg-ftgo-${ENV}-eastus
#   2. User-assigned MI     ftgo-${ENV}-cd-mi
#   3. Federated credential github-${ENV}  (subject: repo:OWNER/REPO:environment:ENV)
#   4. RBAC                 Contributor on the RG (+ User Access Admin for prod)
#
# GitHub side (imperative — outside Azure ARM):
#   5. GitHub Environment   ${ENV} (with required reviewer for prod)
#   6. GH env vars          AZURE_CLIENT_ID, AZURE_SUBSCRIPTION_ID
#   7. Repo secret          AZURE_TENANT_ID (one-time, shared across envs)
#
# Idempotent: safe to re-run. Bicep deployment uses deterministic names; gh PUT
# semantics upsert.
#
# Usage:
#   ./scripts/bootstrap-env.sh ENV=dev
#   ./scripts/bootstrap-env.sh ENV=ppe
#   ./scripts/bootstrap-env.sh ENV=prod
#
# Prereqs: bash 4+, az CLI logged in to the target subscription with Owner role,
# gh CLI authenticated to the repo, jq.

set -euo pipefail

if (( BASH_VERSINFO[0] < 4 )); then
  echo "ERROR: bash 4+ required (you have ${BASH_VERSION})." >&2
  echo "       macOS: 'brew install bash' then re-run with /usr/local/bin/bash." >&2
  exit 1
fi

ENV=""
for arg in "$@"; do
  case "$arg" in
    ENV=*) ENV="${arg#ENV=}" ;;
    -h|--help) sed -n '2,24p' "$0" | sed 's/^# \?//'; exit 0 ;;
    *) echo "unknown arg: $arg (expected ENV=dev|ppe|prod)" >&2; exit 2 ;;
  esac
done

case "$ENV" in
  dev|ppe|prod) ;;
  *) echo "ERROR: ENV must be one of dev|ppe|prod (got '${ENV}')." >&2; exit 2 ;;
esac

for tool in az gh jq; do
  command -v "$tool" >/dev/null || { echo "ERROR: '$tool' not on PATH" >&2; exit 1; }
done

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GH_OWNER="${GH_OWNER:-$(gh repo view --json owner --jq .owner.login)}"
GH_REPO="${GH_REPO:-$(gh repo view --json name --jq .name)}"
LOCATION="${LOCATION:-eastus}"
ACCOUNT_JSON="$(az account show -o json)"
TENANT_ID=$(jq -r .tenantId <<<"$ACCOUNT_JSON")
SUB_ID=$(jq    -r .id       <<<"$ACCOUNT_JSON")
SUB_NAME=$(jq  -r .name     <<<"$ACCOUNT_JSON")

# Detect "tenant-only" logins (no subscription selected). az returns a placeholder
# whose id == tenantId, which would later fail with a confusing SubscriptionNotFound.
if [[ "$SUB_ID" == "$TENANT_ID" ]] || [[ "$SUB_NAME" == "N/A(tenant level account)" ]]; then
  cat >&2 <<EOF
ERROR: az is logged in to tenant ${TENANT_ID} but no Azure subscription is selected.
       Detected: name='${SUB_NAME}', id='${SUB_ID}' (matches tenantId — placeholder, not a real subscription).

       You need a real Azure subscription to deploy resources. Options:
         * Create a free one:  https://azure.microsoft.com/free
         * Switch tenant:      az login --tenant <other-tenant-id>
         * Pick a sub:         az account list -o table
                               az account set --subscription <sub-id-or-name>
EOF
  exit 1
fi

echo "==> repo         : ${GH_OWNER}/${GH_REPO}"
echo "==> subscription : ${SUB_NAME} (${SUB_ID})"
echo "==> tenant       : ${TENANT_ID}"
echo "==> env          : ${ENV}"
echo

# ---------- Azure side: infra/bicep/bootstrap.bicep ----------
DEPLOY_NAME="bootstrap-${ENV}-$(date +%Y%m%d%H%M%S)"
echo "==> Deploying infra/bicep/bootstrap.bicep ($DEPLOY_NAME)"
GH_OWNER="$GH_OWNER" GH_REPO="$GH_REPO" \
  az deployment sub create \
    --name "$DEPLOY_NAME" \
    --location "$LOCATION" \
    --template-file "$ROOT/infra/bicep/bootstrap.bicep" \
    --parameters    "$ROOT/infra/bicep/bootstrap.${ENV}.bicepparam" \
    --only-show-errors --output none

OUTPUTS=$(az deployment sub show --name "$DEPLOY_NAME" --query properties.outputs -o json)
RG_NAME=$(jq -r .resourceGroupName.value <<<"$OUTPUTS")
MI_CLIENT_ID=$(jq -r .clientId.value         <<<"$OUTPUTS")
DEPLOY_SUB_ID=$(jq -r .subscriptionId.value  <<<"$OUTPUTS")
FIC_SUBJECT=$(jq -r .federatedSubject.value  <<<"$OUTPUTS")
echo "    resourceGroup = $RG_NAME"
echo "    clientId      = $MI_CLIENT_ID"
echo "    subject       = $FIC_SUBJECT"

# ---------- GitHub side: gh CLI (Bicep cannot model GitHub resources) ----------
echo "==> GitHub Environment '$ENV'"
if [[ "$ENV" == "prod" ]]; then
  # Assumes a user-owned repo. For org-owned repos, look up a team via
  # `gh api orgs/{org}/teams/{slug}` and use {type:"Team",id:<id>} instead.
  REVIEWER_ID=$(gh api "users/${GH_OWNER}" --jq .id)
  ENV_BODY=$(jq -nc --argjson rid "$REVIEWER_ID" \
    '{wait_timer: 0, prevent_self_review: false, reviewers: [{type: "User", id: $rid}], deployment_branch_policy: null}')
else
  ENV_BODY='{"wait_timer":0,"reviewers":[],"deployment_branch_policy":null}'
fi
echo "$ENV_BODY" | gh api -X PUT "repos/${GH_OWNER}/${GH_REPO}/environments/${ENV}" \
  --input - --silent
echo "    upserted environment '$ENV'"

# Env-scoped variables (clientId is public, not a secret).
gh variable set AZURE_CLIENT_ID       --env "$ENV" --body "$MI_CLIENT_ID"  --repo "${GH_OWNER}/${GH_REPO}"
gh variable set AZURE_SUBSCRIPTION_ID --env "$ENV" --body "$DEPLOY_SUB_ID" --repo "${GH_OWNER}/${GH_REPO}"
echo "    set AZURE_CLIENT_ID, AZURE_SUBSCRIPTION_ID for env '$ENV'"

# Repo-level tenant secret (shared across envs; one-time).
echo "==> Repo secret AZURE_TENANT_ID"
if gh secret list --repo "${GH_OWNER}/${GH_REPO}" --json name --jq '.[].name' | grep -qx 'AZURE_TENANT_ID'; then
  echo "    already set — leaving as-is"
else
  gh secret set AZURE_TENANT_ID --body "$TENANT_ID" --repo "${GH_OWNER}/${GH_REPO}" >/dev/null
  echo "    set"
fi

cat <<EOF

============================================================
Environment ${ENV} bootstrapped:
  - Resource group:     ${RG_NAME}
  - GH OIDC clientId:   ${MI_CLIENT_ID}
  - GitHub Environment: https://github.com/${GH_OWNER}/${GH_REPO}/settings/environments
  - Federated subject:  ${FIC_SUBJECT}

Next:
  - Push to main (auto-deploys dev → ppe → prod), or
  - gh workflow run cd.yml -f environment=${ENV}
============================================================
EOF
