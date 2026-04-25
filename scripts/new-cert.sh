#!/usr/bin/env bash
# scripts/new-cert.sh — generate a self-signed cert for an app registration and upload the
# public key. Idempotent. The .pfx stays local; do NOT commit it.
#
# Usage:  APP_NAME=ftgo-local-apigateway ./scripts/new-cert.sh
#
# Prereqs: openssl, az CLI logged in.

set -euo pipefail

APP_NAME="${APP_NAME:-ftgo-local-accountingservice}"
OUT_DIR="${OUT_DIR:-./.certs}"
SUBJECT="${SUBJECT:-/CN=${APP_NAME}}"
DAYS="${DAYS:-365}"
PFX_PASS="${PFX_PASS:-}"

mkdir -p "$OUT_DIR"

KEY="$OUT_DIR/${APP_NAME}.key"
CRT="$OUT_DIR/${APP_NAME}.crt"
PFX="$OUT_DIR/${APP_NAME}.pfx"

echo "==> Generating self-signed cert ($DAYS days)"
openssl req -x509 -newkey rsa:2048 -nodes -sha256 \
  -days "$DAYS" \
  -subj "$SUBJECT" \
  -keyout "$KEY" -out "$CRT"

echo "==> Bundling into PFX (no passphrase by default — set PFX_PASS to override)"
openssl pkcs12 -export -inkey "$KEY" -in "$CRT" -out "$PFX" -password "pass:${PFX_PASS}"

THUMB=$(openssl x509 -in "$CRT" -noout -fingerprint -sha1 | cut -d= -f2 | tr -d ':' | tr 'A-Z' 'a-z')
APP_ID=$(az ad app list --display-name "$APP_NAME" --query "[0].appId" -o tsv)
[[ -z "$APP_ID" ]] && { echo "ERROR: app '$APP_NAME' not found. Run scripts/deploy.sh first." >&2; exit 1; }

EXISTING=$(az ad app credential list --id "$APP_ID" \
  --query "[?customKeyIdentifier!=null] | [?ends_with(tolower(customKeyIdentifier), '${THUMB}')]" \
  -o tsv 2>/dev/null || true)

if [[ -n "$EXISTING" ]]; then
  echo "==> Cert with thumbprint $THUMB already attached to $APP_NAME — skipping upload."
else
  echo "==> Uploading public key to app registration ($APP_NAME / $APP_ID)"
  az ad app credential reset --id "$APP_ID" --cert "@${CRT}" --append >/dev/null
fi

echo
echo "============================================================"
echo "Cert ready."
echo "  PFX:        $PFX"
echo "  Thumbprint: $THUMB"
echo "============================================================"
