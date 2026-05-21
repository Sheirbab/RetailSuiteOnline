# syntax=docker/dockerfile:1.6
# -----------------------------------------------------------------------------
# Multi-stage Dockerfile for RetailSuite.Api.
# Build stage uses the SDK image (~700 MB); runtime uses the slim aspnet image
# (~210 MB). Total final image is ~250 MB including the published binaries.
#
# Build locally:
#   docker build -t retailsuite-api:local .
# Run locally (with appsettings.Development.json + a reachable SQL Server):
#   docker run --rm -p 8080:8080 \
#     -e ConnectionStrings__Default="Server=host.docker.internal;Database=RetailSuiteDb;User Id=sa;Password=...;TrustServerCertificate=True" \
#     retailsuite-api:local
# -----------------------------------------------------------------------------

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files first for better layer caching on dependency restore.
COPY RetailSuite.Shared/RetailSuite.Shared.csproj           RetailSuite.Shared/
COPY RetailSuite.Infrastructure/RetailSuite.Infrastructure.csproj RetailSuite.Infrastructure/
COPY RetailSuite.Api/RetailSuite.Api.csproj                  RetailSuite.Api/
RUN dotnet restore RetailSuite.Api/RetailSuite.Api.csproj

# Now copy the rest of the source and publish.
COPY RetailSuite.Shared/         RetailSuite.Shared/
COPY RetailSuite.Infrastructure/ RetailSuite.Infrastructure/
COPY RetailSuite.Api/            RetailSuite.Api/
RUN dotnet publish RetailSuite.Api/RetailSuite.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# -----------------------------------------------------------------------------
# Runtime
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# SkiaSharp on Linux needs libfontconfig1 (used by BarcodeService).
RUN apt-get update \
 && apt-get install -y --no-install-recommends libfontconfig1 \
 && rm -rf /var/lib/apt/lists/*

# Run as non-root user — required by most cloud platforms (AKS, App Service Linux, ECS).
RUN useradd -m -u 1001 retail
USER retail

COPY --from=build --chown=retail /app/publish .

# wwwroot/uploads is where ProductImage files land. Persist via a volume in production.
RUN mkdir -p /app/wwwroot/uploads

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "RetailSuite.Api.dll"]
