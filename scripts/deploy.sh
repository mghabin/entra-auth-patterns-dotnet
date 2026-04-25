#!/usr/bin/env bash
# scripts/deploy.sh — provision Entra apps for the FTGO sample. Idempotent: safe to re-run.
#
# Runs `az deployment tenant create` against infra/bicep/main.bicep, then performs the bits
# Bicep can't (yet) do declaratively: a federated credential on DeliveryService (the Microsoft.Graph
# 0.2.0-preview extension rejects FIC at runtime), the AccountingService dev cert, and dotnet
# user-secrets + gh repo secrets hydration.
#
# Prereqs: bash 4+ (macOS: `brew install bash`), az CLI logged in to the target tenant with Owner
# at root scope (https://aka.ms/elevateAccess), gh CLI authenticated to the repo, jq, openssl, dotnet.
#
# Usage:
#   ./scripts/deploy.sh                  # full provision (apps + perms + FIC + cert + secrets)
#   ./scripts/deploy.sh --what-if        # preview Bicep changes only, no writes
#   GH_OWNER=mghabin GH_REPO=… ./scripts/deploy.sh
#   GH_REF=refs/heads/feat/x ./scripts/deploy.sh

set -euo pipefail

WHAT_IF=0
for arg in "$@"; do
  case "$arg" in
    --what-if|-w) WHAT_IF=1 ;;
    -h|--help)
      sed -n '2,16p' "$0" | sed 's/^# \?//'
      exit 0 ;;
    *) echo "unknown arg: $arg" >&2; exit 2 ;;
  esac
done

if (( ${BASH_VERSINFO[0]} < 4 )); then
  echo "ERROR: bash 4+ required (you have ${BASH_VERSION})." >&2
  echo "       macOS users: 'brew install bash' then re-run with /usr/local/bin/bash." >&2
  exit 1
fi

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"

for tool in az gh jq openssl dotnet; do
  command -v "$tool" >/dev/null || { echo "ERROR: '$tool' not on PATH" >&2; exit 1; }
done

TENANT_ID="$(az account show --query tenantId -o tsv 2>/dev/null | tail -1)"
if [[ -z "$TENANT_ID" ]]; then
  echo "ERROR: not signed into az. Run 'az login --allow-no-subscriptions' first." >&2
  exit 1
fi

GH_OWNER="${GH_OWNER:-$(gh repo view --json owner --jq .owner.login 2>/dev/null || echo mghabin)}"
GH_REPO="${GH_REPO:-$(gh repo view --json name --jq .name 2>/dev/null || echo entra-auth-patterns-dotnet)}"
GH_REF="${GH_REF:-refs/heads/main}"

echo "==> tenant : $TENANT_ID"
echo "==> repo   : $GH_OWNER/$GH_REPO @ $GH_REF"
echo

DEPLOY_NAME="ftgo-$(date +%s)"

if (( WHAT_IF )); then
  echo "==> what-if preview only — no resources will be created/modified"
  AZURE_TENANT_ID="$TENANT_ID" \
  az deployment tenant what-if \
    --name "$DEPLOY_NAME" \
    --location eastus \
    --template-file "$ROOT/infra/bicep/main.bicep" \
    --parameters "$ROOT/infra/bicep/main.bicepparam"
  exit 0
fi

echo "==> Deploying Bicep ($DEPLOY_NAME) — provisions 7 app regs + permissions + admin consents"

AZURE_TENANT_ID="$TENANT_ID" \
az deployment tenant create \
  --name "$DEPLOY_NAME" \
  --location eastus \
  --template-file "$ROOT/infra/bicep/main.bicep" \
  --parameters "$ROOT/infra/bicep/main.bicepparam" \
  --no-prompt \
  --output none

echo "==> Reading deployment outputs"
APPS_JSON=$(az deployment tenant show --name "$DEPLOY_NAME" --query 'properties.outputs.apps.value' -o json)
appId() { jq -r ".$1.appId" <<<"$APPS_JSON"; }

GATE_APP=$(appId apiGateway)
ORDER_APP=$(appId orderService)
REST_APP=$(appId restaurantService)
KITCHEN_APP=$(appId kitchenService)
ACCT_APP=$(appId accountingService)
DELIV_APP=$(appId deliveryService)
NOTIF_APP=$(appId notificationService)

echo "    apigateway          = $GATE_APP"
echo "    orderservice        = $ORDER_APP"
echo "    restaurantservice   = $REST_APP"
echo "    kitchenservice      = $KITCHEN_APP"
echo "    accountingservice   = $ACCT_APP"
echo "    deliveryservice     = $DELIV_APP"
echo "    notificationservice = $NOTIF_APP"

# Set identifierUris to `api://{appId}` on the public-facing apps. Bicep can't self-reference an app's
# own appId during creation, so we patch it here. Idempotent — re-applies the same value on every run.
echo
echo "==> Setting identifierUris on public-facing apps"
for app in "$GATE_APP" "$ORDER_APP" "$REST_APP"; do
  current=$(az ad app show --id "$app" --query 'identifierUris' -o json)
  desired="[\"api://$app\"]"
  if [[ "$current" != "$desired" ]]; then
    az ad app update --id "$app" --identifier-uris "api://$app" --output none
    echo "    $app ← api://$app"
  else
    echo "    $app already set"
  fi
done

# Bicep-Graph 0.2.0-preview rejects federatedIdentityCredentials at runtime; do it via az CLI.
echo
echo "==> Federated credential (DeliveryService → GitHub OIDC)"
FIC_SUB="repo:${GH_OWNER}/${GH_REPO}:ref:${GH_REF}"
FIC_NAME="github-${GH_OWNER}-${GH_REPO}"

