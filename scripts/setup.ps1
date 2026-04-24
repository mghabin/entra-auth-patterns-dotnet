# scripts/setup.ps1 — orchestrator.
[CmdletBinding()] param()
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
& "$here/setup-entra.ps1"
& "$here/new-cert.ps1"
Write-Host @"

============================================================
All done. Final manual steps (one-time, in the portal):
  1. Admin-consent the API permissions for each app reg.
  2. Expose scope 'orders.read' on ftgo-orderservice.
  3. Expose app roles 'Orders.Process' (orderservice) and
     'Restaurants.Read.All' (restaurantservice).
  4. Set 'requestedAccessTokenVersion = 2' on each API app reg.
============================================================
"@
