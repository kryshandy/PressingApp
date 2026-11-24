<#
.SYNOPSIS
  Démarre l'API (5180), le site client (5181) et l'application Desktop du personnel.
.EXAMPLE
  .\scripts\start.ps1              # tout démarrer
  .\scripts\start.ps1 -SansDesktop # API + site uniquement
#>
param(
    [switch]$SansDesktop,
    [switch]$SansBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path (Join-Path $root '.env')) -and (Test-Path (Join-Path $root '.env.example'))) {
    Copy-Item (Join-Path $root '.env.example') (Join-Path $root '.env')
    Write-Host "Fichier .env créé à partir de .env.example (pensez à le personnaliser)." -ForegroundColor Yellow
}

if (-not $SansBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1')
}

$logs = Join-Path $root 'logs'
New-Item -ItemType Directory -Force $logs | Out-Null

function Start-PressingProject([string]$name, [string]$project, [string]$url) {
    $out = Join-Path $logs "$name.log"
    $err = Join-Path $logs "$name.err.log"
    $proc = Start-Process dotnet -ArgumentList @('run', '--no-build', '--project', $project, '--urls', $url) `
        -WorkingDirectory $root -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $out -RedirectStandardError $err
    Write-Host ("  {0,-8} PID {1,-6} {2}" -f $name, $proc.Id, $url)
    return $proc
}

Write-Host "==> Démarrage" -ForegroundColor Cyan
$api = Start-PressingProject 'api' 'src/Pressing.Api' 'http://0.0.0.0:5180'

# Attendre que l'API réponde avant de lancer les clients
$ok = $false
for ($i = 0; $i -lt 60; $i++) {
    try {
        Invoke-RestMethod 'http://localhost:5180/api/health' -TimeoutSec 2 | Out-Null
        $ok = $true
        break
    } catch { Start-Sleep -Milliseconds 500 }
}
if (-not $ok) { Write-Warning "L'API ne répond pas encore : consultez logs\api.err.log" }

$web = Start-PressingProject 'web' 'src/Pressing.Web' 'http://0.0.0.0:5181'

if (-not $SansDesktop) {
    Start-Process dotnet -ArgumentList @('run', '--no-build', '--project', 'src/Pressing.Desktop') -WorkingDirectory $root | Out-Null
    Write-Host "  desktop  lancé"
}

Write-Host ""
Write-Host "API        : http://localhost:5180  (documentation : /openapi/v1.json)" -ForegroundColor Green
Write-Host "Site client: http://localhost:5181" -ForegroundColor Green
Write-Host "Arrêt      : .\scripts\stop.ps1"
