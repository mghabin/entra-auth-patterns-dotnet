#!/usr/bin/env bash
# scripts/provision-apps.sh — single deployment entrypoint for cloud envs (dev|ppe|prod).
#
# Run this MANUALLY (requires Owner at root scope to write app regs + repo admin to write
# GitHub vars) on a fresh env or whenever Entra app regs change. CD redeploys then pick
# up the resolved entraConfig from the GitHub env-level variable and pass it through to
# bicep — env vars persist across pushes (no more drift).
#
# Workflow:
#
#   1. (cold only) Deploy infra/bicep/azure.bicep with entraConfig={} to create the
#      Container Apps + system MIs. Skipped when the kitchen-worker container app
#      already exists.
#   2. Read each container app's system-assigned MI principalId.
#   3. Deploy infra/bicep/main.bicep at tenant scope to (re-)create the per-env Entra
#      app registrations and grant Orders.Process to the kitchen-worker MI's principalId
#      (Microsoft.Graph extension is idempotent — matches by uniqueName).
#   4. Resolve the kitchen-worker MI's appId (clientId), used in OrdersApi's
#      EntraAuth__AllowedClientApps allow-list.
#   5. Create the federated identity credential on the BFF app reg trusting the BFF's
#      ACA system MI as a SignedAssertionFromManagedIdentity issuer (BFF's confidential-
#      client S2S/OBO calls require an app-reg identity).
#   6. Re-deploy infra/bicep/azure.bicep with a populated entraConfig object. ARM merges
#      the env-var changes into the container app templates declaratively.
#   7. Publish entraConfig as the ENTRA_CONFIG_JSON env-level GitHub variable so cd.yml
#      can re-pass it on subsequent CD redeploys.
#
# IMAGE_TAG: optional; defaults to `latest`.
#
# Usage:
#   ./scripts/provision-apps.sh ENV=dev
#   IMAGE_TAG=sha-abc1234 ./scripts/provision-apps.sh ENV=dev
#   WHAT_IF=1 ./scripts/provision-apps.sh ENV=ppe   # preview only; no resource changes
#
# Prereqs: bash 4+, az CLI logged in, gh CLI authenticated, jq.

set -euo pipefail

if (( BASH_VERSINFO[0] < 4 )); then
  echo "ERROR: bash 4+ required (you have ${BASH_VERSION})." >&2
  exit 1
fi

ENV=""
for arg in "$@"; do
  case "$arg" in
    ENV=*)        ENV="${arg#ENV=}" ;;
    IMAGE_TAG=*)  IMAGE_TAG="${arg#IMAGE_TAG=}" ;;
    -h|--help)    sed -n '2,38p' "$0" | sed 's/^# \?//'; exit 0 ;;
    *)            echo "unknown arg: $arg (expected ENV=dev|ppe|prod [IMAGE_TAG=...])" >&2; exit 2 ;;
  esac
done

case "$ENV" in
  dev|ppe|prod) ;;
  *) echo "ERROR: ENV must be one of dev|ppe|prod (got '${ENV}')." >&2; exit 2 ;;
esac

for tool in az jq gh; do
  command -v "$tool" >/dev/null || { echo "ERROR: '$tool' not on PATH" >&2; exit 1; }
done

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
RG_NAME="rg-ftgo-${ENV}-eastus"
TENANT_ID="$(az account show --query tenantId -o tsv)"
IMAGE_TAG="${IMAGE_TAG:-latest}"

# Container app names mirror the aca-stack.bicep `services` ordering.
APIGATEWAY_NAME="ftgo-${ENV}-apigateway-eus"
ORDERS_APP_NAME="ftgo-${ENV}-orders-api-eus"
RESTAURANTS_APP_NAME="ftgo-${ENV}-restaurants-api-eus"
KITCHEN_APP_NAME="ftgo-${ENV}-kitchen-worker-eus"

