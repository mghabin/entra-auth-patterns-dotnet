# Managed Identity (the cloud default)

**Use this whenever your workload runs on Azure compute** — App Service,
Container Apps, AKS, Functions, VMs, Logic Apps, Container Instances.

## What it is

Azure provisions an identity that **lives in the platform**, not in your
code or config. Your code asks the IMDS endpoint
(`http://169.254.169.254`) for a token; the platform proves your
identity to Entra; you get a JWT. No cert, no secret, no rotation.

Two flavors:
- **System-assigned**: lifecycle bound to the resource (delete the
  Container App → identity is gone). Simplest. Used by every service
  in this sample.
- **User-assigned**: standalone resource you create. Outlives any
  single workload, and one identity can be attached to many resources.
  Use when you need stable identity across deploys.

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
   ```

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
