# ============================================
# Stage 1: Build
# ============================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore as distinct layers
COPY src/Silk.Trading.Web/Silk.Trading.Web.csproj src/Silk.Trading.Web/
COPY aspire/Silk.ServiceDefaults/Silk.ServiceDefaults.csproj aspire/Silk.ServiceDefaults/
RUN dotnet restore src/Silk.Trading.Web/Silk.Trading.Web.csproj

# Copy everything else and publish
COPY src/Silk.Trading.Web/ src/Silk.Trading.Web/
COPY aspire/Silk.ServiceDefaults/ aspire/Silk.ServiceDefaults/
RUN dotnet publish src/Silk.Trading.Web/Silk.Trading.Web.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ============================================
# Stage 2: Runtime
# ============================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for healthcheck (optional, small image impact)
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 80

COPY --from=build /app/publish .

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:80/healthz || exit 1

ENTRYPOINT ["dotnet", "Silk.Trading.Web.dll"]
