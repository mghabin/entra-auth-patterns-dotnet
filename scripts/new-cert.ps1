# scripts/new-cert.ps1 — self-signed cert for ftgo-accountingservice.
[CmdletBinding()]
param(
    [string]$AppName = 'ftgo-accountingservice',
    [string]$OutDir  = '.\.certs',
    [int]$Days       = 365
)
$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$cert = New-SelfSignedCertificate `
    -Subject "CN=$AppName" `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyAlgorithm RSA -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -NotAfter (Get-Date).AddDays($Days)

$pfx = Join-Path $OutDir "$AppName.pfx"
$cer = Join-Path $OutDir "$AppName.cer"
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password (ConvertTo-SecureString -String '' -AsPlainText -Force) | Out-Null
Export-Certificate    -Cert $cert -FilePath $cer | Out-Null

$appId = az ad app list --display-name $AppName --query '[0].appId' -o tsv
if (-not $appId) { throw "App '$AppName' not found — run setup-entra.ps1 first." }

az ad app credential reset --id $appId --cert "@$cer" --append | Out-Null

Write-Host "PFX:        $pfx"
Write-Host "Thumbprint: $($cert.Thumbprint.ToLower())"
Write-Host @"
dotnet user-secrets --project src/Ftgo.AccountingService set "KeyVault:Uri"      "https://example-kv.vault.azure.net/"
dotnet user-secrets --project src/Ftgo.AccountingService set "KeyVault:CertName" "$AppName"
"@
