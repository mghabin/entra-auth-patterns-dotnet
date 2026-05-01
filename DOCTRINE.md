# DOCTRINE — where canonical guidance lives

This repo is a **reference implementation**, not a doctrine source.
Every architectural rule it follows traces back to one of two upstream guides:

- [`mghabin/dotnet-engineering-guide`](https://github.com/mghabin/dotnet-engineering-guide) — language, framework, ASP.NET Core, testing, performance, cloud-native, client.
- [`mghabin/infra-engineering-guide`](https://github.com/mghabin/infra-engineering-guide) — IaC, CI/CD, containers/K8s, observability, security supply-chain, networking, data, reliability, FinOps, platform engineering, identity.

If a question is not answered by the table below or by [`coverage-map.md`](./coverage-map.md), **search those two repos first**, then [`docs/`](./docs/), then primary sources (RFC / Microsoft Learn / NIST / OWASP). Only after all four have been searched should new doctrine be authored — and new doctrine **belongs in the upstream guide it logically extends**, not here.

---

## How to use this map

- **Humans**: scan the relevant row, follow the link, read the chapter section, then return here for the entra-specific specialization.
- **AI agents working in this repo**: `.github/copilot-instructions.md` repeats this rule with a search-order contract. Honor it before generating code or docs.

The rule is one-way: this repo **cites** the upstream guides; the upstream guides do **not** cite this repo (this is a sample, not a source of truth).

---

## Doctrine reference table

| Topic in this repo | Upstream owner | Section / chapter |
| --- | --- | --- |
| ASP.NET Core auth (`scp` vs `roles`+`azp` named policies, JWT defaults, multi-tenant `IssuerValidator`, `tid` allow-list, deny-by-default) | dotnet-engineering-guide | [`docs/02-aspnetcore.md` §10 Authn/Authz](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md#10-authnauthz) |
| HTTP pipeline order, problem-details, model binding, minimal APIs vs MVC | dotnet-engineering-guide | [`docs/02-aspnetcore.md`](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md) |
| HttpClient + Polly resilience pipeline, named clients, timeouts | dotnet-engineering-guide | [`docs/02-aspnetcore.md`](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/02-aspnetcore.md), [`docs/05-performance.md`](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/05-performance.md) |
| Testing strategy (unit / integration / WebApplicationFactory / Testcontainers / contract) | dotnet-engineering-guide | [`docs/04-testing.md`](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/04-testing.md) |
| Allocation, async, perf hot-paths | dotnet-engineering-guide | [`docs/05-performance.md`](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/05-performance.md) |
| Cloud-native runtime (containers, health, config, secrets at runtime) | dotnet-engineering-guide | [`docs/06-cloud-native.md`](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/06-cloud-native.md) |
| C# language features, project layout, nullable, analyzers | dotnet-engineering-guide | [`docs/01-foundations.md`](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/01-foundations.md) |
| EF Core, Dapper, transactions, outbox | dotnet-engineering-guide | [`docs/03-data.md`](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/03-data.md) |
| Decision trees a senior .NET architect actually faces | dotnet-engineering-guide | [`docs/decision-trees.md`](https://github.com/mghabin/dotnet-engineering-guide/blob/main/docs/decision-trees.md) |
| IaC tooling decision (Bicep vs Terraform vs Pulumi), module shape | infra-engineering-guide | [`docs/02-iac-tooling.md`](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/02-iac-tooling.md) |
| CI/CD pipelines, image promotion by SHA, environments, gates | infra-engineering-guide | [`docs/03-ci-cd.md`](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/03-ci-cd.md) |
| Container build (chiseled / distroless / non-root), Kubernetes / ACA defaults | infra-engineering-guide | [`docs/04-containers-k8s.md`](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/04-containers-k8s.md) |
| Observability (OpenTelemetry → Azure Monitor / Loki / Tempo), SLOs | infra-engineering-guide | [`docs/05-observability.md`](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/05-observability.md) |
| Security supply-chain (SLSA, pinact, OpenSSF Scorecard, Dependabot, gitleaks, dependency-review, CodeQL) | infra-engineering-guide | [`docs/06-security-supply-chain.md`](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/06-security-supply-chain.md) |
| Networking (private endpoints, egress, VNet, WAF, mTLS) | infra-engineering-guide | [`docs/07-networking.md`](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/07-networking.md) |
| Data-state (managed DB defaults, backup, residency) | infra-engineering-guide | [`docs/08-data-state.md`](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/08-data-state.md) |
| Reliability (SLI / SLO / error budget, retry / timeout / circuit, blast-radius) | infra-engineering-guide | [`docs/09-reliability.md`](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/09-reliability.md) |
| FinOps (idle cost, scale-to-zero, ACR Basic, daily caps, teardown) | infra-engineering-guide | [`docs/10-finops.md`](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/10-finops.md) — repo-local specialization in [`docs/cost-zero.md`](./docs/cost-zero.md) |
| Platform engineering (golden paths, paved roads, multi-tenant compute) | infra-engineering-guide | [`docs/11-platform-engineering.md`](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/11-platform-engineering.md) |
| Workload identity (Entra workload-id, FIC, MI, OIDC issuer, SPIFFE) — *infra-side* | infra-engineering-guide | [`docs/12-identity.md`](https://github.com/mghabin/infra-engineering-guide/blob/main/docs/12-identity.md) |
| Entra app-side patterns: token acquisition, validation, credential picker, sample registrations | **this repo** | [`docs/`](./docs/) + [`coverage-map.md`](./coverage-map.md) |

---

## What lives in *this* repo (and nowhere else)

This is the **only** layer you should not look upstream for:

- The wiring of [`Microsoft.Identity.Web`](https://learn.microsoft.com/entra/identity-platform/microsoft-identity-web) into a multi-service ASP.NET Core sample.
- The `OrdersDelegated` / `OrdersApp` named-policy split that operationalizes dotnet-guide ch02 §10.
- The BFF-style `SignedAssertionFromManagedIdentity` pattern (clarified as **not** OIDC-FIC) and its disambiguation.
- The credential-picker for *Entra app-credentials* (MI / FIC / cert / secret) including the three documented exceptions for client secret.
- The per-environment app-registration and FIC creation Bicep modules, parameterized for dev / ppe / prod.
- The end-to-end runnable FTGO demo (ApiGateway, Auth, Auth.Client, Orders.Api, Restaurants.Api, Kitchen.Worker) that exercises all of the above on Azure Container Apps.

Everything else — language, framework, IaC, CI/CD, observability, FinOps, supply-chain — defers upstream. If a doctrine claim in this repo cannot be traced to a row in the table above or a primary source cited in the relevant doc's `## Sources` block, treat it as a bug and open an issue.

---

## Sources

- Microsoft.Identity.Web — [learn.microsoft.com/entra/identity-platform/microsoft-identity-web](https://learn.microsoft.com/entra/identity-platform/microsoft-identity-web)
- Microsoft identity platform overview — [learn.microsoft.com/entra/identity-platform/v2-overview](https://learn.microsoft.com/entra/identity-platform/v2-overview)
- dotnet-engineering-guide — [github.com/mghabin/dotnet-engineering-guide](https://github.com/mghabin/dotnet-engineering-guide)
- infra-engineering-guide — [github.com/mghabin/infra-engineering-guide](https://github.com/mghabin/infra-engineering-guide)
