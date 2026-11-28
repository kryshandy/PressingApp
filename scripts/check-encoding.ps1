<#
.SYNOPSIS
  Contrôle d'encodage du dépôt (utilisé par la CI) : détecte les accents corrompus et les scripts sans BOM.
.DESCRIPTION
  1. Dans les fichiers texte suivis par Git sous src/, tests/, scripts/ et tools/, cherche les séquences de
     double encodage UTF-8 : « Ã », « Â » suivi d'un symbole latin-1 (« » ° © espace insécable...) et « â€ ».
     CONTRIBUTING.md et .github/copilot-instructions.md (qui citent ces séquences en exemple) sont exclus.
  2. Vérifie que chaque script .ps1 suivi par Git commence par un BOM UTF-8 (Windows PowerShell 5.1 lit un
     script sans BOM en ANSI et abîme les accents).
  Affiche chaque problème sous la forme fichier:ligne et renvoie le code 1 s'il y en a, sinon 0.
.EXAMPLE
  .\scripts\check-encoding.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# Les séquences sont construites à partir de codes de caractères : ce script ne contient lui-même aucune
# séquence corrompue (il ne se signalerait donc pas lui-même).
$a = [string][char]0x00C3                              # Ã
$b = [string][char]0x00C2                              # Â
$c = [string][char]0x00E2 + [char]0x20AC               # â€
$pattern = [regex]('(' + $a + '|' + $b + '[ -¿]|' + $c + ')')

$exclus = @('CONTRIBUTING.md', '.github/copilot-instructions.md')
$extensionsBinaires = @('.png', '.jpg', '.jpeg', '.gif', '.ico', '.pdf', '.dll', '.exe', '.zip', '.gguf',
    '.woff', '.woff2', '.ttf', '.otf', '.snk', '.db', '.bin', '.mp3', '.wav')

$problemes = New-Object System.Collections.Generic.List[string]

# --- 1. Séquences de double encodage -----------------------------------------------------------------
$fichiers = @(git -c core.quotepath=false ls-files -- src tests scripts tools)
if ($LASTEXITCODE -ne 0) { throw "git ls-files a échoué : le script doit être lancé dans un dépôt Git." }

$analyses = 0
foreach ($fichier in $fichiers) {
    if ([string]::IsNullOrWhiteSpace($fichier)) { continue }
    if ($exclus -contains $fichier) { continue }
    if ($extensionsBinaires -contains [System.IO.Path]::GetExtension($fichier).ToLowerInvariant()) { continue }
    $chemin = Join-Path $root $fichier
    if (-not (Test-Path -LiteralPath $chemin)) { continue }

    $octets = [System.IO.File]::ReadAllBytes($chemin)
    if ([Array]::IndexOf($octets, [byte]0) -ge 0) { continue }   # binaire

    $analyses++
    $texte = [System.Text.Encoding]::UTF8.GetString($octets)
    if (-not $pattern.IsMatch($texte)) { continue }

    $numero = 0
    foreach ($ligne in ($texte -split "`n")) {
        $numero++
        if ($pattern.IsMatch($ligne)) {
            $extrait = $ligne.Trim()
            if ($extrait.Length -gt 90) { $extrait = $extrait.Substring(0, 90) + '...' }
            $problemes.Add(("{0}:{1} : accents ré-encodés (double encodage UTF-8) : {2}" -f $fichier, $numero, $extrait))
        }
    }
}

# --- 2. BOM UTF-8 des scripts PowerShell -------------------------------------------------------------
$scripts = @(git -c core.quotepath=false ls-files -- '*.ps1')
foreach ($script in $scripts) {
    if ([string]::IsNullOrWhiteSpace($script)) { continue }
    $chemin = Join-Path $root $script
    if (-not (Test-Path -LiteralPath $chemin)) { continue }
    $debut = New-Object byte[] 3
    $flux = [System.IO.File]::OpenRead($chemin)
    try { $lus = $flux.Read($debut, 0, 3) } finally { $flux.Dispose() }
    if ($lus -lt 3 -or $debut[0] -ne 0xEF -or $debut[1] -ne 0xBB -or $debut[2] -ne 0xBF) {
        $problemes.Add(("{0}:1 : script PowerShell sans BOM UTF-8 (Windows PowerShell 5.1 lirait les accents en ANSI)" -f $script))
    }
}

if ($problemes.Count -gt 0) {
    Write-Host "Contrôle d'encodage : $($problemes.Count) problème(s)" -ForegroundColor Red
    foreach ($p in $problemes) { Write-Host "  $p" }
    exit 1
}

Write-Host ("Contrôle d'encodage : OK ({0} fichiers texte, {1} scripts PowerShell avec BOM)." -f $analyses, $scripts.Count) -ForegroundColor Green
exit 0
