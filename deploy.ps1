param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "publish",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

Write-Host "Building FaceMosaicSharp..." -ForegroundColor Cyan

$projectPath = "src/FaceMosaicSharp.csproj"

Write-Host "Cleaning previous build..." -ForegroundColor Yellow
if (Test-Path $OutputDir) {
    Remove-Item -Recurse -Force $OutputDir
}

Write-Host "Publishing application..." -ForegroundColor Yellow
$sc = if ($SelfContained) { "true" } else { "false" }
dotnet publish $projectPath -c $Configuration -o $OutputDir --self-contained $sc

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build completed successfully!" -ForegroundColor Green
    Write-Host "Output: $OutputDir" -ForegroundColor Green
    Write-Host ""
    Write-Host "To create release package:" -ForegroundColor Cyan
    Write-Host "  Compress-Archive -Path $OutputDir/* -DestinationPath FaceMosaicSharp-$Configuration.zip" -ForegroundColor Gray
} else {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}