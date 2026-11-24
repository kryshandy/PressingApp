<#
.SYNOPSIS
  Arrête l'API, le site client et l'application Desktop lancés en local.
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
if ($stopped -eq 0) { Write-Host "Aucune application Pressing en cours d'exécution." }
