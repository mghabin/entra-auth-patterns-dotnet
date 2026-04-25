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
LATEST_DEPLOY=$(az deployment group list \
  --resource-group "$RG_NAME" \
  --query "[?starts_with(name, 'ftgo-${ENV}-') && properties.provisioningState=='Succeeded'] | sort_by(@, &properties.timestamp) | [-1].name" \
  -o tsv --only-show-errors)
if [[ -z "$LATEST_DEPLOY" ]]; then
  echo "ERROR: no successful RG-scope deployment 'ftgo-${ENV}-*' found in $RG_NAME. Deploy azure.bicep first." >&2
  exit 1
fi
echo "    deployment = $LATEST_DEPLOY"

OUTPUTS=$(az deployment group show --resource-group "$RG_NAME" --name "$LATEST_DEPLOY" --query properties.outputs -o json --only-show-errors)
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

# main.${ENV}.bicepparam reads AZURE_TENANT_ID via readEnvironmentVariable; export it for the
# bicep build-params step that az runs internally.
export AZURE_TENANT_ID="$TENANT_ID"

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
  mi_principal_id=$(jq -r --arg k "$short" '.[$k].principalId // empty' <<<"$SERVICES")
  # The MI clientId is what we need as the FIC subject. principalId is the SP objectId — we need
  # to look up the matching clientId via Graph.
  mi_client_id=""
  if [[ -n "$mi_principal_id" && "$mi_principal_id" != "null" ]]; then
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

# 4. Wire Entra config into each Container App's environment variables.
#
# appsettings.json ships with placeholder GUIDs. .NET config binds env vars with '__'
# as the section separator and overrides JSON, so we push the per-env values here.
#
# Conventions:
#   AzureAd__TenantId / AzureAd__ClientId        — every service needs these
#   AzureAd__ClientCredentials__0__SourceType    — services that acquire downstream tokens
#   DownstreamApis__<name>__BaseUrl/Scopes/...   — BFF pattern (Microsoft.Identity.Web)
#   Downstream__BaseUrl/Scope                    — worker pattern (single-target client)
#   EntraAuth__AllowedClientApps__N              — resource-server whitelist (OrderService)
#   EntraAuth__AllowedTenantIds__N               — multi-tenant resource (RestaurantService)
echo
echo "==> Wiring Entra config into Container App env vars"

# Resolve well-known FQDNs and appIds for downstream wiring.
ORDER_FQDN=$(jq -r '.orderservice.fqdn' <<<"$SERVICES")
REST_FQDN=$(jq -r '.restaurantservice.fqdn' <<<"$SERVICES")
ORDER_APPID=$(jq -r '.orderService.appId' <<<"$APPS")
REST_APPID=$(jq -r '.restaurantService.appId' <<<"$APPS")
BFF_APPID=$(jq -r '.apiGateway.appId' <<<"$APPS")
ACCT_APPID=$(jq -r '.accountingService.appId' <<<"$APPS")
DELI_APPID=$(jq -r '.deliveryService.appId' <<<"$APPS")
NOTI_APPID=$(jq -r '.notificationService.appId' <<<"$APPS")
KITC_APPID=$(jq -r '.kitchenService.appId' <<<"$APPS")

set_env() {
  local app_name="$1"; shift
  echo "    $app_name"
  for kv in "$@"; do echo "      $kv"; done
  az containerapp update --name "$app_name" --resource-group "$RG_NAME" \
    --set-env-vars "$@" --only-show-errors --output none
}

# 4a. Per-service: AzureAd__TenantId + AzureAd__ClientId on every app.
for short in "${!SVC_TO_BICEP_KEY[@]}"; do
  bicep_key="${SVC_TO_BICEP_KEY[$short]}"
  app_id=$(jq -r --arg k "$bicep_key" '.[$k].appId' <<<"$APPS")
  app_name=$(jq -r --arg k "$short" '.[$k].name // empty' <<<"$SERVICES")
  [[ -z "$app_name" ]] && continue
  set_env "$app_name" \
    "AzureAd__TenantId=$TENANT_ID" \
    "AzureAd__ClientId=$app_id"
done

# 4b. BFF (apigateway): DownstreamApis routing + URLs.
BFF_APP_NAME=$(jq -r '.apigateway.name' <<<"$SERVICES")
set_env "$BFF_APP_NAME" \
  "AzureAd__ClientCredentials__0__SourceType=SignedAssertionFromManagedIdentity" \
  "DownstreamApis__Orders__BaseUrl=https://${ORDER_FQDN}/" \
  "DownstreamApis__Orders__Scopes__0=api://${ORDER_APPID}/orders.read" \
  "DownstreamApis__Orders__AppPermissionScopes__0=api://${ORDER_APPID}/.default" \
  "DownstreamApis__Restaurants__BaseUrl=https://${REST_FQDN}/" \
  "DownstreamApis__Restaurants__AppPermissionScopes__0=api://${REST_APPID}/.default"

# 4c. Worker services (call OrderService daemon-style via SignedAssertionFromManagedIdentity).
for short in accountingservice deliveryservice notificationservice; do
  app_name=$(jq -r --arg k "$short" '.[$k].name // empty' <<<"$SERVICES")
  [[ -z "$app_name" ]] && continue
  set_env "$app_name" \
    "AzureAd__ClientCredentials__0__SourceType=SignedAssertionFromManagedIdentity" \
    "Downstream__BaseUrl=https://${ORDER_FQDN}/" \
    "Downstream__Scope=api://${ORDER_APPID}/.default"
done

# 4d. KitchenService uses the MI directly (no app reg client) — point it at OrderService.
KITC_APP_NAME=$(jq -r '.kitchenservice.name' <<<"$SERVICES")
[[ -n "$KITC_APP_NAME" ]] && set_env "$KITC_APP_NAME" \
  "Downstream__BaseUrl=https://${ORDER_FQDN}/" \
  "Downstream__Scope=api://${ORDER_APPID}/.default"

# 4e. OrderService: AllowedClientApps whitelist (BFF + 4 workers).
ORDER_APP_NAME=$(jq -r '.orderservice.name' <<<"$SERVICES")
set_env "$ORDER_APP_NAME" \
  "EntraAuth__AllowedClientApps__0=$BFF_APPID" \
  "EntraAuth__AllowedClientApps__1=$ACCT_APPID" \
  "EntraAuth__AllowedClientApps__2=$DELI_APPID" \
  "EntraAuth__AllowedClientApps__3=$NOTI_APPID" \
  "EntraAuth__AllowedClientApps__4=$KITC_APPID"

# 4f. RestaurantService (multi-tenant): pin allowed tenant + BFF as allowed client.
REST_APP_NAME=$(jq -r '.restaurantservice.name' <<<"$SERVICES")
set_env "$REST_APP_NAME" \
  "EntraAuth__AllowedTenantIds__0=$TENANT_ID" \
  "EntraAuth__AllowedClientApps__0=$BFF_APPID"

cat <<EOF

============================================================
Container App env vars wired for ${ENV}.
Browser flow: https://${BFF_FQDN}/signin-oidc
============================================================
EOF
