#!/usr/bin/env bash
# scripts/provision-apps.sh — per-env Entra app provisioning for cloud envs (dev|ppe|prod).
#
# Sibling to scripts/deploy.sh (which handles the local-dev path). For cloud envs we deploy
# infra/bicep/main.bicep at tenant scope to create the per-env app registrations, then create
# federated identity credentials linking each non-BFF service's app reg to the system MI of its
# Container App. Cloud services use SignedAssertionFromManagedIdentity (no client secrets, no certs).
#
# ORDERING: this script depends on infra/bicep/azure.bicep already being deployed for ${ENV}
# (typically by the cd.yml workflow). It reads the most-recent sub-scope deployment outputs to
# discover the BFF FQDN and each ACA app's principalId.
#
# Usage:
#   ./scripts/provision-apps.sh ENV=dev
#
# Prereqs: bash 4+, az CLI logged in (Owner at root scope to write app regs), jq.

set -euo pipefail

if (( BASH_VERSINFO[0] < 4 )); then
  echo "ERROR: bash 4+ required (you have ${BASH_VERSION})." >&2
  exit 1
fi

ENV=""
for arg in "$@"; do
  case "$arg" in
    ENV=*) ENV="${arg#ENV=}" ;;
    -h|--help) sed -n '2,17p' "$0" | sed 's/^# \?//'; exit 0 ;;
    *) echo "unknown arg: $arg (expected ENV=dev|ppe|prod)" >&2; exit 2 ;;
  esac
done

case "$ENV" in
  dev|ppe|prod) ;;
  *) echo "ERROR: ENV must be one of dev|ppe|prod (got '${ENV}')." >&2; exit 2 ;;
esac

for tool in az jq; do
  command -v "$tool" >/dev/null || { echo "ERROR: '$tool' not on PATH" >&2; exit 1; }
done

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
RG_NAME="rg-ftgo-${ENV}-eastus"
TENANT_ID="$(az account show --query tenantId -o tsv)"

echo "==> tenant : $TENANT_ID"
echo "==> env    : $ENV (rg=$RG_NAME)"

# 1. Locate the most-recent successful azure.bicep deployment for this env.
echo "==> Reading latest azure.bicep deployment outputs"
LATEST_DEPLOY=$(az deployment sub list \
  --query "[?starts_with(name, 'ftgo-${ENV}-') && properties.provisioningState=='Succeeded'] | sort_by(@, &properties.timestamp) | [-1].name" \
  -o tsv --only-show-errors)
if [[ -z "$LATEST_DEPLOY" ]]; then
  echo "ERROR: no successful sub-scope deployment 'ftgo-${ENV}-*' found. Deploy azure.bicep first." >&2
  exit 1
fi
echo "    deployment = $LATEST_DEPLOY"

OUTPUTS=$(az deployment sub show --name "$LATEST_DEPLOY" --query properties.outputs -o json --only-show-errors)
BFF_FQDN=$(jq -r '.apiGatewayFqdn.value' <<<"$OUTPUTS")
SERVICES=$(jq -r '.services.value' <<<"$OUTPUTS")
if [[ -z "$BFF_FQDN" || "$BFF_FQDN" == "null" ]]; then
  echo "ERROR: deployment $LATEST_DEPLOY missing apiGatewayFqdn output." >&2
  exit 1
fi
echo "    bff fqdn   = $BFF_FQDN"

# 2. Tenant-scope deployment for the per-env app regs.
echo "==> Deploying infra/bicep/main.bicep (env=$ENV)"
DEPLOY_NAME="ftgo-entra-${ENV}-$(date -u +%Y%m%d%H%M%S)"
AGW_REDIRECT="https://${BFF_FQDN}/signin-oidc"
SCALAR_REDIRECT="https://${BFF_FQDN}/scalar/v1"

az deployment tenant create \
  --name "$DEPLOY_NAME" \
  --location eastus \
  --template-file "$ROOT/infra/bicep/main.bicep" \
  --parameters "$ROOT/infra/bicep/main.${ENV}.bicepparam" \
  --parameters apiGatewayRedirectUri="$AGW_REDIRECT" scalarRedirectUri="$SCALAR_REDIRECT" \
  --only-show-errors --output none

