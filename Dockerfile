# ── Stage 1: BookNotifier CLI (scraper) — pulled from its own published image ──
FROM ghcr.io/riisdev/book-notifier:latest AS cli

# ── Stage 2: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore deps (separate layer for cache)
COPY BookNotifier-Web.csproj ./
RUN dotnet restore BookNotifier-Web.csproj

# Copy all source files
COPY . .

# Publish the web app (no --no-restore: publish's own restore pass is what generates the
# static web assets manifest correctly — a bare `dotnet restore` alone leaves framework-provided
# assets like _framework/blazor.web.js out of it, breaking Blazor's interactive circuit silently)
RUN dotnet publish BookNotifier-Web.csproj \
        -c Release \
        -o /app/publish

# ── Stage 3: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish ./

# The CLI subprocess CliRunnerService launches (`dotnet /app/cli/BookNotifier.dll`)
COPY --from=cli /app ./cli/

# curl for healthcheck; clean apt lists to keep image small
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Persistent data directory — mount a volume here
RUN mkdir -p /app/data

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

ENTRYPOINT ["dotnet", "BookNotifier-Web.dll"]
