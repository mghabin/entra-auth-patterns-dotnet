#!/usr/bin/env bash
# scripts/setup.sh — orchestrator: app regs + cert + reminders.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"

echo "==> Step 1/2: Entra app registrations + federated credential"
"$HERE/setup-entra.sh"

echo
echo "==> Step 2/2: Self-signed certificate for AccountingService"
"$HERE/new-cert.sh"

cat <<'EOF'

============================================================
All done. Final manual steps (one-time, in the portal):

  1. Admin-consent the API permissions for each app reg.
  2. Expose scope 'orders.read' on ftgo-orderservice.
  3. Expose app roles 'Orders.Process' (on ftgo-orderservice) and
     'Restaurants.Read.All' (on ftgo-restaurantservice).
  4. Set 'requestedAccessTokenVersion = 2' on each API app reg.

Then run the sample:
  dotnet build EntraAuthPatterns.slnx
  dotnet run --project src/Ftgo.OrderService
  dotnet run --project src/Ftgo.RestaurantService
  dotnet run --project src/Ftgo.ApiGateway

  docker compose -f tests/local/docker-compose.yml up -d
  open http://localhost:18888  # Aspire Dashboard
============================================================
EOF
