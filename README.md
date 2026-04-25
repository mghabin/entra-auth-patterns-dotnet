# entra-auth-patterns-dotnet

[![CI](https://github.com/mghabin/entra-auth-patterns-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/mghabin/entra-auth-patterns-dotnet/actions/workflows/ci.yml)
[![CD](https://github.com/mghabin/entra-auth-patterns-dotnet/actions/workflows/cd.yml/badge.svg)](https://github.com/mghabin/entra-auth-patterns-dotnet/actions/workflows/cd.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)

A working reference for **acquiring** and **validating** Microsoft Entra ID
tokens in .NET 10 server-side apps. Covers app tokens (S2S / daemon) and
user tokens (delegated / OBO) across every credential type: Managed
Identity, Workload Identity Federation, Certificates, Client Secrets,
and `TokenCredential` (Azure.Identity).

The sample uses **Chris Richardson's FTGO domain** (Food-To-Go, from
*Microservices Patterns*) so each service has a recognisable
business-capability name and demonstrates exactly one Entra auth shape.

## What's in here

| Service                       | Auth shape demonstrated                                |
|-------------------------------|--------------------------------------------------------|
| `Ftgo.ApiGateway`             | BFF: user OIDC sign-in → OBO + S2S fan-out             |
| `Ftgo.OrderService`           | Single-tenant resource API (delegated **or** app)      |
| `Ftgo.RestaurantService`      | Multi-tenant resource API (app-only, tenant allow-list)|
| `Ftgo.KitchenService`         | Worker — **Managed Identity**                          |
| `Ftgo.AccountingService`      | Worker — **Certificate** (Key Vault-backed)            |
| `Ftgo.DeliveryService`        | Worker — **Workload Identity Federation** (GitHub OIDC)|
| `Ftgo.NotificationService`    | Worker — **Client Secret** (anti-pattern, for contrast)|
| `Ftgo.Auth`                   | One-line `AddEntraAuth(...)` library                   |

## Quick start

```bash
git clone https://github.com/mghabin/entra-auth-patterns-dotnet.git
cd entra-auth-patterns-dotnet
./scripts/deploy.sh                # 7 app regs + permissions + cert + FIC (Bicep IaC)
dotnet build EntraAuthPatterns.slnx
dotnet test  EntraAuthPatterns.slnx
```

Full free-tier walkthrough → [`docs/run-locally.md`](docs/run-locally.md).

## Deploy to the cloud

Free-tier Azure Container Apps deployment with **dev → ppe → prod** promotion via GitHub Actions OIDC, image promotion by SHA, App Insights observability, zero stored client secrets:

```bash
./scripts/bootstrap-env.sh ENV=dev   # one-time per env
git push origin main                 # auto-deploys to dev → ppe → prod (with reviewer gate)
```

Costs **$0/mo at idle** (scale-to-zero) and ~$3-5/mo with prod always-on. Full guide → [`docs/deploy-cloud.md`](docs/deploy-cloud.md), promotion model → [`docs/environments.md`](docs/environments.md).

## Docs

### Entra patterns
- [Acquisition](docs/acquisition.md) — how to get a token (app vs user) with each credential and library.
- [Validation](docs/validation.md) — how to validate incoming tokens server-side.
- [Matrix](docs/matrix.md) — one-page comparison of scenarios.
- [Best practices](docs/best-practices.md) — short, opinionated checklist.
- [Shared auth platform (AKS, multi-product)](docs/aks-shared-infra.md) — when you have N services and M products and want to stop re-implementing auth in every repo.
- [Sample setup](docs/sample-setup.md) — Entra app registrations, role grants, projects under `src/`.
- [Run locally (free)](docs/run-locally.md) — bootstrap on just your existing GitHub + Entra tenant.

### .NET engineering practices applied in `src/`

The general .NET / ASP.NET Core / monorepo guidance that `src/` follows lives in
its own repo so it can be read on its own:
👉 **[mghabin/dotnet-engineering-guide](https://github.com/mghabin/dotnet-engineering-guide)**
— foundations, ASP.NET Core, data, testing, performance, cloud-native, client,
monorepo + anti-patterns, and a one-page review checklist.

## Decision tree

```
Who is calling?
├── A user (interactive client → your API)
│   └── Acquire: client does auth-code+PKCE; your API receives a USER token.
│       └── Need to call a downstream API as that user? → On-Behalf-Of (OBO).
│
└── An app/workload (no user)
    └── Acquire: client_credentials → APP token.
        ├── Running in Azure (App Service / Functions / AKS / VM / Container Apps)?
        │   └── Use Managed Identity (system or user-assigned).         ← prefer
        ├── Running in another cloud / GitHub Actions / on-prem k8s?
        │   └── Use Workload Identity Federation (FIC), no secret.      ← prefer
        ├── Need a portable credential where MI/FIC is not possible?
        │   └── Use a Certificate on an app registration.               ← acceptable
        └── Last resort: Client Secret on an app registration.          ← avoid
```

## Library cheat-sheet

| Library | Best for | Notes |
|---|---|---|
| **Microsoft.Identity.Web** | ASP.NET Core APIs / web apps | Wraps MSAL + JwtBearer; handles validation, OBO, token cache. Default choice for ASP.NET Core. |
| **MSAL.NET** (`Microsoft.Identity.Client`) | Non-ASP.NET hosts (workers, libraries) needing Entra-specific features (OBO, claims challenges, CAE, broker) | Lower-level than Microsoft.Identity.Web. |
| **Azure.Identity** (`TokenCredential`) | Calling **Azure resources** (Storage, Key Vault, Cosmos, Service Bus, Graph via SDK, etc.) | `DefaultAzureCredential` chains MI, env, VS, CLI. Not for arbitrary OAuth flows; does not implement OBO. |

## Contributing

Issues and PRs welcome. Please read [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md)
and report security issues per [`SECURITY.md`](SECURITY.md).

## License

[MIT](LICENSE) © 2026 mghabin.
