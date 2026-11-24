<#
.SYNOPSIS
  Restaure les paquets, compile toute la solution et (optionnellement) lance les tests.
.EXAMPLE
  .\scripts\build.ps1
  .\scripts\build.ps1 -Tests
#>
param(
    [switch]$Tests,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "==> Restauration des paquets NuGet" -ForegroundColor Cyan
dotnet restore PressingApp.sln
if ($LASTEXITCODE -ne 0) { throw "La restauration a échoué." }

Write-Host "==> Compilation ($Configuration)" -ForegroundColor Cyan
dotnet build PressingApp.sln -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw "La compilation a échoué." }

if ($Tests) {
    Write-Host "==> Tests" -ForegroundColor Cyan
    dotnet test PressingApp.sln -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw "Des tests ont échoué." }
}

Write-Host "Compilation terminée." -ForegroundColor Green
