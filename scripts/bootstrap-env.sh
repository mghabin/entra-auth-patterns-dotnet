#!/usr/bin/env bash
# scripts/bootstrap-env.sh — one-time per-environment bootstrap for the cloud CD pipeline.
#
# Provisions the bits the GitHub Actions cd.yml workflow assumes already exist:
#   1. Resource group        rg-ftgo-${ENV}-eastus
#   2. User-assigned MI      ftgo-${ENV}-cd-mi   (with GH OIDC federation)
#   3. RBAC                  Contributor on the RG (+ User Access Admin for prod)
#   4. GitHub Environment    ${ENV} (with required reviewer for prod)
#   5. GH env vars           AZURE_CLIENT_ID, AZURE_SUBSCRIPTION_ID
#   6. Repo secret           AZURE_TENANT_ID (one-time, shared across envs)
#
# Idempotent: re-running on a bootstrapped env is a no-op.
#
# Usage:
#   ./scripts/bootstrap-env.sh ENV=dev
#   ./scripts/bootstrap-env.sh ENV=prod
#
# Prereqs: bash 4+, az CLI logged in to the target subscription, gh CLI authenticated to the repo, jq.

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
    -h|--help) sed -n '2,18p' "$0" | sed 's/^# \?//'; exit 0 ;;
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

GH_OWNER="${GH_OWNER:-$(gh repo view --json owner --jq .owner.login)}"
GH_REPO="${GH_REPO:-$(gh repo view --json name --jq .name)}"
SUB_ID="$(az account show --query id -o tsv)"
TENANT_ID="$(az account show --query tenantId -o tsv)"
LOCATION="${LOCATION:-eastus}"
RG_NAME="rg-ftgo-${ENV}-${LOCATION}"
MI_NAME="ftgo-${ENV}-cd-mi"
FIC_NAME="github-${ENV}"
FIC_SUBJECT="repo:${GH_OWNER}/${GH_REPO}:environment:${ENV}"

echo "==> repo         : ${GH_OWNER}/${GH_REPO}"
echo "==> subscription : ${SUB_ID}"
echo "==> tenant       : ${TENANT_ID}"
echo "==> env          : ${ENV}  (rg=${RG_NAME}, mi=${MI_NAME})"
echo

# 1. Resource group
echo "==> Resource group"
if az group show --name "$RG_NAME" --only-show-errors --output none 2>/dev/null; then
  echo "    $RG_NAME already exists"
else
  az group create --name "$RG_NAME" --location "$LOCATION" --only-show-errors --output none
  echo "    created $RG_NAME"
fi

# 2. User-assigned managed identity
echo "==> User-assigned managed identity"
if az identity show --name "$MI_NAME" --resource-group "$RG_NAME" --only-show-errors --output none 2>/dev/null; then
  echo "    $MI_NAME already exists"
else
  az identity create --name "$MI_NAME" --resource-group "$RG_NAME" --location "$LOCATION" --only-show-errors --output none
  echo "    created $MI_NAME"
fi
MI_JSON=$(az identity show --name "$MI_NAME" --resource-group "$RG_NAME" -o json --only-show-errors)
MI_CLIENT_ID=$(jq -r .clientId <<<"$MI_JSON")
MI_PRINCIPAL_ID=$(jq -r .principalId <<<"$MI_JSON")
echo "    clientId    = $MI_CLIENT_ID"
echo "    principalId = $MI_PRINCIPAL_ID"

# 3. Federated identity credential (GH Actions → MI)
echo "==> Federated identity credential ($FIC_NAME)"
if az identity federated-credential show \
      --name "$FIC_NAME" --identity-name "$MI_NAME" --resource-group "$RG_NAME" \
      --only-show-errors --output none 2>/dev/null; then
  echo "    $FIC_NAME already exists"
else
  az identity federated-credential create \
    --name "$FIC_NAME" \
    --identity-name "$MI_NAME" \
    --resource-group "$RG_NAME" \
    --issuer "https://token.actions.githubusercontent.com" \
    --subject "$FIC_SUBJECT" \
    --audiences "api://AzureADTokenExchange" \
    --only-show-errors --output none
  echo "    created (subject=$FIC_SUBJECT)"
fi

# 4. RBAC
assign_role() {
  local role="$1" scope="$2"
  if az role assignment list --assignee "$MI_PRINCIPAL_ID" --role "$role" --scope "$scope" \
        --only-show-errors -o tsv --query '[].id' 2>/dev/null | grep -q .; then
    echo "    '$role' already assigned at $scope"
  else
    az role assignment create --assignee-object-id "$MI_PRINCIPAL_ID" \
      --assignee-principal-type ServicePrincipal \
      --role "$role" --scope "$scope" --only-show-errors --output none
    echo "    granted '$role' at $scope"
  fi
}
RG_SCOPE="/subscriptions/${SUB_ID}/resourceGroups/${RG_NAME}"
echo "==> Role assignments"
assign_role "Contributor" "$RG_SCOPE"
if [[ "$ENV" == "prod" ]]; then
  assign_role "User Access Administrator" "$RG_SCOPE"
fi

# 5. GitHub Environment + variables + reviewer (prod only)
echo "==> GitHub Environment '$ENV'"
if [[ "$ENV" == "prod" ]]; then
  REVIEWER_ID=$(gh api "users/${GH_OWNER}" --jq .id)
  ENV_BODY=$(jq -nc --argjson rid "$REVIEWER_ID" \
    '{wait_timer: 0, prevent_self_review: false, reviewers: [{type: "User", id: $rid}], deployment_branch_policy: null}')
else
  ENV_BODY='{"wait_timer":0,"reviewers":[],"deployment_branch_policy":null}'
fi
echo "$ENV_BODY" | gh api -X PUT "repos/${GH_OWNER}/${GH_REPO}/environments/${ENV}" \
  --input - --silent
echo "    upserted environment '$ENV'"

set_env_var() {
  local name="$1" value="$2"
  # Use gh variable set (supports --env). Idempotent: replaces existing value.
  if gh variable set "$name" --env "$ENV" --body "$value" --repo "${GH_OWNER}/${GH_REPO}" >/dev/null 2>&1; then
    echo "    var $name set"
  else
    # Fallback to REST in case the gh CLI version lacks --env support.
    gh api -X PATCH "repos/${GH_OWNER}/${GH_REPO}/environments/${ENV}/variables/${name}" \
      -f name="$name" -f value="$value" --silent 2>/dev/null \
    || gh api -X POST "repos/${GH_OWNER}/${GH_REPO}/environments/${ENV}/variables" \
      -f name="$name" -f value="$value" --silent
    echo "    var $name set (via REST)"
  fi
}
set_env_var "AZURE_CLIENT_ID"       "$MI_CLIENT_ID"
set_env_var "AZURE_SUBSCRIPTION_ID" "$SUB_ID"

# 6. Repo-level tenant secret (shared across envs; one-time)
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
  - GH OIDC identity:   ${MI_NAME} (clientId: ${MI_CLIENT_ID})
  - GitHub Environment: https://github.com/${GH_OWNER}/${GH_REPO}/settings/environments
  - Federated subject:  ${FIC_SUBJECT}

Next:
  - Push to main (auto-deploys dev → ppe → prod), or
  - gh workflow run cd.yml -f environment=${ENV}
============================================================
EOF