echo "==> tenant   : $TENANT_ID"
echo "==> env      : $ENV (rg=$RG_NAME)"
echo "==> imageTag : $IMAGE_TAG"

deploy_azure_bicep() {
  # $1 = entraConfig JSON (`{}` for cold, populated otherwise)
  # $2 = stage name suffix (cold|wire)
  # Status messages → stderr; deployment name → stdout (so caller can capture).
  local entra_config_json="$1"
  local stage="$2"
  local name="ftgo-${ENV}-$(date -u +%Y%m%d%H%M%S)-${stage}"
  echo "    deploying azure.bicep (name=$name, entraConfig=$([[ "$entra_config_json" == "{}" ]] && echo empty || echo populated))" >&2
  if [[ "${WHAT_IF:-0}" == "1" ]]; then
    echo "    WHAT_IF=1 — preview only, skipping create" >&2
    az deployment group what-if \
      --resource-group "$RG_NAME" \
      --template-file "$ROOT/infra/bicep/azure.bicep" \
      --parameters "$ROOT/infra/bicep/azure.${ENV}.bicepparam" \
      --parameters imageTag="$IMAGE_TAG" entraConfig="$entra_config_json" \
      --only-show-errors >&2
    echo "$name"
    return 0
  fi
  az deployment group create \
    --resource-group "$RG_NAME" \
    --template-file "$ROOT/infra/bicep/azure.bicep" \
    --parameters "$ROOT/infra/bicep/azure.${ENV}.bicepparam" \
    --parameters imageTag="$IMAGE_TAG" entraConfig="$entra_config_json" \
    --name "$name" \
    --only-show-errors --output none
  echo "$name"
}

# 1. Cold-bootstrap detection. If the kitchen-worker container app already exists, the
#    MIs are in place and we can skip the cold azure.bicep deploy. Otherwise do a cold
#    deploy with entraConfig={} to materialise ACA + MIs.
echo "==> Detecting deployment state"
if az containerapp show --name "$KITCHEN_APP_NAME" --resource-group "$RG_NAME" --only-show-errors --output none 2>/dev/null; then
  echo "    container apps exist — skipping cold deploy"
else
  echo "    cold env detected — bootstrapping container apps with empty entraConfig"
  deploy_azure_bicep '{}' 'cold' >/dev/null
fi

# 2. Read MI principalIds from container apps.
echo "==> Reading container app MIs"
APIGATEWAY_MI=$(az containerapp show --name "$APIGATEWAY_NAME"  --resource-group "$RG_NAME" --query identity.principalId -o tsv --only-show-errors)
KITCHEN_MI=$(az    containerapp show --name "$KITCHEN_APP_NAME" --resource-group "$RG_NAME" --query identity.principalId -o tsv --only-show-errors)
APIGATEWAY_FQDN=$(az containerapp show     --name "$APIGATEWAY_NAME"     --resource-group "$RG_NAME" --query properties.configuration.ingress.fqdn -o tsv --only-show-errors)
ORDERS_FQDN=$(az      containerapp show     --name "$ORDERS_APP_NAME"      --resource-group "$RG_NAME" --query properties.configuration.ingress.fqdn -o tsv --only-show-errors)
RESTAURANTS_FQDN=$(az containerapp show     --name "$RESTAURANTS_APP_NAME" --resource-group "$RG_NAME" --query properties.configuration.ingress.fqdn -o tsv --only-show-errors)
echo "    apigateway   MI=${APIGATEWAY_MI:0:8}…  fqdn=$APIGATEWAY_FQDN"
echo "    orders-api          fqdn=$ORDERS_FQDN"
echo "    restaurants-api     fqdn=$RESTAURANTS_FQDN"
echo "    kitchen-worker MI=${KITCHEN_MI:0:8}…"

