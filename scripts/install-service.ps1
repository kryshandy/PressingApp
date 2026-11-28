<#
.SYNOPSIS
  Installe (ou désinstalle) l'API et le site de Pressing Éclat comme services Windows. À lancer en administrateur.
.DESCRIPTION
  Depuis le dossier livré par publish.ps1 (dist\<version>\), enregistre :
    PressingEclatApi   api\Pressing.Api.exe   (écoute selon PRESSING_API_URL du .env, 5180 par défaut)
    PressingEclatWeb   web\Pressing.Web.exe   (écoute sur le port -PortWeb, 5181 par défaut)
  Les deux démarrent automatiquement avec Windows et redémarrent seuls après un arrêt anormal
  (au bout de 5 s, 5 s puis 30 s ; compteur remis à zéro après 24 h). Le site dépend de l'API.
  Un fichier .env est créé à partir de .env.example s'il n'existe pas, avec la base et les sauvegardes dans
  -DossierDonnees (C:\ProgramData\PressingEclat par défaut), car un service ne s'exécute pas sous le profil
  d'un utilisateur. Relancer le script met à jour les services existants (sans toucher à la base ni au .env).
  -Desinstaller supprime les deux services ; la base, les sauvegardes et le .env sont conservés.
.PARAMETER Dossier
  Dossier d'installation contenant api\, web\ et .env.example (défaut : le dossier parent de scripts\).
.PARAMETER DossierDonnees
  Dossier de la base et des sauvegardes (défaut : C:\ProgramData\PressingEclat).
.PARAMETER PortWeb
  Port d'écoute du site (défaut 5181).
.PARAMETER SansDemarrage
  Enregistre les services sans les démarrer.
.PARAMETER Desinstaller
  Arrête et supprime les services.
.EXAMPLE
  .\scripts\install-service.ps1
  .\scripts\install-service.ps1 -DossierDonnees D:\PressingData -PortWeb 8081
  .\scripts\install-service.ps1 -Desinstaller
  .\scripts\install-service.ps1 -WhatIf     # montre ce qui serait fait
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Dossier,
    [string]$DossierDonnees = 'C:\ProgramData\PressingEclat',
    [int]$PortWeb = 5181,
    [switch]$SansDemarrage,
    [switch]$Desinstaller
)

$ErrorActionPreference = 'Stop'
if (-not $Dossier) {
    $dossierScript = Split-Path -Parent $MyInvocation.MyCommand.Path
    $Dossier = Split-Path -Parent $dossierScript
}

$services = @(
    @{ Nom = 'PressingEclatApi'; Affichage = 'Pressing Éclat - API';  Exe = 'api\Pressing.Api.exe'; Arguments = '';
       Description = "API de Pressing Éclat : base de données, e-mails, sauvegardes automatiques."; Depend = '' },
    @{ Nom = 'PressingEclatWeb'; Affichage = 'Pressing Éclat - Site'; Exe = 'web\Pressing.Web.exe'; Arguments = "--urls http://0.0.0.0:$PortWeb";
       Description = "Site web des clients de Pressing Éclat."; Depend = 'PressingEclatApi' }
)

function Test-Administrateur {
    $identite = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal($identite)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not $WhatIfPreference -and -not (Test-Administrateur)) {
    Write-Host "Ce script doit être lancé dans un PowerShell ouvert EN ADMINISTRATEUR." -ForegroundColor Red
    exit 1
}

function Remove-ServicePressing([string]$nom) {
    if (-not (Get-Service -Name $nom -ErrorAction SilentlyContinue)) {
        Write-Host "  $nom : absent"
        return
    }
    if ($PSCmdlet.ShouldProcess($nom, "Arrêter et supprimer le service")) {
        Stop-Service -Name $nom -Force -ErrorAction SilentlyContinue
        & sc.exe delete $nom | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "sc.exe delete $nom a échoué (code $LASTEXITCODE)." }
        Write-Host "  $nom : supprimé" -ForegroundColor Green
    }
}

# --- Désinstallation -----------------------------------------------------------------------------------
if ($Desinstaller) {
    Write-Host "==> Désinstallation des services Pressing Éclat" -ForegroundColor Cyan
    foreach ($s in @($services | Sort-Object { $_.Depend } -Descending)) { Remove-ServicePressing $s.Nom }
    Write-Host "Base, sauvegardes et .env conservés (dossier de données : $DossierDonnees)." -ForegroundColor Yellow
    return
}

# --- Installation --------------------------------------------------------------------------------------
Write-Host "==> Installation des services Pressing Éclat" -ForegroundColor Cyan
Write-Host "    Dossier : $Dossier"
Write-Host "    Données : $DossierDonnees"

