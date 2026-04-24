# .NET Best Practices & Patterns

An opinionated reference compiled from Microsoft Learn, the .NET / ASP.NET team blogs,
`dotnet/runtime` docs, and widely-respected community voices. Every page ends with a
Sources section so you can follow the citations.

Target: **.NET 10 / C# 13**. Older versions are called out inline when guidance differs.

## How to read

- **`foundations.md`** — projects & solutions (slnx, Central Package Management,
  Directory.Build.props, analyzers, treat-warnings-as-errors), C# 13 language features,
  nullable reference types end-to-end, async (Cleary canon), DI & lifetimes.
- **`aspnetcore.md`** — Minimal vs Controllers, model binding & validation, ProblemDetails,
  versioning, OpenAPI, rate-limiting, output caching, OpenTelemetry wiring, resilience
  with `Microsoft.Extensions.Resilience`.
- **`data.md`** — EF Core (tracking, split queries, compiled queries, pooling, migrations,
  transactions), when to drop to Dapper/raw SQL, testing data access.
- **`testing.md`** — xUnit v3, FluentAssertions / Shouldly, NSubstitute vs Moq,
  `WebApplicationFactory`, Testcontainers, snapshot testing, mutation testing with Stryker.
- **`performance.md`** — BenchmarkDotNet methodology, allocation analysis, `Span<T>` /
  `Memory<T>`, pooling, source generators, NativeAOT trade-offs, server GC tuning.
- **`cloud-native.md`** — .NET Aspire app model, service discovery, OTel defaults, AKS
  liveness/readiness, graceful shutdown, container and image best practices.
- **`client.md`** — Blazor United / SSR / WASM render-mode trade-offs, MAUI architecture,
  MVVM with CommunityToolkit, navigation.
- **`checklist.md`** — one-page do/don't list for code review.
- **`monorepo-and-antipatterns.md`** — .NET monorepo strategy, 38 anti-patterns to avoid, positive patterns, GoF design patterns through a .NET lens.

## Using this in the repo

The `src/` sample in this repository applies these practices:

- Central Package Management (`Directory.Packages.props`)
- `Directory.Build.props` with `TreatWarningsAsErrors=true`, analyzers
  (Microsoft.CodeAnalysis.NetAnalyzers + Meziantou.Analyzer + SonarAnalyzer.CSharp),
  deterministic builds, NuGet audit
- `.editorconfig` with taste rules tuned for ASP.NET Core
- Source-generated `ILogger` (`LoggerMessage`) throughout
- OpenTelemetry traces + metrics (`TelemetryExtensions.AddEntraAuthTelemetry`)
- ProblemDetails + `IExceptionHandler` (`ProblemDetailsExtensions`)
- Typed HTTP clients + `AddStandardResilienceHandler` (`DownstreamApiServiceCollectionExtensions`)
- Options pattern with `ValidateDataAnnotations().ValidateOnStart()`
- Generic Host workers: `BackgroundService` + `IHostApplicationLifetime`

See `docs/sample-setup.md` for building and running it.