APPS=$(az deployment tenant show --name "$DEPLOY_NAME" --query 'properties.outputs.apps.value' -o json --only-show-errors)

# 3. Federated identity credentials: each service's app reg ← its ACA system MI.
#
# SignedAssertionFromManagedIdentity flow: the service uses its system-assigned MI to mint a token
# with audience `api://AzureADTokenExchange`. The app reg trusts that token via a federated credential
# whose issuer is the tenant's STS, subject is the MI's clientId.
#
# Bicep keys:    apps[apiGateway|orderService|restaurantService|kitchenService|accountingService|deliveryService|notificationService]
# Azure keys:    services[apigateway|orderservice|...]   (lowercased, no dot)
declare -A SVC_TO_BICEP_KEY=(
  [apigateway]=apiGateway
  [orderservice]=orderService
  [restaurantservice]=restaurantService
  [kitchenservice]=kitchenService
  [accountingservice]=accountingService
  [deliveryservice]=deliveryService
  [notificationservice]=notificationService
)

ISSUER="https://login.microsoftonline.com/${TENANT_ID}/v2.0"
echo "==> Federated identity credentials"
printf '    %-22s %-38s %s\n' SERVICE APP_ID FQDN

for short in "${!SVC_TO_BICEP_KEY[@]}"; do
  bicep_key="${SVC_TO_BICEP_KEY[$short]}"
  app_id=$(jq -r --arg k "$bicep_key" '.[$k].appId // empty' <<<"$APPS")
  # main.bicep outputs only {appId, spId}. The Graph object id isn't exposed, so
  # resolve it on demand via the Graph appId → object lookup.
  app_obj_id=""
  if [[ -n "$app_id" ]]; then
    app_obj_id=$(az ad app show --id "$app_id" --query id -o tsv --only-show-errors 2>/dev/null || echo "")
  fi
  fqdn=$(jq -r --arg k "$short" '.[$k].fqdn // empty' <<<"$SERVICES")
  mi_client_id=$(az containerapp show --name "ftgo-${ENV}-${short}" --resource-group "$RG_NAME" \
    --query 'identity.principalId' -o tsv --only-show-errors 2>/dev/null || true)
  # The MI clientId is what we need as the FIC subject. principalId is the SP objectId — we need
  # to look up the matching clientId.
  mi_principal_id="$mi_client_id"
  if [[ -n "$mi_principal_id" && "$mi_principal_id" != "None" ]]; then
    mi_client_id=$(az ad sp show --id "$mi_principal_id" --query appId -o tsv --only-show-errors 2>/dev/null || echo "")
  fi

  printf '    %-22s %-38s %s\n' "$short" "${app_id:-?}" "${fqdn:-?}"

  # Skip BFF — it doesn't use SignedAssertionFromManagedIdentity for downstream OBO directly via FIC
  # on its own app reg in this pattern. (The BFF uses MI-issued client assertion against its own app
  # reg too, so include it as well — keeps things uniform.)
  if [[ -z "$app_id" || -z "$app_obj_id" || -z "$mi_client_id" ]]; then
    echo "      skipped (missing appId / objectId / MI clientId)"
    continue
  fi

  fic_name="aca-${ENV}-${short}"
  if az ad app federated-credential list --id "$app_obj_id" \
        --query "[?name=='${fic_name}']" -o tsv --only-show-errors 2>/dev/null | grep -q .; then
    echo "      FIC '${fic_name}' already exists"
  else
    az ad app federated-credential create --id "$app_obj_id" --parameters "$(jq -nc \
      --arg name "$fic_name" --arg issuer "$ISSUER" --arg sub "$mi_client_id" '{
        name: $name,
        issuer: $issuer,
        subject: $sub,
        description: "ACA system MI → app reg (SignedAssertionFromManagedIdentity)",
        audiences: ["api://AzureADTokenExchange"]
      }')" --only-show-errors --output none
    echo "      created FIC '${fic_name}' (subject=${mi_client_id})"
  fi
done

cat <<EOF

============================================================
Per-env Entra provisioning complete for ${ENV}.
  - tenant deployment: $DEPLOY_NAME
  - BFF redirect URI:  $AGW_REDIRECT
  - Scalar redirect:   $SCALAR_REDIRECT
============================================================
EOF
