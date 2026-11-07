# ==============================================================================
# KEPaymentTracker Backend — Production Multi-Stage Dockerfile for Render / Cloud
# ==============================================================================

# Stage 1: Build & Restore Dependencies
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution file and project descriptors first (optimizes Docker layer caching)
COPY EnterprisePaymentSystem.slnx ./
COPY src/EnterprisePaymentSystem.Domain/*.csproj src/EnterprisePaymentSystem.Domain/
COPY src/EnterprisePaymentSystem.Application/*.csproj src/EnterprisePaymentSystem.Application/
COPY src/EnterprisePaymentSystem.Infrastructure/*.csproj src/EnterprisePaymentSystem.Infrastructure/
COPY src/EnterprisePaymentSystem.Api/*.csproj src/EnterprisePaymentSystem.Api/
COPY tests/EnterprisePaymentSystem.UnitTests/*.csproj tests/EnterprisePaymentSystem.UnitTests/

# Restore dependencies
RUN dotnet restore src/EnterprisePaymentSystem.Api/EnterprisePaymentSystem.Api.csproj

# Copy the complete source tree
COPY src/ src/
COPY tests/ tests/

# Publish optimized Release build
WORKDIR /src/src/EnterprisePaymentSystem.Api
RUN dotnet publish EnterprisePaymentSystem.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ==============================================================================
# Stage 2: Minimal Production Runtime
# ==============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Ensure non-root security container environment
USER $APP_UID

# Copy compiled artifacts from build stage
COPY --from=build /app/publish .

# Environment Configuration for Cloud SaaS (Render / AWS / Railway)
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    PORT=8080

EXPOSE 8080

# Render dynamically injects $PORT (e.g. 10000).
# The entrypoint shell wrapper binds Kestrel to 0.0.0.0 on the port supplied by Render or defaults to 8080.
ENTRYPOINT ["sh", "-c", "dotnet EnterprisePaymentSystem.Api.dll --urls http://0.0.0.0:${PORT:-8080}"]

