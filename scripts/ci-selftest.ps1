<#
.SYNOPSIS
  Autotest Desktop sans interaction (CI et poste de développeur) sur une solution déjà compilée en Release.
.DESCRIPTION
  1. crée une base jetable dans un dossier temporaire (données de démonstration, e-mails simulés, rappels coupés) ;
  2. démarre l'API sur http://127.0.0.1:<Port> et attend /api/health ;
  3. lance Pressing.Desktop.exe --selftest (avec un %TEMP% privé : le journal pressing-selftest.log n'est jamais
     mélangé à celui d'un autre autotest), attend la fin (délai maximal) et affiche le journal ;
  4. arrête l'API qu'il a lui-même lancée (par PID, y compris le processus enfant de « dotnet run »)
     et supprime le dossier temporaire ;
  5. renvoie 0 seulement si le journal contient « SELFTEST OK », sinon 1.
  N'agit jamais sur une autre API (celle du port 5180, par exemple) : si le port choisi répond déjà, le script s'arrête.
.PARAMETER Port
  Port de l'API jetable (défaut 5290).
.PARAMETER DelaiSecondes
  Délai maximal de l'autotest (défaut 300 s).
.PARAMETER AttenteSecondes
  Attente maximale de la fermeture d'un autre Pressing.Desktop déjà ouvert avant de lancer l'autotest (défaut 600 s).
.EXAMPLE
  dotnet build PressingApp.sln -c Release
  .\scripts\ci-selftest.ps1 -Port 5290
#>
param(
    [int]$Port = 5290,
    [int]$DelaiSecondes = 300,
    [int]$AttenteSecondes = 600
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$url = "http://127.0.0.1:$Port"
$exe = Get-ChildItem -Path (Join-Path $root 'src\Pressing.Desktop\bin\Release') -Filter 'Pressing.Desktop.exe' -Recurse -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $exe) {
    Write-Host "Pressing.Desktop.exe introuvable : compilez d'abord la solution (dotnet build PressingApp.sln -c Release)." -ForegroundColor Red
    exit 1
}

function Test-Sante([string]$adresse) {
    try {
        Invoke-RestMethod "$adresse/api/health" -TimeoutSec 2 | Out-Null
        return $true
    } catch { return $false }
}

if (Test-Sante $url) {
    Write-Host "Une API répond déjà sur $url : choisissez un autre port avec -Port." -ForegroundColor Red
    exit 1
}

$dossier = Join-Path ([System.IO.Path]::GetTempPath()) ("pressing-ci-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $dossier | Out-Null
$journalApi = Join-Path $dossier 'api.log'
$journalApiErr = Join-Path $dossier 'api.err.log'
# L'autotest écrit « pressing-selftest.log » dans %TEMP% : on lui donne un TEMP privé (dans le dossier jetable) pour
# qu'un autre autotest lancé en parallèle sur la machine ne mélange pas son journal avec le nôtre.
$tempAutotest = Join-Path $dossier 'temp'
New-Item -ItemType Directory -Force $tempAutotest | Out-Null
$journalTest = Join-Path $tempAutotest 'pressing-selftest.log'

# Variables d'environnement propres à ce test (restaurées dans le bloc finally). Elles sont posées AVANT le
# lancement : un fichier .env du dépôt ne remplace jamais une variable déjà définie.
$variables = [ordered]@{
    'PRESSING_DB_PATH'         = (Join-Path $dossier 'pressing-ci.db')
    'PRESSING_BACKUP_PATH'     = (Join-Path $dossier 'Backups')
    'PRESSING_JWT_KEY'         = 'ci-selftest-jwt-key-0123456789-abcdefghijklmnopqrstuvwxyz'
    'PRESSING_SEED_DEMO'       = 'true'
    'PRESSING_ADMIN_USERNAME'  = 'admin'
    'PRESSING_ADMIN_PASSWORD'  = 'Admin@2026!'
    'EMAIL_PROVIDER'           = 'simulated'
    'EMAIL_BACKGROUND_ENABLED' = 'false'
    'REMINDERS_ENABLED'        = 'false'
    'BACKUP_SCHEDULE_ENABLED'  = 'false'
    'WHATSAPP_PROVIDER'        = 'simulated'
    'PRESSING_PUBLIC_API_URL'  = $url
    'PRESSING_DPI_UNAWARE'     = '1'
}
$anciennes = @{}
foreach ($nom in $variables.Keys) {
    $anciennes[$nom] = [Environment]::GetEnvironmentVariable($nom)
    [Environment]::SetEnvironmentVariable($nom, $variables[$nom])
}

$api = $null
$code = 1
try {
    Write-Host "==> Démarrage de l'API jetable sur $url" -ForegroundColor Cyan
    $api = Start-Process dotnet -ArgumentList @('run', '--no-build', '-c', 'Release', '--project', 'src/Pressing.Api', '--urls', $url) `
        -WorkingDirectory $root -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $journalApi -RedirectStandardError $journalApiErr
    Write-Host "    PID $($api.Id)"

    $pret = $false
    for ($i = 0; $i -lt 120; $i++) {
        if ($api.HasExited) { break }
        if (Test-Sante $url) { $pret = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $pret) {
        Write-Host "L'API n'a pas démarré. Journaux :" -ForegroundColor Red
        if (Test-Path $journalApi) { Get-Content $journalApi -Tail 40 }
        if (Test-Path $journalApiErr) { Get-Content $journalApiErr -Tail 40 }
        throw "API indisponible sur $url."
    }
    Write-Host "    API prête." -ForegroundColor Green

    Write-Host "==> Autotest Desktop (délai maximal : $DelaiSecondes s)" -ForegroundColor Cyan
    # Un seul Pressing.Desktop à la fois : deux fenêtres en parallèle (autre autotest, session ouverte) faussent
    # le test. On attend qu'il n'y en ait plus ; sur un runner de CI propre, il n'y en a jamais.
    $attente = 0
    while (@(Get-Process -Name 'Pressing.Desktop' -ErrorAction SilentlyContinue).Count -gt 0) {
        if ($attente -ge $AttenteSecondes) {
            throw "Un autre Pressing.Desktop est ouvert depuis plus de $AttenteSecondes s : fermez-le puis relancez."
        }
        if ($attente -eq 0) { Write-Host "Un autre Pressing.Desktop est en cours : attente de sa fermeture..." -ForegroundColor Yellow }
        Start-Sleep -Seconds 5
        $attente += 5
    }
    $ancienTemp = $env:TEMP; $ancienTmp = $env:TMP
    # Deux essais au plus, et seulement si l'application s'est arrêtée AVANT d'écrire son journal (plantage au
    # démarrage de WinForms observé de façon sporadique). Un échec réel du scénario n'est jamais rejoué.
    for ($essai = 1; $essai -le 2; $essai++) {
        $env:TEMP = $tempAutotest; $env:TMP = $tempAutotest
        try {
            $desktop = Start-Process $exe.FullName -ArgumentList '--selftest' -PassThru
        } finally {
            $env:TEMP = $ancienTemp; $env:TMP = $ancienTmp
        }
        if (-not $desktop.WaitForExit($DelaiSecondes * 1000)) {
            Write-Host "Délai dépassé : arrêt de l'autotest (PID $($desktop.Id))." -ForegroundColor Red
            & taskkill.exe /PID $desktop.Id /T /F | Out-Null
        }
        if (Test-Path $journalTest) { break }
        Write-Host "Aucun journal produit (essai $essai, code de sortie $($desktop.ExitCode))." -ForegroundColor Yellow
    }

    Write-Host "==> Journal $journalTest" -ForegroundColor Cyan
    if (Test-Path $journalTest) {
        $contenu = Get-Content $journalTest -Encoding UTF8
        $contenu | ForEach-Object { Write-Host $_ }
        # Succès = code de sortie 0 de l'application ET « SELFTEST OK » dans le journal (sans « ÉCHEC »).
        if ($desktop.ExitCode -eq 0 -and ($contenu -match 'SELFTEST OK') -and -not ($contenu -match 'SELFTEST ÉCHEC')) { $code = 0 }
        else { Write-Host "Code de sortie du Desktop : $($desktop.ExitCode)" -ForegroundColor Red }
    } else {
        Write-Host "Aucun journal produit." -ForegroundColor Red
    }
} catch {
    Write-Host "Erreur : $($_.Exception.Message)" -ForegroundColor Red
    $code = 1
} finally {
    if ($api -and -not $api.HasExited) {
        Write-Host "==> Arrêt de l'API (PID $($api.Id) et ses processus enfants)" -ForegroundColor Cyan
        & taskkill.exe /PID $api.Id /T /F | Out-Null
    }
    foreach ($nom in $variables.Keys) {
        [Environment]::SetEnvironmentVariable($nom, $anciennes[$nom])
    }
    Start-Sleep -Seconds 1
    # Journaux conservés dans artifacts/selftest (ignoré par Git) : la CI les publie en cas d'échec.
    try {
        $sortie = Join-Path $root 'artifacts\selftest'
        New-Item -ItemType Directory -Force $sortie | Out-Null
        if (Test-Path $journalTest) { Copy-Item $journalTest (Join-Path $sortie 'pressing-selftest.log') -Force }
        if (Test-Path $journalApi) { Copy-Item $journalApi (Join-Path $sortie 'api.log') -Force }
        if (Test-Path $journalApiErr) { Copy-Item $journalApiErr (Join-Path $sortie 'api.err.log') -Force }
    } catch { }
    Remove-Item -Recurse -Force $dossier -ErrorAction SilentlyContinue
}

if ($code -eq 0) {
    Write-Host "Autotest Desktop : SELFTEST OK" -ForegroundColor Green
} else {
    Write-Host "Autotest Desktop : ÉCHEC" -ForegroundColor Red
}
exit $code
