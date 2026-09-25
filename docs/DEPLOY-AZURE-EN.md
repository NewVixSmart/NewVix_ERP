# Deployment Guide — Aspire + Azure (English)

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Azure CLI (`az`) — [Install Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for local image builds)

---

## 1. Install the Aspire Workload

```bash
dotnet workload install aspire
```

Verify:

```bash
dotnet workload list
```

---

## 2. Run Locally

```bash
dotnet run --project aspire/Vix.AppHost
```

This starts a local SQL Server container and the web app. The Aspire dashboard opens automatically.

---

## 3. Build the Docker Image

```bash
docker build -f src/NewVixSmart.Web/Dockerfile -t new-vix-smart-web .
```

Run with docker-compose:

```bash
docker-compose up --build
```

App: `http://localhost:8080`

---

## 4. Deploy to Azure Container Apps

### A. Create Resources

```bash
az login
az group create --name vix-trading-rg --location eastus
az acr create --resource-group vix-trading-rg --name newvixsmartacr --sku Basic
az acr update -n newvixsmartacr --admin-enabled true
```

### B. Push Image to ACR

```bash
az acr build --registry newvixsmartacr --image new-vix-smart-web:v1 .
```

### C. Create Azure SQL

```bash
az sql server create --name vix-trading-sql \
    --resource-group vix-trading-rg --location eastus \
    --admin-user sqladmin --admin-password "REPLACE_WITH_StrongPassword_123!"
az sql db create --name NewVixSmartDb --server vix-trading-sql \
    --resource-group vix-trading-rg --service-tier Basic
```

### D. Deploy Container App

```bash
az containerapp env create --name vix-trading-env \
    --resource-group vix-trading-rg --location eastus
ACR_PASSWORD=$(az acr credential show -n newvixsmartacr --query passwords[0].value -o tsv)
az containerapp create --name new-vix-smart-web \
    --resource-group vix-trading-rg --environment vix-trading-env \
    --image newvixsmartacr.azurecr.io/new-vix-smart-web:v1 \
    --registry-server newvixsmartacr.azurecr.io \
    --registry-username newvixsmartacr --registry-password "$ACR_PASSWORD" \
    --target-port 80 --ingress external \
    --min-replicas 1 --max-replicas 3 \
    --env-vars \
        ASPNETCORE_ENVIRONMENT="Production" \
        ASPNETCORE_URLS="http://+:80" \
        ConnectionStrings__DefaultConnection="Server=vix-trading-sql.database.windows.net,1433;Database=NewVixSmartDb;User Id=sqladmin;Password=REPLACE_WITH_StrongPassword_123!;TrustServerCertificate=True"
```

### E. Migrations

EF Core migrations run automatically on startup via `db.Database.Migrate()`.

### F. Host header allow-list

The base `appsettings.json` restricts `AllowedHosts` to `localhost`. Azure host headers use your generated host name, so the committed `appsettings.Production.json` sets `Hosting:AllowedHosts = "*"` (or set `Hosting__AllowedHosts=*` as an env var). `Program.cs` applies `Hosting:AllowedHosts` over the host-level value automatically.

---

## 5. Azure SQL Connection String

```
Server=<server>.database.windows.net,1433;Database=NewVixSmartDb;User Id=<user>;Password=<password>;TrustServerCertificate=True
```

---

## 6. Security

- Never use default passwords in production.
- Use Azure Key Vault for secrets.
- Use Managed Identity instead of SQL authentication where possible.