# 3. Tenant-scope deployment for the per-env app regs + MI role grants.
echo "==> Deploying main.bicep (env=$ENV)"
ENTRA_DEPLOY="ftgo-entra-${ENV}-$(date -u +%Y%m%d%H%M%S)"
AGW_REDIRECT="https://${APIGATEWAY_FQDN}/signin-oidc"
SCALAR_REDIRECT="https://${APIGATEWAY_FQDN}/scalar/v1"

# main.${ENV}.bicepparam reads AZURE_TENANT_ID via readEnvironmentVariable.
export AZURE_TENANT_ID="$TENANT_ID"

WORKER_MI_JSON=$(jq -nc --arg k "$KITCHEN_MI" '{kitchenWorker: $k}')

if [[ "${WHAT_IF:-0}" == "1" ]]; then
  echo "    WHAT_IF=1 — preview only, skipping tenant-scope create"
  az deployment tenant what-if \
    --location eastus \
    --template-file "$ROOT/infra/bicep/main.bicep" \
    --parameters "$ROOT/infra/bicep/main.${ENV}.bicepparam" \
    --parameters \
        apiGatewayRedirectUri="$AGW_REDIRECT" \
        scalarRedirectUri="$SCALAR_REDIRECT" \
        workerMiPrincipalIds="$WORKER_MI_JSON" \
    --only-show-errors
  exit 0
fi

az deployment tenant create \
  --name "$ENTRA_DEPLOY" \
  --location eastus \
  --template-file "$ROOT/infra/bicep/main.bicep" \
  --parameters "$ROOT/infra/bicep/main.${ENV}.bicepparam" \
  --parameters \
      apiGatewayRedirectUri="$AGW_REDIRECT" \
      scalarRedirectUri="$SCALAR_REDIRECT" \
      workerMiPrincipalIds="$WORKER_MI_JSON" \
  --only-show-errors --output none

APPS=$(az deployment tenant show --name "$ENTRA_DEPLOY" --query 'properties.outputs.apps.value' -o json --only-show-errors)
BFF_APPID=$(jq         -r '.apiGateway.appId'     <<<"$APPS")
ORDERS_APPID=$(jq      -r '.ordersApi.appId'      <<<"$APPS")
RESTAURANTS_APPID=$(jq -r '.restaurantsApi.appId' <<<"$APPS")
echo "    bff appId             = $BFF_APPID"
echo "    orders-api appId      = $ORDERS_APPID"
echo "    restaurants-api appId = $RESTAURANTS_APPID"

# 4. Resolve kitchen-worker MI clientId (appId, not principalId). Required so OrdersApi
#    can match the worker's `azp` claim against EntraAuth__AllowedClientApps.
echo "==> Resolving kitchen-worker MI clientId"
KITCHEN_MI_CLIENT_ID=$(az ad sp show --id "$KITCHEN_MI" --query appId -o tsv --only-show-errors)
echo "    kitchen-worker clientId = $KITCHEN_MI_CLIENT_ID"

# 5. Federated identity credential on the BFF app reg ← BFF ACA system MI.
#
#    SignedAssertionFromManagedIdentity flow: the BFF uses its system-assigned MI to mint
#    a token with audience `api://AzureADTokenExchange`. The BFF app reg trusts that
#    token via a federated credential whose issuer is the tenant STS, subject is the MI's
#    clientId. Then the BFF can do confidential-client S2S/OBO calls under its app-reg
#    identity without any cert or secret on the box.
#
#    Workers (Kitchen) do NOT need this — they call APIs as the MI directly.
echo "==> Federated identity credential (BFF app reg ← BFF MI)"
ISSUER="https://login.microsoftonline.com/${TENANT_ID}/v2.0"
BFF_APP_OBJ_ID=$(az ad app show --id "$BFF_APPID" --query id -o tsv --only-show-errors)
BFF_MI_CLIENT_ID=$(az ad sp show --id "$APIGATEWAY_MI" --query appId -o tsv --only-show-errors)
BFF_FIC_NAME="aca-${ENV}-apigateway"