foreach ($s in $services) {
    $exe = Join-Path $Dossier $s.Exe
    if (-not (Test-Path $exe)) {
        throw "Introuvable : $exe. Lancez ce script depuis le dossier livré par publish.ps1 (dist\<version>\), ou passez -Dossier."
    }
}

# .env : créé une seule fois, jamais écrasé.
$env_ = Join-Path $Dossier '.env'
if (-not (Test-Path $env_)) {
    $modele = Join-Path $Dossier '.env.example'
    if (-not (Test-Path $modele)) { throw ".env.example introuvable dans $Dossier." }
    if ($PSCmdlet.ShouldProcess($env_, "Créer le .env à partir de .env.example")) {
        $lignes = [System.IO.File]::ReadAllLines($modele, [System.Text.Encoding]::UTF8)
        $remplacements = @{
            'PRESSING_DB_PATH'       = (Join-Path $DossierDonnees 'pressing.db')
            'PRESSING_BACKUP_PATH'   = (Join-Path $DossierDonnees 'Backups')
            'ASPNETCORE_ENVIRONMENT' = 'Production'
        }
        $sortie = foreach ($ligne in $lignes) {
            $cle = ($ligne -split '=', 2)[0].Trim()
            if ($ligne -match '^[A-Z_]+=' -and $remplacements.ContainsKey($cle)) { "$cle=$($remplacements[$cle])" } else { $ligne }
        }
        [System.IO.File]::WriteAllLines($env_, $sortie, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "  .env créé : relisez-le (mot de passe admin, PRESSING_SEED_DEMO, BACKUP_COPY_PATH...)." -ForegroundColor Yellow
    }
} else {
    Write-Host "  .env existant conservé."
}

if (-not (Test-Path $DossierDonnees)) {
    if ($PSCmdlet.ShouldProcess($DossierDonnees, "Créer le dossier de données")) {
        New-Item -ItemType Directory -Force $DossierDonnees | Out-Null
    }
}

foreach ($s in $services) {
    $exe = Join-Path $Dossier $s.Exe
    $commande = if ($s.Arguments) { "`"$exe`" $($s.Arguments)" } else { "`"$exe`"" }
    $existe = [bool](Get-Service -Name $s.Nom -ErrorAction SilentlyContinue)

    if ($existe) {
        if ($PSCmdlet.ShouldProcess($s.Nom, "Mettre à jour le service existant")) {
            Stop-Service -Name $s.Nom -Force -ErrorAction SilentlyContinue
            & sc.exe config $s.Nom binPath= $commande start= auto | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "sc.exe config $($s.Nom) a échoué (code $LASTEXITCODE)." }
        }
    } elseif ($PSCmdlet.ShouldProcess($s.Nom, "Créer le service ($commande)")) {
        New-Service -Name $s.Nom -BinaryPathName $commande -DisplayName $s.Affichage `
            -Description $s.Description -StartupType Automatic | Out-Null
    }

    if ($PSCmdlet.ShouldProcess($s.Nom, "Régler le redémarrage automatique en cas d'échec")) {
        & sc.exe failure $s.Nom reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "sc.exe failure $($s.Nom) a échoué (code $LASTEXITCODE)." }
        & sc.exe failureflag $s.Nom 1 | Out-Null
        if ($s.Depend) { & sc.exe config $s.Nom depend= $s.Depend | Out-Null }
        Write-Host "  $($s.Nom) : enregistré (démarrage automatique, redémarrage en cas d'échec)" -ForegroundColor Green
    }
}

if (-not $SansDemarrage) {
    foreach ($s in $services) {
        if ($PSCmdlet.ShouldProcess($s.Nom, "Démarrer le service")) {
            Start-Service -Name $s.Nom
            Write-Host "  $($s.Nom) : démarré"
        }
    }

    if ($PSCmdlet.ShouldProcess('http://localhost:5180/api/health', "Vérifier que l'API répond")) {
        $ok = $false
        for ($i = 0; $i -lt 40; $i++) {
            try {
                Invoke-RestMethod 'http://localhost:5180/api/health' -TimeoutSec 2 | Out-Null
                $ok = $true
                break
            } catch { Start-Sleep -Milliseconds 750 }
        }
        if ($ok) {
            Write-Host "API opérationnelle : http://localhost:5180/api/health  |  Site : http://localhost:$PortWeb" -ForegroundColor Green
        } else {
            Write-Warning "L'API ne répond pas encore sur le port 5180 (PRESSING_API_URL du .env ?). Consultez l'Observateur d'événements, journal Application."
        }
    }
}
