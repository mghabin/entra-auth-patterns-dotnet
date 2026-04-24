#!/usr/bin/env bash
# scripts/setup-entra.sh
#
# Idempotently creates / updates the seven Entra app registrations the
# sample needs, plus exposed scopes, app roles, and one federated
# credential for the workload-identity worker. Prints the
# `dotnet user-secrets` and `gh secret set` commands you need to run.
#
# Prerequisites:
#   - az CLI logged in to the target tenant (`az login --allow-no-subscriptions`)
#   - jq installed
#
# Re-running is safe: the script looks up existing apps by display name
# and updates them in place rather than duplicating.

set -euo pipefail

GH_OWNER="${GH_OWNER:-mghabin}"
GH_REPO="${GH_REPO:-entra-auth-patterns-dotnet}"
GH_REF="${GH_REF:-refs/heads/main}"

declare -A APPS=(
  [ftgo-apigateway]="single"
  [ftgo-orderservice]="single"
  [ftgo-restaurantservice]="multi"
  [ftgo-kitchenservice]="single"
  [ftgo-accountingservice]="single"
  [ftgo-deliveryservice]="single"
  [ftgo-notificationservice]="single"
)

declare -A APP_IDS=()

ensure_app() {
  local name="$1" tenancy="$2"
  local audience="AzureADMyOrg"
  [[ "$tenancy" == "multi" ]] && audience="AzureADMultipleOrgs"

  local app_id
  app_id=$(az ad app list --display-name "$name" --query "[0].appId" -o tsv 2>/dev/null || true)
  if [[ -z "$app_id" ]]; then
    echo "==> Creating app: $name ($audience)"
    app_id=$(az ad app create --display-name "$name" --sign-in-audience "$audience" --query appId -o tsv)
  else
    echo "==> Found existing app: $name ($app_id)"
  fi
  APP_IDS[$name]="$app_id"

  # Ensure a service principal exists too.
  az ad sp show --id "$app_id" >/dev/null 2>&1 || az ad sp create --id "$app_id" >/dev/null
}

for app in "${!APPS[@]}"; do
  ensure_app "$app" "${APPS[$app]}"
done

ORDER_APP="${APP_IDS[ftgo-orderservice]}"
REST_APP="${APP_IDS[ftgo-restaurantservice]}"
GATE_APP="${APP_IDS[ftgo-apigateway]}"

echo
echo "==> Setting Application ID URIs"
az ad app update --id "$ORDER_APP" --identifier-uris "api://$ORDER_APP" >/dev/null
az ad app update --id "$REST_APP"  --identifier-uris "api://$REST_APP"  >/dev/null

echo
echo "==> Adding federated credential to ftgo-deliveryservice"
DELIV_APP="${APP_IDS[ftgo-deliveryservice]}"
az ad app federated-credential create \
  --id "$DELIV_APP" \
  --parameters "$(jq -nc \
      --arg sub "repo:${GH_OWNER}/${GH_REPO}:ref:${GH_REF}" \
      --arg name "github-${GH_OWNER}-${GH_REPO}-main" '{
        name: $name,
        issuer: "https://token.actions.githubusercontent.com",
        subject: $sub,
        description: "GitHub Actions OIDC for the workload-identity demo",
        audiences: ["api://AzureADTokenExchange"]
      }')" 2>/dev/null || echo "    (federated credential already exists — skipping)"

echo
echo "============================================================"
echo "App registrations ready. Copy the following into user-secrets:"
echo "============================================================"
for name in "${!APP_IDS[@]}"; do
  printf "  %-32s = %s\n" "$name" "${APP_IDS[$name]}"
done

cat <<EOF

# ---- ApiGateway ----
dotnet user-secrets --project src/Ftgo.ApiGateway set "AzureAd:TenantId" "\$(az account show --query tenantId -o tsv)"
dotnet user-secrets --project src/Ftgo.ApiGateway set "AzureAd:ClientId" "${GATE_APP}"
dotnet user-secrets --project src/Ftgo.ApiGateway set "DownstreamApis:Orders:Scopes:0"              "api://${ORDER_APP}/orders.read"
dotnet user-secrets --project src/Ftgo.ApiGateway set "DownstreamApis:Orders:AppPermissionScopes:0" "api://${ORDER_APP}/.default"
dotnet user-secrets --project src/Ftgo.ApiGateway set "DownstreamApis:Restaurants:AppPermissionScopes:0" "api://${REST_APP}/.default"

# ---- OrderService ----
dotnet user-secrets --project src/Ftgo.OrderService set "AzureAd:ClientId" "${ORDER_APP}"
dotnet user-secrets --project src/Ftgo.OrderService set "EntraAuth:AllowedClientApps:0" "${GATE_APP}"

# ---- gh repo secrets for the workload-identity demo ----
gh secret set AZURE_TENANT_ID         -b "\$(az account show --query tenantId -o tsv)"
gh secret set AZURE_CLIENT_ID         -b "${DELIV_APP}"
gh secret set FTGO_RESTAURANTS_SCOPE  -b "api://${REST_APP}/.default"
EOF

echo
echo "Done. Next steps:"
echo "  - Grant admin consent for the API permissions in the portal."
echo "  - Add scopes (orders.read) and app roles (Orders.Process,"
echo "    Restaurants.Read.All) via 'az ad app update --set' or the portal."
echo "  - Run scripts/new-cert.sh for ftgo-accountingservice."
