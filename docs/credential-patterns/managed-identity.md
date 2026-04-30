# Managed Identity (the cloud default)

**Use this whenever your workload runs on Azure compute** — App Service,
Container Apps, AKS, Functions, VMs, Logic Apps, Container Instances.

## What it is

Azure provisions an identity that **lives in the platform**, not in your
code or config. Your code asks the IMDS endpoint
(`http://169.254.169.254`) for a token; the platform proves your
identity to Entra; you get a JWT. No cert, no secret, no rotation.

Two flavors:

- **System-assigned (SAMI)**: lifecycle bound to the resource (delete the
  Container App → identity is gone). Simplest. One identity per
  resource — narrowest blast radius. Used by every service in this
  sample.
- **User-assigned (UAMI)**: standalone resource you create. Outlives any
  single workload, and one identity can be attached to many resources.
  Use when you need stable identity across deploys, or when several
  replicas / regional deployments of the *same* workload must share a
  principal.

**Security implication of UAMI sharing.** Reusing a single UAMI across
unrelated resources widens the blast radius — a compromise of any one
host that has the UAMI attached lets the attacker mint tokens with the
union of every role assignment that UAMI holds. **For high-sensitivity
workloads, prefer SAMI (one per resource), or a dedicated UAMI per
workload (not per cluster, not per subscription).** Treat a UAMI like
you would a service principal: one workload, one identity. See
[`glossary.md` — Managed Identity (MI)](../../glossary.md#managed-identity-mi--system-assigned-sami-user-assigned-uami)
for the canonical definitions.

## Code (worker calling an Entra-protected API)

```csharp
// Ftgo.Kitchen.Worker / ManagedIdentityTokenProvider.cs
private readonly TokenCredential _credential =
    new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);

public async ValueTask<string> GetAccessTokenAsync(string scope, CancellationToken ct)
{
    var token = await _credential.GetTokenAsync(
        new TokenRequestContext([scope]), ct);
    return token.Token;
}
```

`scope` here is e.g. `api://<orders-api-app-id>/.default` for an
app-only token, or `https://graph.microsoft.com/.default` for a Graph
app-only token. **Same code for both** — only the scope changes.

## Setup

1. Enable a system-assigned identity on the Container App
   (`identity: { type: 'SystemAssigned' }` in bicep — see
   `infra/bicep/modules/container-app.bicep`).
2. Grant the **MI's service principal** the role on the resource
   (e.g. `Orders.Process` on the OrdersApi app reg, or `User.Read.All`
   on the Microsoft Graph SP).

For app roles on a custom API:

```bicep
resource grant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  principalId: kitchenWorkerMi.principalId
  resourceId:  ordersApiSp.id
  appRoleId:   ordersProcessRoleId
}
```

For Microsoft Graph app roles, the resourceId is the Graph SP's
objectId (`00000003-0000-0000-c000-000000000000` is Graph's appId; its
SP is per-tenant).

## Calling Microsoft Graph with an MI

You don't need a certificate. You don't need a secret. You don't
need a worker app reg. You need:

1. The MI (you already have it).
2. Graph permission granted to the MI's SP. You can't do this in the
   Azure portal UI — use Graph PowerShell or `az rest`. Example to
   grant `User.Read.All`:

   ```bash
   GRAPH_SP_ID=$(az ad sp show --id 00000003-0000-0000-c000-000000000000 --query id -o tsv)
   USER_READ_ALL=$(az ad sp show --id 00000003-0000-0000-c000-000000000000 \
     --query "appRoles[?value=='User.Read.All'].id | [0]" -o tsv)
   az rest --method POST \
     --url "https://graph.microsoft.com/v1.0/servicePrincipals/${MI_PRINCIPAL_ID}/appRoleAssignments" \
     --body "{\"principalId\":\"${MI_PRINCIPAL_ID}\",\"resourceId\":\"${GRAPH_SP_ID}\",\"appRoleId\":\"${USER_READ_ALL}\"}"
   ```

3. In code, just request a Graph token:

   ```csharp
   var token = await new ManagedIdentityCredential().GetTokenAsync(
       new TokenRequestContext(["https://graph.microsoft.com/.default"]), ct);
   // Failure mode: if the MI's SP is missing the Graph app role
   // (e.g. User.Read.All not granted at step 2 above), the call throws
   // Azure.Identity.AuthenticationFailedException with an inner
   // MsalServiceException carrying AADSTS500011 / AADSTS65001-style
   // "no app role assignment" detail. Fix is at the role-assignment
   // layer (Graph), not in code.
   ```

## DefaultAzureCredential vs ManagedIdentityCredential

In this sample, **deployed code uses `ManagedIdentityCredential`
explicitly** (not `DefaultAzureCredential`). Reasons:

- **Predictable failure mode.** `DefaultAzureCredential` walks a chain
  (env vars → workload identity → MI → VS → Azure CLI → …). On Azure
  compute the MI step succeeds and the rest is dead weight; in a
  misconfigured env it can silently fall through to a developer's
  `az login` cached token, masking a real wiring bug.
- **Fewer round-trips.** No probing of credential sources that will
  never be available in production.
- **Local dev still works.** `run-locally.md` documents using
  `DefaultAzureCredential` (or `AzureCliCredential`) on a developer
  machine where there is no MI; production code paths stay explicit.

See [`glossary.md` — DefaultAzureCredential](../../glossary.md#defaultazurecredential)
and [`acquisition.md` §1a](../acquisition.md#1a-managed-identity--preferred-when-in-azure)
for the acquisition-side wiring.

## Why this is the default

- **No secrets.** Nothing to rotate, nothing to leak in logs or
  config files, nothing to commit by accident.
- **Tenant-bound.** The identity exists only inside your subscription
  and tenant. Deleting the resource deletes the identity.
- **Cheaper.** No Key Vault round-trip, no cert lifecycle, no FIC
  trust to maintain.
- **Same surface as everything else.** `TokenCredential` is the
  Azure SDK base type — works with Azure Storage, Cosmos DB, Service
  Bus, your own APIs, and Microsoft Graph identically.

## Cross-references

- Acquisition wiring (which library, caching, scope strings) — [`acquisition.md` §1a](../acquisition.md#1a-managed-identity--preferred-when-in-azure).
- Receiver-side validation of MI tokens (`idtyp`, `azp` allow-list) — [`validation.md` §5](../validation.md#5-mi-tokens).
- Picker decision (when MI vs FIC vs cert) — [`index.md`](index.md#decision-matrix).
- Sibling credential patterns — [`federated-identity.md`](federated-identity.md), [`cert.md`](cert.md), [`client-secret.md`](client-secret.md).
- Local-dev fallback when there is no MI — [`run-locally.md`](../run-locally.md).
- Auth-policy doctrine (delegated vs app-only, never OR-claims) — dotnet-engineering-guide [ch02 §10](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz).

---

## Sources

- Managed identities for Azure resources — overview — [learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview](https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview)
- System-assigned vs user-assigned managed identity — [learn.microsoft.com/entra/identity/managed-identities-azure-resources/managed-identities-faq](https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/managed-identities-faq)
- How to use managed identities with Azure Container Apps — [learn.microsoft.com/azure/container-apps/managed-identity](https://learn.microsoft.com/azure/container-apps/managed-identity)
- Azure Instance Metadata Service (IMDS) — [learn.microsoft.com/azure/virtual-machines/instance-metadata-service](https://learn.microsoft.com/azure/virtual-machines/instance-metadata-service)
- How managed identities work with virtual machines (IMDS token endpoint) — [learn.microsoft.com/entra/identity/managed-identities-azure-resources/how-managed-identities-work-vm](https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/how-managed-identities-work-vm)
- `ManagedIdentityCredential` reference — [learn.microsoft.com/dotnet/api/azure.identity.managedidentitycredential](https://learn.microsoft.com/dotnet/api/azure.identity.managedidentitycredential)
- `DefaultAzureCredential` reference — [learn.microsoft.com/dotnet/api/azure.identity.defaultazurecredential](https://learn.microsoft.com/dotnet/api/azure.identity.defaultazurecredential)
- Azure SDK for .NET — Identity client library — [learn.microsoft.com/dotnet/api/overview/azure/identity-readme](https://learn.microsoft.com/dotnet/api/overview/azure/identity-readme)
- Azure SDK identity guidance — [github.com/Azure/azure-sdk-for-net/blob/main/sdk/identity/Azure.Identity/README.md](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/identity/Azure.Identity/README.md)
- Granting Microsoft Graph app roles to a managed identity — [learn.microsoft.com/graph/permissions-grant-via-msgraph-rest-api](https://learn.microsoft.com/graph/permissions-grant-via-msgraph-rest-api)
