# Deployment Guide — Aspire + Azure

<div dir="rtl">

## دليل النشر — Aspire + Azure

### المتطلبات المسبقة

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Azure CLI (`az`) — [تثبيت Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (لبناء الصور محلياً)

</div>

---

## 1. تثبيت Aspire Workload

```bash
dotnet workload install aspire
```

To verify installation:

```bash
dotnet workload list
```

You should see `aspire` in the workload list.

---

## 2. تشغيل المشروع محلياً

```bash
dotnet run --project aspire/Vix.AppHost
```

This will:

- Start a local SQL Server container
- Run the web application
- Open the Aspire dashboard in your browser

The Aspire dashboard provides real-time telemetry, logs, and resource status.

---

## 3. بناء صورة Docker

```bash
docker build -f src/NewVixSmart.Web/Dockerfile -t new-vix-smart-web .
```

To run with docker-compose (includes SQL Server):

```bash
docker-compose up --build
```

The app will be available at `http://localhost:8080`.

---

## 4. نشر إلى Azure Container Apps

### Step A: Create Azure Resources

```bash
# Login to Azure
az login

# Create a resource group
az group create --name vix-trading-rg --location eastus

# Create an Azure Container Registry (ACR)
az acr create --resource-group vix-trading-rg \
    --name newvixsmartacr \
    --sku Basic

# Enable admin user on ACR (for quick testing; use managed identity for production)
az acr update -n newvixsmartacr --admin-enabled true
```

### Step B: Push the Image to ACR

```bash
# Build and tag
az acr build --registry newvixsmartacr --image new-vix-smart-web:v1 .

# OR if using local Docker:
docker tag new-vix-smart-web newvixsmartacr.azurecr.io/new-vix-smart-web:v1
docker push newvixsmartacr.azurecr.io/new-vix-smart-web:v1
```

### Step C: Create Azure SQL Database

```bash
# Create SQL Server
az sql server create --name vix-trading-sql \
    --resource-group vix-trading-rg \
    --location eastus \
    --admin-user sqladmin \
    --admin-password "REPLACE_WITH_StrongPassword_123!"

# Create the database
az sql db create --name NewVixSmartDb \
    --server vix-trading-sql \
    --resource-group vix-trading-rg \
    --service-tier Basic
```

### Step D: Deploy to Azure Container Apps

```bash
# Create Container Apps environment
az containerapp env create \
    --name vix-trading-env \
    --resource-group vix-trading-rg \
    --location eastus

# Get ACR credentials
ACR_PASSWORD=$(az acr credential show -n newvixsmartacr --query passwords[0].value -o tsv)

# Deploy the container app
az containerapp create \
    --name new-vix-smart-web \
    --resource-group vix-trading-rg \
    --environment vix-trading-env \
    --image newvixsmartacr.azurecr.io/new-vix-smart-web:v1 \
    --registry-server newvixsmartacr.azurecr.io \
    --registry-username newvixsmartacr \
    --registry-password "$ACR_PASSWORD" \
    --target-port 80 \
    --ingress external \
    --min-replicas 1 \
    --max-replicas 3 \
    --env-vars \
        ASPNETCORE_ENVIRONMENT="Production" \
        ASPNETCORE_URLS="http://+:80" \
        ConnectionStrings__DefaultConnection="Server=vix-trading-sql.database.windows.net,1433;Database=NewVixSmartDb;User Id=sqladmin;Password=REPLACE_WITH_StrongPassword_123!;TrustServerCertificate=True"
```

### Step E: Apply EF Core Migrations on Azure SQL

The app runs `db.Database.Migrate()` on startup, so migrations are applied automatically.
Ensure the web app's service identity has `db_owner` permissions on the Azure SQL database.

### Step F: Host header allow-list

The base `appsettings.json` restricts `AllowedHosts` to `localhost`. On Azure App Service / Container Apps the host header is your generated host name, so override it with `appsettings.Production.json` (already committed: `Hosting:AllowedHosts = "*"`), or set the environment variable `Hosting__AllowedHosts=*` in the App Service configuration. `Program.cs` applies `Hosting:AllowedHosts` over the host-level value automatically.

---

## 5. إعدادات الاتصال بقاعدة البيانات Azure SQL

Replace the `ConnectionStrings__DefaultConnection` environment variable with your Azure SQL connection string:

```
Server=<your-server>.database.windows.net,1433;Database=NewVixSmartDb;User Id=<admin-user>;Password=<admin-password>;TrustServerCertificate=True
```

For production, use **Managed Identity** instead of SQL authentication:

```bash
az sql server auth-policy update \
    --name vix-trading-sql \
    --resource-group vix-trading-rg \
    --enable-admin-only-login false
```

Then assign the Container App's managed identity as a SQL admin.

---

## 6. Docker Compose Reference

The `docker-compose.yml` at the repository root provides a local development environment:

| Service | Image | Port |
|---------|-------|------|
| `db` | `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04` | 1433 |
| `web` | Built from `src/NewVixSmart.Web/Dockerfile` | 8080 |

**Important:** Change the `SA_PASSWORD` value in `docker-compose.yml` before any non-local deployment.

---

## 7. ملاحظات أمان / Security Notes

- **لا** تستخدم `SA_PASSWORD` في بيئة الإنتاج — استخدم Azure Key Vault أو Managed Identity.
- تأكد من تغيير كلمة المرور الافتراضية قبل النشر.
- احفظ متغيرات الاتصال في Azure Key Vault واربطها بالـ Container App:

```bash
az keyvault secret set --vault-name vix-trading-kv \
    --name ConnectionStrings__DefaultConnection \
    --value "Server=..."
```
