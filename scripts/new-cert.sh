#!/usr/bin/env bash
# scripts/new-cert.sh
#
# Generates a self-signed certificate for ftgo-accountingservice and
# uploads the public key to the app registration. The .pfx is saved
# locally; **do not commit it** (the .gitignore excludes *.pfx).
#
# Prerequisites: openssl, az CLI logged in.

set -euo pipefail

APP_NAME="${APP_NAME:-ftgo-accountingservice}"
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
[[ -z "$APP_ID" ]] && { echo "ERROR: app '$APP_NAME' not found. Run scripts/setup-entra.sh first." >&2; exit 1; }

echo "==> Uploading public key to app registration ($APP_NAME / $APP_ID)"
az ad app credential reset --id "$APP_ID" --cert "@${CRT}" --append >/dev/null

echo
echo "============================================================"
echo "Cert ready."
echo "  PFX:        $PFX"
echo "  Thumbprint: $THUMB"
echo "============================================================"
cat <<EOF

# Tell AccountingService where to read it (local dev: file path; prod: Key Vault)
dotnet user-secrets --project src/Ftgo.AccountingService set "KeyVault:Uri"      "https://example-kv.vault.azure.net/"
dotnet user-secrets --project src/Ftgo.AccountingService set "KeyVault:CertName" "${APP_NAME}"
EOF
