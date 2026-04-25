#!/usr/bin/env bash
# scripts/provision-apps.sh — per-env Entra app provisioning for cloud envs (dev|ppe|prod).
#
# For each cloud env this script:
#   1. Deploys infra/bicep/main.bicep at tenant scope to (re-)create the per-env app
#      registrations (idempotent — Microsoft.Graph extension matches by uniqueName).
#      It grants Orders.Process to the KitchenWorker MI's principalId so the worker
#      can call OrdersApi using a bare ManagedIdentityCredential.
#   2. Creates a federated identity credential on the BFF app reg trusting the BFF
#      Container App's system MI as a SignedAssertionFromManagedIdentity issuer
#      (the BFF's confidential-client S2S/OBO calls require an app-reg identity).
#   3. Pushes per-env Entra config into each Container App's environment variables
#      (TenantId, ClientId, downstream URLs/scopes, allow-lists).
#
# ORDERING: this script depends on infra/bicep/azure.bicep already being deployed
# for ${ENV} (typically by cd.yml). It reads the most-recent RG-scope deployment
# outputs to discover the BFF FQDN and each ACA app's MI principalId.
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
    -h|--help) sed -n '2,21p' "$0" | sed 's/^# \?//'; exit 0 ;;
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

# Worker MI principalIds harvested from acaStack outputs — fed into permission-grants
# so the bare-MI workers (no app reg) get their Orders.Process role admin-consented.
KITCHEN_MI_PRINCIPAL_ID=$(jq -r '.kitchenWorker.principalId // empty' <<<"$SERVICES")
echo "    kitchen MI = ${KITCHEN_MI_PRINCIPAL_ID:-<none>}"

# 2. Tenant-scope deployment for the per-env app regs + MI role grants.
echo "==> Deploying infra/bicep/main.bicep (env=$ENV)"
DEPLOY_NAME="ftgo-entra-${ENV}-$(date -u +%Y%m%d%H%M%S)"
AGW_REDIRECT="https://${BFF_FQDN}/signin-oidc"
SCALAR_REDIRECT="https://${BFF_FQDN}/scalar/v1"

# main.${ENV}.bicepparam reads AZURE_TENANT_ID via readEnvironmentVariable.
export AZURE_TENANT_ID="$TENANT_ID"

WORKER_MI_JSON=$(jq -nc --arg k "$KITCHEN_MI_PRINCIPAL_ID" \
  'if $k == "" then {} else {kitchenWorker: $k} end')

az deployment tenant create \
  --name "$DEPLOY_NAME" \
  --location eastus \
  --template-file "$ROOT/infra/bicep/main.bicep" \
  --parameters "$ROOT/infra/bicep/main.${ENV}.bicepparam" \
  --parameters \
      apiGatewayRedirectUri="$AGW_REDIRECT" \
      scalarRedirectUri="$SCALAR_REDIRECT" \
      workerMiPrincipalIds="$WORKER_MI_JSON" \
  --only-show-errors --output none

APPS=$(az deployment tenant show --name "$DEPLOY_NAME" --query 'properties.outputs.apps.value' -o json --only-show-errors)

# 3. Federated identity credential on the BFF app reg ← BFF ACA system MI.
#
# SignedAssertionFromManagedIdentity flow: the BFF uses its system-assigned MI to mint a
# token with audience `api://AzureADTokenExchange`. The BFF app reg trusts that token via
# a federated credential whose issuer is the tenant's STS, subject is the MI's clientId.
# Then the BFF can do confidential-client S2S/OBO calls under its app-reg identity without
# any cert or secret on the box.
#
# Workers (Kitchen) do NOT need this — they call APIs as the MI directly (see Phase 2 docs).

echo "==> Federated identity credential (BFF app reg ← BFF MI)"
ISSUER="https://login.microsoftonline.com/${TENANT_ID}/v2.0"

bff_app_id=$(jq -r '.apiGateway.appId' <<<"$APPS")
bff_app_obj_id=$(az ad app show --id "$bff_app_id" --query id -o tsv --only-show-errors)
bff_mi_principal_id=$(jq -r '.apiGateway.principalId' <<<"$SERVICES")
bff_mi_client_id=$(az ad sp show --id "$bff_mi_principal_id" --query appId -o tsv --only-show-errors)
bff_fic_name="aca-${ENV}-apigateway"

if az ad app federated-credential list --id "$bff_app_obj_id" \
      --query "[?name=='${bff_fic_name}']" -o tsv --only-show-errors 2>/dev/null | grep -q .; then
  echo "    FIC '${bff_fic_name}' already exists"
else
  az ad app federated-credential create --id "$bff_app_obj_id" --parameters "$(jq -nc \
    --arg name "$bff_fic_name" --arg issuer "$ISSUER" --arg sub "$bff_mi_client_id" '{
      name: $name,
      issuer: $issuer,
      subject: $sub,
      description: "ACA system MI → BFF app reg (SignedAssertionFromManagedIdentity)",
      audiences: ["api://AzureADTokenExchange"]
    }')" --only-show-errors --output none
  echo "    created FIC '${bff_fic_name}' (subject=${bff_mi_client_id})"
fi

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
# NOTE: this wiring is imperative and gets clobbered on every azure.bicep redeploy.
# Phase 3 of the refactor moves these into bicep params for declarative wiring.

echo
echo "==> Wiring Entra config into Container App env vars"

