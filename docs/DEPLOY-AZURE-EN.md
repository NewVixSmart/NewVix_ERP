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
dotnet run --project aspire/Silk.AppHost
```

This starts a local SQL Server container and the web app. The Aspire dashboard opens automatically.

---

## 3. Build the Docker Image

```bash
docker build -f src/Silk.Trading.Web/Dockerfile -t silk-trading-web .
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
az group create --name silk-trading-rg --location eastus
az acr create --resource-group silk-trading-rg --name silktradingacr --sku Basic
az acr update -n silktradingacr --admin-enabled true
```

### B. Push Image to ACR

```bash
az acr build --registry silktradingacr --image silk-trading-web:v1 .
```

### C. Create Azure SQL

```bash
az sql server create --name silk-trading-sql \
    --resource-group silk-trading-rg --location eastus \
    --admin-user sqladmin --admin-password "YourStr0ng!Password"
az sql db create --name SilkTradingDb --server silk-trading-sql \
    --resource-group silk-trading-rg --service-tier Basic
```

### D. Deploy Container App

```bash
az containerapp env create --name silk-trading-env \
    --resource-group silk-trading-rg --location eastus
ACR_PASSWORD=$(az acr credential show -n silktradingacr --query passwords[0].value -o tsv)
az containerapp create --name silk-trading-web \
    --resource-group silk-trading-rg --environment silk-trading-env \
    --image silktradingacr.azurecr.io/silk-trading-web:v1 \
    --registry-server silktradingacr.azurecr.io \
    --registry-username silktradingacr --registry-password "$ACR_PASSWORD" \
    --target-port 80 --ingress external \
    --min-replicas 1 --max-replicas 3 \
    --env-vars \
        ASPNETCORE_ENVIRONMENT="Production" \
        ASPNETCORE_URLS="http://+:80" \
        ConnectionStrings__DefaultConnection="Server=silk-trading-sql.database.windows.net,1433;Database=SilkTradingDb;User Id=sqladmin;Password=YourStr0ng!Password;TrustServerCertificate=True"
```

### E. Migrations

EF Core migrations run automatically on startup via `db.Database.Migrate()`.

---

## 5. Azure SQL Connection String

```
Server=<server>.database.windows.net,1433;Database=SilkTradingDb;User Id=<user>;Password=<password>;TrustServerCertificate=True
```

---

## 6. Security

- Never use default passwords in production.
- Use Azure Key Vault for secrets.
- Use Managed Identity instead of SQL authentication where possible.
