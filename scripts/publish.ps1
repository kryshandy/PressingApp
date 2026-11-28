<#
.SYNOPSIS
  Publie Pressing Éclat en Release dans dist\<version>\ : API, site web, Desktop, configuration et mode d'emploi.
.DESCRIPTION
  Produit, pour Windows 64 bits (win-x64) :
    dist\<version>\api\        l'API (Pressing.Api.exe)
    dist\<version>\web\         le site client (Pressing.Web.exe)
    dist\<version>\desktop\    l'application du personnel (Pressing.Desktop.exe)
    dist\<version>\scripts\    install-service.ps1 (installation des services Windows)
    dist\<version>\.env.example, README.md, LISEZMOI-INSTALLATION.txt

  Choix par défaut : publication DÉPENDANTE DU FRAMEWORK (framework-dependent). Les trois applications partagent
  les mêmes exécutables .NET installés une fois sur la machine : le dossier livré est petit (quelques dizaines
  de Mo au lieu de plusieurs centaines), et les correctifs de sécurité de .NET arrivent par Windows Update sans
  republier l'application. Contrepartie : il faut installer « ASP.NET Core Runtime 10 » (serveur) et
  « .NET Desktop Runtime 10 » (postes du comptoir) ; LISEZMOI-INSTALLATION.txt donne les commandes winget.
  Avec -AutonomeComplet, chaque application embarque son propre runtime (plus gros, aucun prérequis).
.PARAMETER Version
  Numéro de version du dossier de sortie. Défaut : <Version> de Directory.Build.props.
.PARAMETER Sortie
  Dossier racine de sortie (défaut : dist, ignoré par Git).
.PARAMETER AutonomeComplet
  Publie en self-contained (le runtime .NET est embarqué dans chaque application).
.PARAMETER SansTests
  Ne lance pas les tests avant la publication.
.EXAMPLE
  .\scripts\publish.ps1
  .\scripts\publish.ps1 -AutonomeComplet
  .\scripts\publish.ps1 -WhatIf     # affiche ce qui serait fait, sans rien publier
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Version,
    [string]$Sortie = 'dist',
    [switch]$AutonomeComplet,
    [switch]$SansTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $Version) {
    [xml]$props = Get-Content (Join-Path $root 'Directory.Build.props') -Encoding UTF8
    $Version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
}
if (-not $Version) { throw "Version introuvable : passez -Version." }

$racine = if ([System.IO.Path]::IsPathRooted($Sortie)) { $Sortie } else { Join-Path $root $Sortie }
$cible = Join-Path $racine $Version
$mode = if ($AutonomeComplet) { 'autonome (self-contained)' } else { 'dépendant du framework' }
$selfContained = if ($AutonomeComplet) { 'true' } else { 'false' }

$projets = @(
    @{ Nom = 'api';     Projet = 'src/Pressing.Api/Pressing.Api.csproj' },
    @{ Nom = 'web';     Projet = 'src/Pressing.Web/Pressing.Web.csproj' },
    @{ Nom = 'desktop'; Projet = 'src/Pressing.Desktop/Pressing.Desktop.csproj' }
)

Write-Host "==> Publication Pressing Éclat $Version ($mode, win-x64)" -ForegroundColor Cyan
Write-Host "    Dossier de sortie : $cible"

if (-not $PSCmdlet.ShouldProcess($cible, "Publier l'API, le site et le Desktop")) {
    foreach ($p in $projets) { Write-Host ("    [simulation] dotnet publish {0} -> {1}" -f $p.Projet, (Join-Path $cible $p.Nom)) }
    Write-Host "    [simulation] copie de .env.example, README.md, scripts\install-service.ps1 et LISEZMOI-INSTALLATION.txt"
    return
}

if (-not $SansTests) {
    Write-Host "==> Tests" -ForegroundColor Cyan
    dotnet test PressingApp.sln -c Release
    if ($LASTEXITCODE -ne 0) { throw "Des tests ont échoué : publication annulée (ou utilisez -SansTests)." }
}

if (Test-Path $cible) { Remove-Item -Recurse -Force $cible }
New-Item -ItemType Directory -Force $cible | Out-Null

foreach ($p in $projets) {
    $dossier = Join-Path $cible $p.Nom
    Write-Host "==> $($p.Nom)" -ForegroundColor Cyan
    dotnet publish $p.Projet -c Release -r win-x64 --self-contained $selfContained -o $dossier -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "La publication de $($p.Nom) a échoué." }
}

Write-Host "==> Fichiers d'accompagnement" -ForegroundColor Cyan
Copy-Item (Join-Path $root '.env.example') (Join-Path $cible '.env.example')
Copy-Item (Join-Path $root 'README.md') (Join-Path $cible 'README.md')
New-Item -ItemType Directory -Force (Join-Path $cible 'scripts') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'install-service.ps1') (Join-Path $cible 'scripts\install-service.ps1')

