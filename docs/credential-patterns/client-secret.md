# Client secret (the anti-pattern)

**Don't use this.** Almost ever. This page exists so you recognise
the shape of the problem when reviewing legacy code.

## What it is

You generate a string ("secret"), paste it into the Entra app reg's
**Certificates & secrets** blade, copy it back out, and give it to
your workload. The workload includes it as `client_secret` in every
token request. Entra compares it to the stored hash and issues a
token if they match.

It's a shared password.

## Code (.NET)

```csharp
var credential = new ClientSecretCredential(
    tenantId, clientId,
    Environment.GetEnvironmentVariable("FTGO_CLIENTSECRET"));
```

## Why this is bad

1. **Shared symmetric secret.** Anyone with read access to the host's
   env vars / config / logs / process memory can mint tokens as your
   service.
2. **Rotation is a maintenance burden** that gets neglected. Most
   leaked-secret incidents involve a secret older than the engineer
   who left it.
3. **Limited TTL.** Entra caps client secret lifetimes at 24 months
   (recently shortened from infinite). Forced rotation cycles
   regularly. With MI or FIC there's nothing to rotate.
4. **Audit story is bad.** A token request signed by a secret tells
   the audit log nothing about *which workload* made the request.
   The MI assertion or FIC subject claim does.
5. **Easy to commit by accident.** `git push` of `appsettings.json`
   with the secret still in it. The whole class of secret-scanning
   tooling exists because of this pattern.

## When it's actually OK

- Local dev where the workload genuinely cannot use MI / FIC and
  doing so would block iteration.
- Quick spike / proof-of-concept code with a 1-hour TTL secret in a
  scratch tenant. Delete after.
- Migrating a legacy system: secret stays as the "before" while you
  build the cert/FIC/MI replacement; secret gets revoked the moment
  the migration cuts over.

## Why we used to ship this and don't anymore

The deleted `Ftgo.NotificationService` was deliberately built around
`ClientSecretCredential` to demonstrate the worst-case. Even with the
big "DON'T DO THIS" comment on it, having it deployed in cloud risked
new contributors copying the pattern. Removing it removes the
copy-paste vector. The pattern survives here as a reference.

If you find this pattern in your codebase, the migration path is:
1. Identify the workload's runtime (Azure compute → MI; non-Azure
   compute with OIDC → FIC; truly unable to do either → cert).
2. Add the new credential alongside the secret.
3. Cut over and revoke the secret in Entra.
4. Delete the env var, the secret-fetching code, and the secret
   from any KV / config store.
