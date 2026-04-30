# Certificate credential

**Use this only when both Managed Identity AND Workload Identity
Federation are unavailable.** That's a small set of scenarios in
2026:

- On-prem workloads with no OIDC-issuing IdP.
- HSM- or smartcard-bound identities for regulated industries (FIPS,
  PCI, healthcare) where the private key MUST never touch software.
- Bootstrap chicken-and-egg: the very first identity in a fresh data
  center before any IdP trust is wired.
- A handful of legacy Microsoft partner programs (CSP) that mandated
  cert chains; most have moved to FIC.

If you're on Azure compute, **don't use this**. Use
[Managed Identity](managed-identity.md). The MI can fetch a cert from
Key Vault if you really want a cert credential, but at that point you
might as well skip the cert and use the MI directly.

## What it is

You generate an asymmetric key pair, upload the public-key cert to
the Entra app reg, and the workload signs a JWT assertion with the
private key. Entra verifies the signature against the published
public key and issues an access token.

## Code (.NET, cert from disk)

```csharp
var cert = X509CertificateLoader.LoadPkcs12FromFile(
    "/path/to/cert.pfx",
    password: null,
    // EphemeralKeySet: hold the private key in process memory only;
    // do NOT persist a copy to the user/machine keystore on disk.
    // Required on Linux containers and recommended everywhere — it
    // prevents stale key material from leaking to the host filesystem
    // across container restarts and avoids ACL drift on Windows.
    keyStorageFlags: X509KeyStorageFlags.EphemeralKeySet);

var credential = new ClientCertificateCredential(
    tenantId, clientId, cert);

var token = await credential.GetTokenAsync(
    new TokenRequestContext(["api://orders/.default"]), ct);
```

## Code (.NET, cert from Key Vault — anti-pattern in 2026)

If you really need this pattern (e.g. an on-prem service that has
network access to Key Vault but no MI), the cert lives in KV and you
fetch it on boot:

```csharp
// 1. authenticate to KV with whatever non-cert credential you have
//    (in cloud, that would be MI — at which point: just use MI for
//     the API call too and skip the cert entirely)
var kvClient = new SecretClient(
    new Uri("https://kv-foo.vault.azure.net/"),
    new ManagedIdentityCredential());

// 2. fetch the cert + private key (KV stores them as a base64 PFX
//    in a secret named after the cert)
var pfx = await kvClient.GetSecretAsync("orders-client-cert");
var cert = X509CertificateLoader.LoadPkcs12(
    Convert.FromBase64String(pfx.Value.Value), password: null,
    keyStorageFlags: X509KeyStorageFlags.EphemeralKeySet);

// 3. now the cert credential
var credential = new ClientCertificateCredential(
    tenantId, clientId, cert);
```

## Rotation cadence

- **Must** rotate at least annually. The Entra app-registration UI
  permits up to 24 months on certificate credentials; **don't** use
  the maximum.
- **Best practice: rotate every 90 days** (matches the Let's Encrypt
  / public-CA cadence and the `openssl req -days 90 …` example below).
  Short lifetimes mean a leaked private key is useful to an attacker
  for a bounded window.
- **Should** automate via Azure Key Vault certificate auto-renewal +
  an event-grid hook that reuploads the new public-key portion to the
  Entra app reg via `az ad app credential reset --id $APP_ID --cert @cert.pem`.
  Manual rotation is error-prone: the failure mode is "expired cert in
  prod at 03:00" because the rotation runbook lived in someone's
  bookmarks.
- **Must** keep `notBefore` overlap windows (publish the new cert
  before retiring the old) so that in-flight requests don't see a
  rejected `kid`. Entra accepts multiple cert credentials per app reg
  for exactly this reason.
- **Must** monitor cert expiry. `az ad app credential list --id $APP_ID`
  in a scheduled job, alerting at T-30d / T-7d / T-1d.

## Setup

1. Generate a cert (`openssl req -x509 -newkey rsa:2048 -days 90 …`).
2. Upload the public-key portion to the Entra app reg
   (`az ad app credential reset --id $APP_ID --cert @cert.pem`).
3. Get the private key onto the workload securely. **This is the hard
   part.** That problem doesn't exist with MI or FIC.
4. Rotate before expiry. Forever. **This is the second hard part.**

## Why we used to ship this and don't anymore

The deleted `Ftgo.AccountingService` demonstrated this pattern. In
cloud, it was "MI fetches cert from KV → cert auth to Entra" — which
is two hops to do what MI does in one, with the cert in the middle
adding nothing except more things to rotate.

We kept the pattern in this doc because the on-prem and HSM-bound
scenarios are real. We dropped the deployed service because it
taught the wrong default for cloud.

## Cross-references

- Acquisition wiring (which library, scope strings) — [`acquisition.md` §1c](../acquisition.md#1c-certificate-on-an-app-registration--acceptable-when-mific-unavailable).
- Rotation as a cross-cutting operational concern — [`best-practices.md` — Credentials](../best-practices.md#credentials).
- Picker decision (when MI vs FIC vs cert) — [`index.md`](index.md#decision-matrix).
- Sibling credential patterns — [`managed-identity.md`](managed-identity.md), [`federated-identity.md`](federated-identity.md), [`client-secret.md`](client-secret.md).
- Per-scenario credential picker — [`matrix.md`](../matrix.md).
- Auth-policy doctrine (delegated vs app-only) — dotnet-engineering-guide [ch02 §10](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz).

---

## Sources

- Microsoft identity platform — Certificate credentials for application authentication — [learn.microsoft.com/entra/identity-platform/certificate-credentials](https://learn.microsoft.com/entra/identity-platform/certificate-credentials)
- `ClientCertificateCredential` reference — [learn.microsoft.com/dotnet/api/azure.identity.clientcertificatecredential](https://learn.microsoft.com/dotnet/api/azure.identity.clientcertificatecredential)
- `X509KeyStorageFlags.EphemeralKeySet` — [learn.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509keystorageflags](https://learn.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509keystorageflags)
- Azure Key Vault — Certificate auto-renewal — [learn.microsoft.com/azure/key-vault/certificates/tutorial-rotate-certificates](https://learn.microsoft.com/azure/key-vault/certificates/tutorial-rotate-certificates)
- OWASP — Cryptographic Storage Cheat Sheet — [cheatsheetseries.owasp.org/cheatsheets/Cryptographic_Storage_Cheat_Sheet.html](https://cheatsheetseries.owasp.org/cheatsheets/Cryptographic_Storage_Cheat_Sheet.html)
- OWASP — Key Management Cheat Sheet — [cheatsheetseries.owasp.org/cheatsheets/Key_Management_Cheat_Sheet.html](https://cheatsheetseries.owasp.org/cheatsheets/Key_Management_Cheat_Sheet.html)
- NIST SP 800-57 Part 1 Rev. 5 — Recommendation for Key Management — [nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-57pt1r5.pdf](https://nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-57pt1r5.pdf)
- FIPS 140-2 — Security Requirements for Cryptographic Modules — [csrc.nist.gov/publications/detail/fips/140/2/final](https://csrc.nist.gov/publications/detail/fips/140/2/final)
- PCI DSS v4.0 — Requirement 3 (key management) — [pcisecuritystandards.org/document_library/](https://www.pcisecuritystandards.org/document_library/)
