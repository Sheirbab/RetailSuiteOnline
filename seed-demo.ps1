# RetailSuite Demo Data Seeding Script
# This script runs the RetailSuite API which will automatically seed demo data

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "RetailSuite Demo Data Seeding" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# Set location
$projectRoot = "D:\Shehriyar\Project\RetailSuite_Starter"
Set-Location $projectRoot

Write-Host "📦 Building solution..." -ForegroundColor Yellow
dotnet build

Write-Host ""
Write-Host "🚀 Starting API (demo data will be seeded automatically)..." -ForegroundColor Yellow
Write-Host ""
Write-Host "The API will seed demo data on startup if it doesn't already exist." -ForegroundColor Green
Write-Host "The seeded data includes:" -ForegroundColor Green
Write-Host "  • Demo Store tenant (subdomain: demo-store)" -ForegroundColor Green
Write-Host "  • Categories: Garments, Shoes" -ForegroundColor Green
Write-Host "  • 6 Products with 20 variants total" -ForegroundColor Green
Write-Host "  • Full inventory for all variants" -ForegroundColor Green
Write-Host ""

# Start the API
dotnet run --project RetailSuite.Api/RetailSuite.Api.csproj
