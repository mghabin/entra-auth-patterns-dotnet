# syntax=docker/dockerfile:1.7
# Multi-service .NET 10 ASP.NET Core Dockerfile.
# One file, parameterized via PROJECT build-arg — same image recipe, different entry point per service.
#
# Build:
#   docker build --build-arg PROJECT=Ftgo.ApiGateway -t ftgo-apigateway .
#
# Base images: distroless chiseled (~95 MB final image, runs as non-root uid 11654 by default).
# Auth tokens / JWT validation are culture-invariant, so plain noble-chiseled (no ICU/tzdata) is sufficient.

ARG PROJECT
# TARGETARCH is auto-populated by buildx for multi-arch builds; we apply a safe
# default at each `dotnet` use site (${TARGETARCH:-amd64}) so single-arch builds
# without an explicit --platform still work. Matches dotnet/dotnet-docker samples.
ARG TARGETARCH

# ─── Stage 1: restore (cached unless csprojs or central package files change) ───
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-noble AS restore
ARG PROJECT
ARG TARGETARCH
WORKDIR /src

# Central props affect every restore — copy first.
# .editorconfig is required so analyzers (Meziantou, etc.) honor per-rule
# severity overrides during `dotnet publish` inside the container.
COPY Directory.Build.props Directory.Packages.props .editorconfig EntraAuthPatterns.slnx ./

# Copy ALL service csprojs (small files; this layer is cached as long as none of them change).
# Including all of them lets us share one restore layer across services that ProjectReference each other.
COPY src/Ftgo.Auth/Ftgo.Auth.csproj                         src/Ftgo.Auth/
COPY src/Ftgo.Auth.Client/Ftgo.Auth.Client.csproj           src/Ftgo.Auth.Client/
COPY src/Ftgo.ApiGateway/Ftgo.ApiGateway.csproj             src/Ftgo.ApiGateway/
COPY src/Ftgo.OrderService/Ftgo.OrderService.csproj         src/Ftgo.OrderService/
COPY src/Ftgo.RestaurantService/Ftgo.RestaurantService.csproj src/Ftgo.RestaurantService/
COPY src/Ftgo.KitchenService/Ftgo.KitchenService.csproj     src/Ftgo.KitchenService/
COPY src/Ftgo.AccountingService/Ftgo.AccountingService.csproj src/Ftgo.AccountingService/
COPY src/Ftgo.DeliveryService/Ftgo.DeliveryService.csproj   src/Ftgo.DeliveryService/
COPY src/Ftgo.NotificationService/Ftgo.NotificationService.csproj src/Ftgo.NotificationService/
COPY src/Ftgo.ApiGateway/packages.lock.json                 src/Ftgo.ApiGateway/
COPY src/Ftgo.OrderService/packages.lock.json               src/Ftgo.OrderService/
COPY src/Ftgo.RestaurantService/packages.lock.json          src/Ftgo.RestaurantService/
COPY src/Ftgo.KitchenService/packages.lock.json             src/Ftgo.KitchenService/
COPY src/Ftgo.AccountingService/packages.lock.json          src/Ftgo.AccountingService/
COPY src/Ftgo.DeliveryService/packages.lock.json            src/Ftgo.DeliveryService/
COPY src/Ftgo.NotificationService/packages.lock.json        src/Ftgo.NotificationService/
COPY src/Ftgo.Auth/packages.lock.json                       src/Ftgo.Auth/
COPY src/Ftgo.Auth.Client/packages.lock.json                src/Ftgo.Auth.Client/

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore -a "${TARGETARCH:-amd64}" src/${PROJECT}/${PROJECT}.csproj
# NOTE: --locked-mode intentionally NOT used here. CI (`ci.yml`) restores the
# whole solution with --locked-mode on every PR/push, so lock-file integrity
# is already enforced before any image is built. Adding it here would conflict
# with the per-RID restore (lock files don't include runtime identifiers, so
# `-a $TARGETARCH` produces an RID set the lock file never recorded → NU1004).
# Matches dotnet/dotnet-docker official samples.

# ─── Stage 2: publish ───
FROM restore AS publish
ARG PROJECT
ARG TARGETARCH
ARG BUILD_GIT_SHA=local
ARG BUILD_VERSION=0.0.0-local
COPY src/ src/
# Explicit id=nuget on the cache mount ensures BuildKit shares the cache contents
# with the restore stage above (without an explicit id, BuildKit may scope the
# mount per-stage, and `dotnet publish --no-restore` then can't find analyzer
# packages like AsyncFixer → NETSDK1064). Pattern from dotnet/dotnet-docker
# issue #3353.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish src/${PROJECT}/${PROJECT}.csproj \
        -a "${TARGETARCH:-amd64}" \
        --no-restore \
        -c Release \
        -o /app \
        -p:UseAppHost=false \
        -p:DebugType=embedded \
        -p:Version=$BUILD_VERSION \
        -p:SourceRevisionId=$BUILD_GIT_SHA \
    && for f in /app/${PROJECT}.*; do mv "$f" "/app/app.${f#/app/${PROJECT}.}"; done

# ─── Stage 3: runtime (chiseled, ~95 MB, runs as uid 11654 non-root by default) ───
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
ARG PROJECT
ARG BUILD_GIT_SHA=local
ARG BUILD_VERSION=0.0.0-local
ARG BUILD_TIMESTAMP=unknown

# OCI labels — GHCR uses 'org.opencontainers.image.source' to auto-link the package to the repo.
LABEL org.opencontainers.image.source="https://github.com/mghabin/entra-auth-patterns-dotnet" \
      org.opencontainers.image.description="Entra Auth Patterns sample — service container" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.revision="${BUILD_GIT_SHA}" \
      org.opencontainers.image.version="${BUILD_VERSION}" \
      org.opencontainers.image.created="${BUILD_TIMESTAMP}"

WORKDIR /app
COPY --from=publish /app .

# ASP.NET Core defaults to port 8080 in container images since .NET 8.
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

# Publish stage renames ${PROJECT}.* → app.*, so the entrypoint is the same for every service.
# Chiseled's appuser (uid 11654) is already the default USER.
ENTRYPOINT ["dotnet", "app.dll"]
