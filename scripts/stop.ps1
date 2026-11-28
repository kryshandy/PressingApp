<#
.SYNOPSIS
  Arrête l'API, le site client, l'application Desktop et les moteurs IA locaux.
#>
$names = @('Pressing.Api', 'Pressing.Web', 'Pressing.Desktop')
$stopped = 0
foreach ($name in $names) {
    Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
        Stop-Process -Id $_.Id -Force
        Write-Host "Arrêté : $name (PID $($_.Id))"
        $stopped++
    }
}
$aiScript = Join-Path $PSScriptRoot '..\tools\assistant\stop-ai.ps1'
if (Test-Path $aiScript) {
    & $aiScript
}
elseif ($stopped -eq 0) {
    Write-Host "Aucune application Pressing en cours d'exécution."
}
