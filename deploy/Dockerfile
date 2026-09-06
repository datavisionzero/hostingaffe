# One image, carrying the API and the built SPA: the two toolchains of ADR 0002
# and ADR 0004 meet here and nowhere else. The runtime image carries neither of
# them.
#
# Build context is the repository root:
#   docker build -f deploy/Dockerfile -t hostingaffe .

# ---------------------------------------------------------------- the SPA ----
FROM node:24-alpine AS web
WORKDIR /web

COPY src/web/package.json src/web/package-lock.json ./
RUN npm ci

# The API layer is generated from the contract before the build runs (ADR
# 0005), so the document has to be in this stage — at the path `npm run
# generate` reaches for, two levels above src/web/. Copied before the sources
# because it changes less often than they do.
COPY docs/api/openapi.json /docs/api/openapi.json

COPY src/web/ ./
RUN npm run build -- --outDir dist --emptyOutDir

# ------------------------------------------------------------- the server ----
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# What this build calls itself. The release workflow passes the tag; a build
# started by hand keeps the default the props file gives, which says plainly
# that it is not a release (Directory.Build.props).
ARG VERSION=0.0.0-dev

# Which commit it was built from, appended by the SDK to the informational
# version behind a `+`. CI passes it, because this build has no repository to
# read it out of — `Dockerfile.dockerignore` excludes `.git/`. Left empty it
# stamps nothing, and `InstanceVersion` cuts the suffix off anyway: the commit
# is the build's business, the semver is what the CLI compares (ADR 0011).
ARG REVISION=

# Restore against the manifests alone, so that a change to a source file does
# not invalidate the package layer.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Hostingaffe.Domain/Hostingaffe.Domain.csproj src/Hostingaffe.Domain/
COPY src/Hostingaffe.Application/Hostingaffe.Application.csproj src/Hostingaffe.Application/
COPY src/Hostingaffe.Infrastructure/Hostingaffe.Infrastructure.csproj src/Hostingaffe.Infrastructure/
COPY src/Hostingaffe.Api/Hostingaffe.Api.csproj src/Hostingaffe.Api/
RUN dotnet restore src/Hostingaffe.Api/Hostingaffe.Api.csproj

COPY src/Hostingaffe.Domain/ src/Hostingaffe.Domain/
COPY src/Hostingaffe.Application/ src/Hostingaffe.Application/
COPY src/Hostingaffe.Infrastructure/ src/Hostingaffe.Infrastructure/
COPY src/Hostingaffe.Api/ src/Hostingaffe.Api/
RUN dotnet publish src/Hostingaffe.Api/Hostingaffe.Api.csproj -c Release -o /app \
    -p:Version=$VERSION -p:SourceRevisionId=$REVISION

# ------------------------------------------------------------ the runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# Npgsql probes for GSSAPI when it opens a connection, whatever the connection
# string says about authentication. Without the library the probe writes two
# lines with the word `Error` straight to stderr on every start of a run that is
# going fine, and the next person reading the log during a real outage has to
# rule it out first. Three megabytes to keep the log honest; nothing here asks
# for Kerberos. Ahead of the COPY, so a source change does not re-run apk.
RUN apk add --no-cache krb5-libs

COPY --from=build /app ./
COPY --from=web /web/dist ./wwwroot

# The operational verbs the CLI deliberately does not have — the ones that
# need the connection string — run as `docker compose exec hostingaffe hostingaffe …`
# (docs/codebase.md), so the binary has to be found by name.
ENV PATH="/app:${PATH}" \
    ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

# Reachable over the network, so it does not run as root.
RUN chown -R $APP_UID:$APP_UID /app
USER $APP_UID

# Where this image came from, said by the image itself. The package page on
# GHCR carries the same link, but that is a property of the registry: an image
# somebody mirrors or copies into their own registry keeps these and loses
# that. Two of them repeat the arguments the build stage consumed, so they are
# declared again here — an ARG belongs to one stage.
#
# Last, after the COPYs and after the only RUN that costs anything, because
# `revision` changes on every commit and a label is the cheapest layer there
# is. Nothing below it has to be rebuilt when it does.
ARG VERSION=0.0.0-dev
ARG REVISION=
LABEL org.opencontainers.image.title="hostingaffe" \
      org.opencontainers.image.description="A lean, self-hosted issue tracker for software development in the agentic age." \
      org.opencontainers.image.source="https://github.com/datavisionzero/hostingaffe" \
      org.opencontainers.image.documentation="https://github.com/datavisionzero/hostingaffe/blob/main/docs/operations.md" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.vendor="datavisionzero" \
      org.opencontainers.image.version="$VERSION" \
      org.opencontainers.image.revision="$REVISION"

ENTRYPOINT ["hostingaffe"]
