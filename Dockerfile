# Builds the single artifact described in the README: the Vue SPA compiled into the API's
# wwwroot, served by the same .NET process on one port.

# ---- Frontend -------------------------------------------------------------------------
# Debian rather than Alpine on purpose: pnpm-lock.yaml is resolved against glibc, and the
# musl builds of Rollup's native binaries are a different set of optional dependencies.
# This stage is thrown away, so its size doesn't reach the final image.
FROM node:22-bookworm-slim AS web

# Corepack installs the exact pnpm version pinned in package.json's "packageManager".
ENV COREPACK_ENABLE_DOWNLOAD_PROMPT=0
RUN corepack enable

WORKDIR /src/Web

# Dependencies first, so editing application code doesn't re-run the install.
COPY src/Web/package.json src/Web/pnpm-lock.yaml src/Web/pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile

# vite.config.ts's copy-license plugin reads this two directories up from itself
# (/src/Web/vite.config.ts -> /LICENSE.txt), the same relative position it has in the
# source tree (src/Web/vite.config.ts -> LICENSE.txt at the repo root).
COPY LICENSE.txt /LICENSE.txt

COPY src/Web/ ./
# vite.config.ts sends the build to ../Api/wwwroot, i.e. /src/Api/wwwroot here.
RUN pnpm build

# ---- API ------------------------------------------------------------------------------
# Ubuntu's SDK rather than an mcr.microsoft.com/dotnet/sdk image: global.json pins the
# 10.0.1xx feature band, and Microsoft publishes SDK images for the newest band only (its
# 1xx images stopped at 10.0.103). Ubuntu ships the 1xx band with every servicing release,
# so this stays patched. Only the build runs on it; the runtime stage below is Microsoft's.
#
# Both pinned, so the same commit builds with the same compiler. To bump them:
# - the image: the digest Docker Hub serves for ubuntu:24.04, e.g. from
#   https://hub.docker.com/v2/repositories/library/ubuntu/tags/24.04 ("digest");
# - the SDK: the version Launchpad publishes for dotnet-sdk-10.0 in noble-updates
#   (https://launchpad.net/ubuntu/noble/amd64/dotnet-sdk-10.0), which must stay in the
#   10.0.1xx band global.json pins. The archive keeps only the newest version, so once
#   Ubuntu ships the next servicing release this one stops resolving and the build fails
#   until it is bumped.
FROM ubuntu:24.04@sha256:9c7f200c07e3d4a03e7249d2c16bf66a51120fb876aa2242cf31451412543289 AS api

RUN apt-get update \
    && apt-get install --yes --no-install-recommends dotnet-sdk-10.0=10.0.115-0ubuntu1~24.04.1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src

# Same idea: restore against just the project files so a code change doesn't refetch NuGet.
# global.json comes along so an SDK outside the pinned band fails the build instead of
# quietly compiling it. .editorconfig comes along because the build treats warnings as
# errors: without it the analyzers run with none of the repository's deliberate
# suppressions and fail the image build on rules the source tree has turned off.
COPY global.json Directory.Build.props Directory.Packages.props nuget.config .editorconfig ./
COPY src/Api/Api.csproj src/Api/
RUN dotnet restore src/Api/Api.csproj

COPY src/Api/ src/Api/
COPY --from=web /src/Api/wwwroot src/Api/wwwroot

RUN dotnet publish src/Api/Api.csproj --configuration Release --no-restore --output /app

# ---- Runtime --------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# The only addition to the base image, and only so HEALTHCHECK below has a client to call
# with. Orchestrators that probe over HTTP themselves (Kubernetes httpGet, for one) don't
# need it, and can drop this layer.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=api /app ./

# SQLite writes next to its file, so the database lives on a mount rather than in the
# container's writable layer. Owned by the non-root app user up front: a named volume
# inherits the ownership of the directory it covers, which is what keeps migrations from
# failing on first start. A bind mount does not, and has to be chown'd on the host.
ENV ConnectionStrings__FooCalendarDb="Data Source=/var/lib/foo-calendar/foo-calendar.db"
RUN mkdir --parents /var/lib/foo-calendar && chown $APP_UID /var/lib/foo-calendar

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER $APP_UID

# Reports Degraded, not Unhealthy, when only the database check fails, so this treats
# anything other than a clean 200 as unhealthy.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl --fail --silent --show-error http://localhost:8080/healthchecks || exit 1

ENTRYPOINT ["dotnet", "Api.dll"]
