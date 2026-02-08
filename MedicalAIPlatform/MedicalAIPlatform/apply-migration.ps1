# Script to apply Patient Models migration
# This script will attempt to apply the migration using Entity Framework

Write-Host "Applying Patient Models Migration..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "MedicalAIPlatform.csproj"
$migrationPath = Join-Path $PSScriptRoot "Migrations\ApplyPatientModelsMigration.sql"

# Check if dotnet-ef is installed
Write-Host "Checking for dotnet-ef tool..." -ForegroundColor Yellow
$efInstalled = dotnet tool list -g | Select-String "dotnet-ef"

if (-not $efInstalled) {
    Write-Host "dotnet-ef tool not found. Installing..." -ForegroundColor Yellow
    dotnet tool install --global dotnet-ef
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to install dotnet-ef tool." -ForegroundColor Red
        Write-Host "Please install it manually: dotnet tool install --global dotnet-ef" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Alternatively, you can apply the migration using SQL:" -ForegroundColor Yellow
        Write-Host "1. Open SQL Server Management Studio" -ForegroundColor Yellow
        Write-Host "2. Connect to your database" -ForegroundColor Yellow
        Write-Host "3. Execute: $migrationPath" -ForegroundColor Yellow
        exit 1
    }
}

Write-Host "Applying migration using Entity Framework..." -ForegroundColor Green
dotnet ef database update --project $projectPath

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Migration applied successfully!" -ForegroundColor Green
    Write-Host "You can now use the Patient Management features." -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "Migration failed. Trying alternative method..." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Please apply the migration manually using one of these methods:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Method 1: SQL Script" -ForegroundColor Cyan
    Write-Host "1. Open SQL Server Management Studio" -ForegroundColor White
    Write-Host "2. Connect to your database" -ForegroundColor White
    Write-Host "3. Execute the SQL script: $migrationPath" -ForegroundColor White
    Write-Host ""
    Write-Host "Method 2: Package Manager Console (Visual Studio)" -ForegroundColor Cyan
    Write-Host "Run: Update-Database" -ForegroundColor White
}
