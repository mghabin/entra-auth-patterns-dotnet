# Credential patterns for Entra ID

When a workload acquires a token from Microsoft Entra to call a protected
API, it has to prove its identity. The "credential pattern" is *how* it
proves that identity. Picking the right one is the single most important
operational decision in any Entra-integrated app.

This sample is opinionated: **on Azure compute, use Managed Identity.
For everything else, use Workload Identity Federation. Stop using certs
and secrets unless you genuinely cannot avoid them.**

## Decision matrix

| Where does your workload run?               | Credential                                                       |
|---------------------------------------------|-----------------------------------------------------------------:|
| Azure (App Service, ACA, AKS, Functions, …) | **[Managed Identity](managed-identity.md)**                      |
| GitHub Actions                              | **[Workload Identity Federation](federated-identity.md)**        |
| GKE / EKS / on-prem K8s with SPIFFE / OIDC  | **[Workload Identity Federation](federated-identity.md)**        |
| On-prem service with no IdP / HSM-bound key | [Certificate](cert.md)                                           |
| Anything else                               | … you almost certainly don't need [client secret](client-secret.md) |

## Why this sample stopped using credential menagerie services

Earlier revisions of this sample shipped four worker services
(`AccountingService`, `DeliveryService`, `NotificationService`) — one per credential type — to teach the patterns
side-by-side. We deleted three of them after realising they were
either broken in cloud (FIC env vars don't exist on Azure Container
Apps; secret env var was never set) or had dead env-var wiring (the
worker code never read the `AzureAd:ClientCredentials` config we
populated; it had its own custom `IAppTokenProvider`).

The kept worker, **`Ftgo.Kitchen.Worker`**, uses the canonical Azure
pattern: `ManagedIdentityCredential` with `api://orders/.default`,
admin-consented at the MI's service principal. It is the credential
pattern any new Azure-resident worker should follow.

The other three patterns survive as 1-page references here.
