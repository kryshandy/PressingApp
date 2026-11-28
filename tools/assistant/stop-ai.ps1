<#
.SYNOPSIS
  Arrête les serveurs IA locaux démarrés par start-ai.ps1.
#>
$ErrorActionPreference = 'Stop'

$aiRoot = Join-Path $env:LOCALAPPDATA 'Pressing\ai'
$allowed = @('llama-server', 'whisper-server')
$stopped = 0

foreach ($key in @('llama', 'whisper')) {
    $pidFile = Join-Path $aiRoot "$key.pid"
    if (-not (Test-Path $pidFile)) { continue }

    $raw = (Get-Content $pidFile -Raw).Trim()
    $processId = 0
    if (-not [int]::TryParse($raw, [ref]$processId)) {
        Remove-Item $pidFile -Force
        continue
    }

    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if (-not $process) {
        Remove-Item $pidFile -Force
        continue
    }

    if ($allowed -notcontains $process.ProcessName) {
        Write-Warning "PID $processId n'est pas un serveur IA attendu ($($process.ProcessName)) ; il n'est pas arrêté."
        Remove-Item $pidFile -Force
        continue
    }

    try {
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(3000)) { Stop-Process -Id $processId -Force }
        Write-Host "Arrêté : $($process.ProcessName) (PID $processId)"
        $stopped++
    }
    catch {
        Write-Warning "Impossible d'arrêter le PID $processId : $($_.Exception.Message)"
    }
    finally {
        if (Test-Path $pidFile) { Remove-Item $pidFile -Force }
    }
}

if ($stopped -eq 0) { Write-Host "Aucun serveur IA local en cours d'exécution." }
