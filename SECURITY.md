# Security Policy

This is a **sample / reference repository**. It is not a hardened
production system.

## Scope

Do **not** rely on this code as-is to protect real workloads. The purpose
is to teach Microsoft Entra ID auth patterns; production deployments
must apply the hardening notes inline in the code and in `docs/`.

## Reporting a vulnerability

If you find a security issue in this repository, please **do not** open
a public GitHub issue. Instead, use GitHub's private vulnerability
reporting:

1. Open the repository's **Security** tab.
2. Click **Report a vulnerability**.
3. Describe the issue and (if possible) the fix.

You can expect an initial response within a few days. Critical issues
will be patched on the `main` branch and called out in the release
notes.

## Known caveats

The following items are **intentionally** present as teaching contrast
and should not be copied into production code:

- `Ftgo.NotificationService` uses a **client secret** — the worst
  credential type. The file documents why and points at better options.
- Some package versions may carry `NU1902`/`NU1903` advisories; these
  surface as build warnings on purpose so they're visible. See the
  [dotnet-engineering-guide checklist](https://github.com/mghabin/dotnet-engineering-guide/blob/main/checklist.md)
  for the rotation policy.

## What we do guarantee

- Secrets are never committed. `appsettings*.json` only carries
  zero-GUID placeholders; real values live in `dotnet user-secrets`,
  Key Vault, or environment variables.
- The CI workflow runs build + tests + `dotnet format --verify-no-changes`
  on every PR.