$prerequis = if ($AutonomeComplet) {
@"
Cette livraison est AUTONOME : le runtime .NET est inclus dans chaque dossier. Aucun prérequis.
"@
} else {
@"
Cette livraison est DÉPENDANTE DU FRAMEWORK : installez une fois (Invite de commandes ou PowerShell) :
  - sur le serveur (API et site)  : winget install Microsoft.DotNet.AspNetCore.10   (ASP.NET Core Runtime 10)
  - sur chaque poste du comptoir  : winget install Microsoft.DotNet.DesktopRuntime.10 (.NET Desktop Runtime 10)
Sans accès à winget : téléchargez « ASP.NET Core Runtime 10 » / « .NET Desktop Runtime 10 »
(Windows x64) sur https://dotnet.microsoft.com/download/dotnet/10.0
"@
}

$lisezMoi = @"
PRESSING ÉCLAT — INSTALLATION (version $Version)
==================================================

Contenu de ce dossier
  api\        l'API (le « serveur » : base de données, e-mails, sauvegardes)
  web\        le site web des clients
  desktop\    l'application Windows du personnel (Pressing.Desktop.exe)
  scripts\    install-service.ps1 : installe l'API et le site comme services Windows
  .env.example, README.md

PRÉREQUIS
$prerequis

INSTALLATION SUR LE SERVEUR (une seule fois)
  1. Copiez ce dossier sur le serveur, par exemple dans C:\PressingEclat\ (sans espace ni accent dans le chemin).
  2. Ouvrez PowerShell EN ADMINISTRATEUR dans ce dossier et lancez :
         Set-ExecutionPolicy -Scope Process Bypass
         .\scripts\install-service.ps1
     Le script crée le fichier .env (à partir de .env.example), les dossiers de données dans C:\ProgramData\PressingEclat,
     enregistre les services « PressingEclatApi » et « PressingEclatWeb » (démarrage automatique, redémarrage en
     cas d'échec) et les démarre.
  3. Ouvrez le fichier .env et vérifiez au minimum (voir README.md, section 6) :
       PRESSING_ADMIN_PASSWORD   un mot de passe long et unique (lu une seule fois, à la création de la base)
       PRESSING_SEED_DEMO=false  pas de données de démonstration en production
       PRESSING_WEB_URL          l'adresse publique du site
       BACKUP_COPY_PATH          un second disque ou une clé USB pour la copie des sauvegardes
     Puis redémarrez les services :  Restart-Service PressingEclatApi, PressingEclatWeb
  4. Vérifiez : http://localhost:5180/api/health (API) et http://localhost:5181 (site).
  5. Pare-feu : ouvrez le port 5180 (TCP) si des postes du comptoir sont sur d'autres machines.

INSTALLATION D'UN POSTE DU COMPTOIR
  1. Copiez le dossier desktop\ sur le poste (par exemple C:\PressingEclat\desktop\).
  2. Créez à côté de Pressing.Desktop.exe un fichier .env contenant l'adresse du serveur :
         PRESSING_PUBLIC_API_URL=http://NOM-DU-SERVEUR:5180
  3. Lancez Pressing.Desktop.exe et connectez-vous (compte admin créé au premier démarrage de l'API).

SAUVEGARDES ET RESTAURATION
  Une sauvegarde vérifiée est créée chaque jour (BACKUP_HOUR, 22 h par défaut) dans le dossier de sauvegarde,
  conservée 30 jours (BACKUP_RETENTION_DAYS) et copiée dans BACKUP_COPY_PATH si renseigné.
  Pour restaurer : arrêtez le service PressingEclatApi, remplacez pressing.db par la sauvegarde choisie,
  supprimez pressing.db-wal et pressing.db-shm, puis redémarrez le service (détails : README.md, section 7).

MISE À JOUR
  Sauvegardez (Desktop, Paramètres, Sauvegardes), arrêtez les services, remplacez les dossiers api\ et web\ par ceux
  de la nouvelle version (le fichier .env et la base ne changent pas), redémarrez. Les migrations de la base
  s'appliquent seules au démarrage, après une sauvegarde automatique « avant-migration ».

DÉSINSTALLATION
  PowerShell en administrateur :  .\scripts\install-service.ps1 -Desinstaller
  (les services sont supprimés ; la base et les sauvegardes dans C:\ProgramData\PressingEclat sont conservées).
"@
# Fichier texte en UTF-8 avec BOM et fins de ligne Windows : lisible tel quel dans le Bloc-notes.
$lisezMoi = ($lisezMoi -replace "`r`n", "`n") -replace "`n", "`r`n"
[System.IO.File]::WriteAllText((Join-Path $cible 'LISEZMOI-INSTALLATION.txt'), $lisezMoi, (New-Object System.Text.UTF8Encoding($true)))

$octets = (Get-ChildItem $cible -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ""
Write-Host ("Publication terminée : {0} ({1:N1} Mo, {2})" -f $cible, ($octets / 1MB), $mode) -ForegroundColor Green
foreach ($p in $projets) {
    $taille = (Get-ChildItem (Join-Path $cible $p.Nom) -Recurse -File | Measure-Object -Property Length -Sum).Sum
    Write-Host ("  {0,-8} {1,8:N1} Mo" -f $p.Nom, ($taille / 1MB))
}
