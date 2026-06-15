# Reset Demo Store Data and Reseed
# This script deletes the demo-store tenant data so it will be reseeded on API startup

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "RetailSuite - Reset Demo Store" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "⚠️  WARNING: This will delete all demo-store data" -ForegroundColor Yellow
Write-Host "    - All products, variants, orders, inventory" -ForegroundColor Yellow
Write-Host "    - All will be recreated on API startup" -ForegroundColor Yellow
Write-Host ""

$confirm = Read-Host "Are you sure? Type 'yes' to continue"

if ($confirm -eq "yes") {
    Write-Host ""
    Write-Host "🗑️  Deleting demo-store tenant..." -ForegroundColor Red

    # Read SQL Server connection string from appsettings
    $projectPath = "RetailSuite.Api"
    $appsettingsPath = Join-Path $projectPath "appsettings.json"

    if (Test-Path $appsettingsPath) {
        Write-Host "Found appsettings.json" -ForegroundColor Green

        # Extract connection string (basic parsing)
        $json = Get-Content $appsettingsPath | ConvertFrom-Json
        $connectionString = $json.ConnectionStrings.Default

        if ($connectionString) {
            Write-Host "Using connection: $($connectionString.Substring(0, 50))..." -ForegroundColor Cyan

            # Execute SQL script
            $sqlScript = Get-Content "delete-demo-store.sql" -Raw

            # Try to run the SQL script
            try {
                # Method 1: Try using sqlcmd
                $sqlScript | sqlcmd -S "(localdb)\MSSQLLocalDB" -U sa
                Write-Host ""
                Write-Host "✅ Demo store deleted successfully" -ForegroundColor Green
                Write-Host ""
                Write-Host "📖 Next steps:" -ForegroundColor Cyan
                Write-Host "  1. Start the API:" -ForegroundColor Cyan
                Write-Host "     dotnet run --project RetailSuite.Api/RetailSuite.Api.csproj" -ForegroundColor Yellow
                Write-Host ""
                Write-Host "  2. Watch for seeding output in console" -ForegroundColor Cyan
                Write-Host ""
                Write-Host "  3. Login to StoreAdmin at https://localhost:7096/" -ForegroundColor Cyan
                Write-Host "     Email: admin@demo-store.com" -ForegroundColor Yellow
                Write-Host "     Password: Demo@12345" -ForegroundColor Yellow
                Write-Host ""
                Write-Host "  4. Navigate to Point of Sale" -ForegroundColor Cyan
                Write-Host "     Products should now show with stock quantities!" -ForegroundColor Green
            }
            catch {
                Write-Host ""
                Write-Host "⚠️  Could not execute SQL via sqlcmd" -ForegroundColor Yellow
                Write-Host "    Manual alternative:" -ForegroundColor Yellow
                Write-Host ""
                Write-Host "    1. Open SQL Server Management Studio" -ForegroundColor Yellow
                Write-Host "    2. Run: delete-demo-store.sql" -ForegroundColor Yellow
                Write-Host "    3. Then restart the API" -ForegroundColor Yellow
            }
        }
        else {
            Write-Host "⚠️  Could not find connection string" -ForegroundColor Yellow
            Write-Host "    Please manually run: delete-demo-store.sql" -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "⚠️  Could not find appsettings.json" -ForegroundColor Yellow
        Write-Host "    Please manually run: delete-demo-store.sql" -ForegroundColor Yellow
    }
}
else {
    Write-Host ""
    Write-Host "❌ Cancelled" -ForegroundColor Red
    Write-Host ""
}
