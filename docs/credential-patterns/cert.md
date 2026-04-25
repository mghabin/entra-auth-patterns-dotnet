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
