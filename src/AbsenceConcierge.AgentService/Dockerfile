# syntax=docker/dockerfile:1
#
# Absence Concierge — agent service.
# Build context is the repository root: docker build -f src/AbsenceConcierge.AgentService/Dockerfile .
#
# P6: multi-stage; project files restored before the source so a code change does not
# invalidate the restore layer; runtime image major version equals the TFM major
# version (10 / net10.0), because default roll-forward does not cross a major and the
# mismatch is a startup failure rather than a warning.

# ─── build ───────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Central package management and the SDK pin apply to every project below.
COPY Directory.Build.props Directory.Packages.props global.json ./

# Project files first. This layer is what makes an ordinary source edit cheap.
COPY src/AbsenceConcierge.ServiceDefaults/AbsenceConcierge.ServiceDefaults.csproj src/AbsenceConcierge.ServiceDefaults/
COPY src/AbsenceConcierge.AgentService/AbsenceConcierge.AgentService.csproj src/AbsenceConcierge.AgentService/
RUN dotnet restore src/AbsenceConcierge.AgentService/AbsenceConcierge.AgentService.csproj

COPY src/AbsenceConcierge.ServiceDefaults/ src/AbsenceConcierge.ServiceDefaults/
COPY src/AbsenceConcierge.AgentService/ src/AbsenceConcierge.AgentService/

# The world and the agent definition are linked into the build from outside the
# project directory, so they must be present in the context. .dockerignore
# re-includes exactly these two paths after excluding everything around them.
COPY evals/fixtures/ evals/fixtures/
COPY agents/ agents/

RUN dotnet publish src/AbsenceConcierge.AgentService/AbsenceConcierge.AgentService.csproj \
      --configuration Release \
      --no-restore \
      --output /app

# ─── runtime ─────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# Native dependency, declared rather than assumed. Globalization is deliberately ON
# (Directory.Build.props): this agent resolves dates in the caller's timezone and
# culture, and the invariant mode would return wrong answers silently rather than
# failing. The non-alpine aspnet image carries ICU and tzdata already — this ENV
# states the requirement so a later switch to a slim base cannot quietly break it.
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Non-root. The base image defines APP_UID for exactly this.
USER $APP_UID

LABEL org.opencontainers.image.source=https://github.com/konradcinkusz/agent-eval-bench
LABEL org.opencontainers.image.description="Absence Concierge agent service — mock workforce tools by default, zero credentials required."
LABEL org.opencontainers.image.licenses=MIT

ENTRYPOINT ["dotnet", "AbsenceConcierge.AgentService.dll"]
