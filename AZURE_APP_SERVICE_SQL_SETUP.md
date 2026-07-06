# Azure App Service + Azure SQL setup

The API reads its database connection from `ConnectionStrings:Default`.
Keep the local `RetailSuite.Api/appsettings.json` value for development and set the Azure value in App Service configuration.

## Recommended production approach

Use the App Service managed identity with Microsoft Entra authentication.
This avoids storing a SQL username and password in source control or deployment files.

Connection string:

```text
Server=tcp:retailsuiteonline.database.windows.net,1433;Initial Catalog=RetailSuiteSuiteDB;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication="Active Directory Default";
```

## App Service configuration

In the Azure portal:

1. Open the API App Service.
2. Go to **Settings** > **Identity**.
3. Turn **System assigned** identity **On** and save.
4. Go to **Settings** > **Environment variables**.
5. Add an app setting:

```text
Name: ConnectionStrings__Default
Value: Server=tcp:retailsuiteonline.database.windows.net,1433;Initial Catalog=RetailSuiteSuiteDB;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication="Active Directory Default";
```

6. Also replace production placeholders such as:

```text
Jwt__Key
SuperAdmin__Password
Cors__AllowedOrigins__0
Verification__PublicBaseUrl
Billing__PublicBaseUrl
```

7. Restart the App Service.

You can also use the **Connection strings** section instead of app settings. If you do, use name `Default` and type `SQLAzure`; ASP.NET Core will expose it to `GetConnectionString("Default")`.

## Azure SQL permissions

The managed identity must exist as a user inside the database.

1. On the Azure SQL server, configure a Microsoft Entra admin if one is not already set.
2. Connect to `RetailSuiteSuiteDB` as that Entra admin using Azure Data Studio or SQL Server Management Studio.
3. Run the following, replacing the bracketed name with the exact App Service managed identity name:

```sql
CREATE USER [your-api-app-service-name] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [your-api-app-service-name];
ALTER ROLE db_datawriter ADD MEMBER [your-api-app-service-name];
```

For first-time schema creation, run EF Core migrations using an admin or deployment identity with DDL permissions. Do not leave the live app with broad schema permissions unless you intentionally want it to manage database migrations.

## Apply database migrations

From the repo root, run:

```powershell
dotnet ef database update --project RetailSuite.Infrastructure --startup-project RetailSuite.Api --connection "Server=tcp:retailsuiteonline.database.windows.net,1433;Initial Catalog=RetailSuiteSuiteDB;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=`"Active Directory Default`";"
```

If the command cannot authenticate from your machine, sign in with Azure tooling first:

```powershell
az login
```

## Networking checklist

Make sure Azure SQL allows the App Service to reach it:

- In Azure SQL server **Networking**, allow the App Service outbound IP addresses, or temporarily enable **Allow Azure services and resources to access this server**.
- Keep port `1433` reachable.
- After deployment, check `https://<your-api-app>.azurewebsites.net/health`.