if az ad app federated-credential list --id "$DELIV_APP" --query "[?name=='$FIC_NAME']" -o tsv 2>/dev/null | grep -q .; then
  echo "    federated credential '$FIC_NAME' already exists — skipping"
else
  az ad app federated-credential create --id "$DELIV_APP" --parameters "$(jq -nc \
    --arg name "$FIC_NAME" --arg sub "$FIC_SUB" '{
      name: $name,
      issuer: "https://token.actions.githubusercontent.com",
      subject: $sub,
      description: "GitHub Actions OIDC for the workload-identity demo",
      audiences: ["api://AzureADTokenExchange"]
    }')" --output none
  echo "    created '$FIC_NAME'"
fi

echo
echo "==> Self-signed cert for AccountingService"
APP_NAME=ftgo-local-accountingservice "$HERE/new-cert.sh"
ACCT_PFX="$ROOT/.certs/ftgo-local-accountingservice.pfx"

# ApiGateway also needs a credential for downstream OBO. In Azure it uses a federated assertion via
# managed identity (`SignedAssertionFromManagedIdentity`); on a laptop there's no IMDS, so we generate
# a local cert and override `AzureAd:ClientCredentials` via user-secrets to source from disk.
echo
echo "==> Self-signed cert for ApiGateway (local-dev OBO)"
APP_NAME=ftgo-local-apigateway "$HERE/new-cert.sh"
GATE_PFX="$ROOT/.certs/ftgo-local-apigateway.pfx"

echo
echo "==> Hydrating dotnet user-secrets for 7 projects"

set_secret() {
  dotnet user-secrets --project "$ROOT/src/$1" set "$2" "$3" >/dev/null
}

for p in Ftgo.ApiGateway Ftgo.OrderService Ftgo.RestaurantService Ftgo.KitchenService Ftgo.AccountingService Ftgo.DeliveryService Ftgo.NotificationService; do
  dotnet user-secrets --project "$ROOT/src/$p" init >/dev/null 2>&1 || true
done

set_secret Ftgo.ApiGateway "AzureAd:TenantId" "$TENANT_ID"
set_secret Ftgo.ApiGateway "AzureAd:ClientId" "$GATE_APP"
set_secret Ftgo.ApiGateway "AzureAd:ClientCredentials:0:SourceType"           "Path"
set_secret Ftgo.ApiGateway "AzureAd:ClientCredentials:0:CertificateDiskPath"  "$GATE_PFX"
set_secret Ftgo.ApiGateway "AzureAd:ClientCredentials:0:CertificatePassword"  ""
set_secret Ftgo.ApiGateway "DownstreamApis:Orders:Scopes:0"              "api://${ORDER_APP}/orders.read"
set_secret Ftgo.ApiGateway "DownstreamApis:Orders:AppPermissionScopes:0" "api://${ORDER_APP}/.default"
set_secret Ftgo.ApiGateway "DownstreamApis:Restaurants:AppPermissionScopes:0" "api://${REST_APP}/.default"

set_secret Ftgo.OrderService "AzureAd:TenantId" "$TENANT_ID"
set_secret Ftgo.OrderService "AzureAd:ClientId" "$ORDER_APP"
set_secret Ftgo.OrderService "EntraAuth:AllowedClientApps:0" "$GATE_APP"

set_secret Ftgo.RestaurantService "AzureAd:TenantId" "$TENANT_ID"
set_secret Ftgo.RestaurantService "AzureAd:ClientId" "$REST_APP"
set_secret Ftgo.RestaurantService "EntraAuth:AllowedClientApps:0" "$GATE_APP"
set_secret Ftgo.RestaurantService "EntraAuth:AllowedTenantIds:0"  "$TENANT_ID"

set_secret Ftgo.AccountingService "AzureAd:TenantId" "$TENANT_ID"
set_secret Ftgo.AccountingService "AzureAd:ClientId" "$ACCT_APP"
set_secret Ftgo.AccountingService "KeyVault:LocalPfxPath" "$ACCT_PFX"

set_secret Ftgo.KitchenService "AzureAd:TenantId" "$TENANT_ID"

set_secret Ftgo.DeliveryService "AzureAd:TenantId" "$TENANT_ID"
set_secret Ftgo.DeliveryService "AzureAd:ClientId" "$DELIV_APP"

set_secret Ftgo.NotificationService "AzureAd:TenantId" "$TENANT_ID"
set_secret Ftgo.NotificationService "AzureAd:ClientId" "$NOTIF_APP"

echo "    user-secrets ✓"

echo "==> Setting gh repo secrets (workload-identity demo workflow)"
gh secret set AZURE_TENANT_ID         -b "$TENANT_ID"                       2>/dev/null
gh secret set AZURE_CLIENT_ID         -b "$DELIV_APP"                       2>/dev/null
gh secret set FTGO_RESTAURANTS_SCOPE  -b "api://${REST_APP}/.default"       2>/dev/null
echo "    gh secrets ✓"

cat <<EOF

============================================================
Provisioning complete. Next steps:

  # Build + run the BFF + APIs
  dotnet build EntraAuthPatterns.slnx
  dotnet run --project src/Ftgo.OrderService       # https://localhost:7102
  dotnet run --project src/Ftgo.RestaurantService  # https://localhost:7103
  dotnet run --project src/Ftgo.ApiGateway         # https://localhost:7101

  # Aspire telemetry dashboard (optional, requires docker)
  docker compose -f tests/local/docker-compose.yml up -d
  open http://localhost:18888

  # Workload-identity demo (no secrets stored locally)
  gh workflow run wi-demo.yml
============================================================
EOF
