# Plan 002, Step 4 — the ToolBox's HTTP deployment shape.
# Multi-stage: the SDK image (~800MB, compilers and all) builds; only the
# published output moves into the runtime image. Nobody ships their workshop.

# ---------- build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Layer-caching choreography: csproj/props files first, then restore, THEN the
# source. Docker reuses cached layers until an input changes — dependency
# manifests change rarely, source changes constantly, so restore (the slow,
# network-bound part) replays from cache on most builds.
COPY Directory.Build.props ./
COPY src/ToolBox.Core/ToolBox.Core.csproj        src/ToolBox.Core/
COPY src/ToolBox.Host/ToolBox.Host.csproj        src/ToolBox.Host/
COPY src/ToolSets/ToolBox.Basics/ToolBox.Basics.csproj src/ToolSets/ToolBox.Basics/
RUN dotnet restore src/ToolBox.Host/ToolBox.Host.csproj

COPY src/ ./src/
RUN dotnet publish src/ToolBox.Host/ToolBox.Host.csproj -c Release -o /app --no-restore

# ---------- runtime stage ----------
# aspnet:10.0 per plan 002 Stage 2 Q3: boring, correct, ~110MB.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# The aspnet image ships neither curl nor wget (same trap LLM_Monitor hit with
# its slim Python image — its healthcheck went stdlib for this exact reason).
# We install curl for the compose healthcheck; root is required, so this runs
# before the USER directive below.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# Container identity: HTTP transport, all interfaces (the compose network needs
# to reach us; "localhost" inside a container means only the container itself).
ENV TOOLBOX_TRANSPORT=http
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

# Non-root: the .NET images define APP_UID for exactly this line.
USER $APP_UID

ENTRYPOINT ["dotnet", "ToolBox.Host.dll"]