ORDER_FQDN=$(jq -r '.ordersApi.fqdn'      <<<"$SERVICES")
REST_FQDN=$(jq  -r '.restaurantsApi.fqdn' <<<"$SERVICES")
ORDER_APPID=$(jq -r '.ordersApi.appId'      <<<"$APPS")
REST_APPID=$(jq  -r '.restaurantsApi.appId' <<<"$APPS")
BFF_APPID=$(jq   -r '.apiGateway.appId'     <<<"$APPS")

set_env() {
  local app_name="$1"; shift
  echo "    $app_name"
  for kv in "$@"; do echo "      $kv"; done
  az containerapp update --name "$app_name" --resource-group "$RG_NAME" \
    --set-env-vars "$@" --only-show-errors --output none
}

# 4a. AzureAd__TenantId / AzureAd__ClientId on every app that has its own app reg
#     (BFF + 2 APIs). KitchenWorker uses MI directly and reads no AzureAd:* values.
for key in apiGateway ordersApi restaurantsApi; do
  app_id=$(jq -r --arg k "$key" '.[$k].appId' <<<"$APPS")
  app_name=$(jq -r --arg k "$key" '.[$k].name // empty' <<<"$SERVICES")
  [[ -z "$app_name" ]] && continue
  set_env "$app_name" \
    "AzureAd__TenantId=$TENANT_ID" \
    "AzureAd__ClientId=$app_id"
done

# 4b. BFF (apigateway): DownstreamApis routing + URLs.
BFF_APP_NAME=$(jq -r '.apiGateway.name' <<<"$SERVICES")
set_env "$BFF_APP_NAME" \
  "AzureAd__ClientCredentials__0__SourceType=SignedAssertionFromManagedIdentity" \
  "DownstreamApis__Orders__BaseUrl=https://${ORDER_FQDN}/" \
  "DownstreamApis__Orders__Scopes__0=api://${ORDER_APPID}/orders.read" \
  "DownstreamApis__Orders__AppPermissionScopes__0=api://${ORDER_APPID}/.default" \
  "DownstreamApis__Restaurants__BaseUrl=https://${REST_FQDN}/" \
  "DownstreamApis__Restaurants__AppPermissionScopes__0=api://${REST_APPID}/.default"

# 4c. KitchenWorker: bare MI, no app reg. Just point it at OrdersApi.
KITC_APP_NAME=$(jq -r '.kitchenWorker.name' <<<"$SERVICES")
[[ -n "$KITC_APP_NAME" ]] && set_env "$KITC_APP_NAME" \
  "Downstream__BaseUrl=https://${ORDER_FQDN}/" \
  "Downstream__Scope=api://${ORDER_APPID}/.default"

# Microsoft public client appIds — pre-registered, well-known, used as the developer's
# local identity via DefaultAzureCredential / AzureCliCredential. Whitelisted ONLY in
# the dev env so a laptop can hit dev cloud APIs with `az account get-access-token`.
# ppe and prod accept tokens only from real workload identities (BFF).
#   Azure CLI:  04b07795-8ddb-461a-bbee-02f9e1bf7b46
#   Visual Studio Code: aebc6443-996d-45c2-90f0-388ff96faa56
ORDER_DEV_CLIENTS=()
REST_DEV_CLIENTS=()
if [[ "$ENV" == "dev" ]]; then
  ORDER_DEV_CLIENTS=(
    "EntraAuth__AllowedClientApps__1=04b07795-8ddb-461a-bbee-02f9e1bf7b46"
    "EntraAuth__AllowedClientApps__2=aebc6443-996d-45c2-90f0-388ff96faa56"
  )
  REST_DEV_CLIENTS=(
    "EntraAuth__AllowedClientApps__1=04b07795-8ddb-461a-bbee-02f9e1bf7b46"
    "EntraAuth__AllowedClientApps__2=aebc6443-996d-45c2-90f0-388ff96faa56"
  )
fi

# 4d. OrdersApi: AllowedClientApps whitelist (BFF, plus dev tools when env=dev).
#     The KitchenWorker MI's appId could be added too, but `RequireClientApp` checks `azp`
#     against this list — for MI tokens `azp` IS the MI clientId, so add it explicitly.
KITC_MI_CLIENT_ID=""
if [[ -n "$KITCHEN_MI_PRINCIPAL_ID" ]]; then
  KITC_MI_CLIENT_ID=$(az ad sp show --id "$KITCHEN_MI_PRINCIPAL_ID" --query appId -o tsv --only-show-errors 2>/dev/null || echo "")
fi
ORDER_APP_NAME=$(jq -r '.ordersApi.name' <<<"$SERVICES")
ORDER_ENV=(
  "EntraAuth__AllowedClientApps__0=$BFF_APPID"
  "${ORDER_DEV_CLIENTS[@]}"
)
if [[ -n "$KITC_MI_CLIENT_ID" ]]; then
  # Append after dev-tools to keep dev-tool indices stable across env=dev/ppe/prod.
  ORDER_ENV+=("EntraAuth__AllowedClientApps__3=$KITC_MI_CLIENT_ID")
fi
set_env "$ORDER_APP_NAME" "${ORDER_ENV[@]}"

# 4e. RestaurantsApi (multi-tenant): pin allowed tenant + BFF (plus dev tools when env=dev).
REST_APP_NAME=$(jq -r '.restaurantsApi.name' <<<"$SERVICES")
set_env "$REST_APP_NAME" \
  "EntraAuth__AllowedTenantIds__0=$TENANT_ID" \
  "EntraAuth__AllowedClientApps__0=$BFF_APPID" \
  "${REST_DEV_CLIENTS[@]}"

cat <<EOF

============================================================
Container App env vars wired for ${ENV}.
Browser flow: https://${BFF_FQDN}/signin-oidc
============================================================
EOF