if az ad app federated-credential list --id "$BFF_APP_OBJ_ID" \
      --query "[?name=='${BFF_FIC_NAME}']" -o tsv --only-show-errors 2>/dev/null | grep -q .; then
  echo "    FIC '${BFF_FIC_NAME}' already exists"
else
  az ad app federated-credential create --id "$BFF_APP_OBJ_ID" --parameters "$(jq -nc \
    --arg name "$BFF_FIC_NAME" --arg issuer "$ISSUER" --arg sub "$BFF_MI_CLIENT_ID" '{
      name: $name,
      issuer: $issuer,
      subject: $sub,
      description: "ACA system MI → BFF app reg (SignedAssertionFromManagedIdentity)",
      audiences: ["api://AzureADTokenExchange"]
    }')" --only-show-errors --output none
  echo "    created FIC '${BFF_FIC_NAME}' (subject=${BFF_MI_CLIENT_ID})"
fi

# 6. Build full entraConfig and re-deploy azure.bicep. This is the step that wires env
#    vars into the container app templates declaratively; subsequent CD redeploys with
#    the same entraConfig will be no-ops on env vars.
echo "==> Wiring entraConfig into azure.bicep"
ENTRA_CONFIG_JSON=$(jq -nc \
  --arg tenantId                 "$TENANT_ID" \
  --arg bffAppId                 "$BFF_APPID" \
  --arg ordersApiAppId           "$ORDERS_APPID" \
  --arg restaurantsApiAppId      "$RESTAURANTS_APPID" \
  --arg kitchenWorkerMiClientId  "$KITCHEN_MI_CLIENT_ID" \
  --arg ordersApiFqdn            "$ORDERS_FQDN" \
  --arg restaurantsApiFqdn       "$RESTAURANTS_FQDN" \
  '{
    tenantId:                $tenantId,
    bffAppId:                $bffAppId,
    ordersApiAppId:          $ordersApiAppId,
    restaurantsApiAppId:     $restaurantsApiAppId,
    kitchenWorkerMiClientId: $kitchenWorkerMiClientId,
    ordersApiFqdn:           $ordersApiFqdn,
    restaurantsApiFqdn:      $restaurantsApiFqdn
  }')

WIRE_DEPLOY=$(deploy_azure_bicep "$ENTRA_CONFIG_JSON" 'wire')

# 7. Write entraConfig to the env-level GitHub variable consumed by cd.yml. CD reads
#    `${{ vars.ENTRA_CONFIG_JSON }}` and passes it back into azure.bicep on every
#    redeploy, so env vars persist across pushes.
echo "==> Publishing ENTRA_CONFIG_JSON GitHub env var (env=$ENV)"
if gh variable set ENTRA_CONFIG_JSON --env "$ENV" --body "$ENTRA_CONFIG_JSON" 2>/dev/null; then
  echo "    set ENTRA_CONFIG_JSON in GitHub environment '$ENV'"
else
  cat <<EOF >&2
    WARNING: failed to write ENTRA_CONFIG_JSON via 'gh variable set' — gh may not be
    authenticated or the env '$ENV' may not exist as a GitHub Environment yet.
    Set it manually so cd.yml will pick it up on the next push:
      gh variable set ENTRA_CONFIG_JSON --env $ENV --body '$ENTRA_CONFIG_JSON'
EOF
fi

cat <<EOF

============================================================
Provisioning complete for ${ENV}.
  - tenant deployment: $ENTRA_DEPLOY
  - infra deployment:  $WIRE_DEPLOY
  - BFF redirect URI:  $AGW_REDIRECT
  - Scalar redirect:   $SCALAR_REDIRECT
  - BFF URL:           https://${APIGATEWAY_FQDN}
  - GitHub env var:    ENTRA_CONFIG_JSON (env=$ENV)
============================================================
EOF
