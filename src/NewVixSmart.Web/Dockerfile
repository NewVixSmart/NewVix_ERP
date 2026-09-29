# ============================================
# Stage 1: Build
# ============================================
# Pinned to a concrete patch on purpose. "10.0" is a moving tag: the same commit
# can restore against a different runtime and a different transitive dependency
# graph every time the tag is re-pushed, which makes a failing build impossible
# to reproduce.
#   sdk:10.0.400   -> exactly the SDK pinned in global.json (rollForward latestFeature)
#                     and bundles runtime 10.0.11
#   aspnet:10.0.11 -> the runtime band every PackageReference in this repo targets
FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
WORKDIR /src

# global.json must be in the build context before restore, otherwise the SDK
# resolves to whatever default is baked into the image and the pin is a no-op.
# It only changes when the SDK pin changes, so it stays in its own cache layer.
COPY global.json ./

# Copy csproj and restore as distinct layers
COPY src/NewVixSmart.Web/NewVixSmart.Web.csproj src/NewVixSmart.Web/
COPY aspire/Vix.ServiceDefaults/Vix.ServiceDefaults.csproj aspire/Vix.ServiceDefaults/
RUN dotnet restore src/NewVixSmart.Web/NewVixSmart.Web.csproj

# Copy everything else and publish
COPY src/NewVixSmart.Web/ src/NewVixSmart.Web/
COPY aspire/Vix.ServiceDefaults/ aspire/Vix.ServiceDefaults/
RUN dotnet publish src/NewVixSmart.Web/NewVixSmart.Web.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ============================================
# Stage 2: Runtime
# ============================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS runtime
WORKDIR /app

# Install curl for healthcheck (optional, small image impact)
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 80

COPY --from=build /app/publish .

RUN chown -R 1654:1654 /app

USER 1654

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:80/healthz || exit 1

ENTRYPOINT ["dotnet", "NewVixSmart.Web.dll"]
