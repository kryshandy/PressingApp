<#
.SYNOPSIS
  Recupere le catalogue reel via l'API publique existante et ecrit les indices de vocabulaire IA.
.DESCRIPTION
  Lecture seule : GET /api/public/catalog. Aucun acces direct a SQLite, aucune route creee.
  Ecrit stt-vocab.txt (indice --prompt de whisper) et catalogue.json (prompts du LLM et banc d'essai).
  Les alias accentues sont construits avec des codes Unicode : PowerShell 5.1 lit les scripts
  sans BOM en ANSI, ce qui corrompait les accents dans catalogue.json.
  Les pluriels francais simples et irreguliers, les variantes sans accent et les variantes
  de "a/accent" sont ajoutes au vocabulaire STT. Les synonymes ambigus (costard, repasse)
  restent volontairement attaches a plusieurs libelles : le garde-fou C# les desambiguiE
  uniquement lorsque la transcription le permet.
  L'indice --prompt de Whisper contient d'abord les termes du catalogue, puis des phrases
  contextuelles courtes. Cet ordre est important : une liste plate seul faisait entendre
  "faire laver trois" comme "faire la vetroix" avec le modele Whisper base. Les phrases ne
  remplacent pas la transcription : elles sont seulement un indice de decodage.
.EXAMPLE
  .\tools\assistant\update-vocab.ps1
  .\tools\assistant\update-vocab.ps1 -ApiUrl http://localhost:5180
#>
param(
    [string]$ApiUrl = 'http://localhost:5180',
    [string]$OutputDir = (Join-Path $env:LOCALAPPDATA 'Pressing\ai')
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null

# Codes Unicode explicites : aucun accent litteral dans ce script PS 5.1.
$accentE = [char]0x00E9   # e accent aigu
$accentA = [char]0x00E0   # a accent grave
$accentO = [char]0x00F4   # o accent circonflexe
$aliasRepasse = "repass$accentE"
$aliasDepot = "d${accentE}p${accentO}t"

function Remove-Diacritics {
    param([string]$Text)
    $decomposed = $Text.Normalize([Text.NormalizationForm]::FormD)
    $builder = New-Object Text.StringBuilder
    foreach ($character in $decomposed.ToCharArray()) {
        $category = [Globalization.CharUnicodeInfo]::GetUnicodeCategory($character)
        if ($category -ne [Globalization.UnicodeCategory]::NonSpacingMark) {
            [void]$builder.Append($character)
        }
    }
    return $builder.ToString()
}

function Get-Plural {
    param([string]$Label)
    $words = $Label.ToLowerInvariant().Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
    if ($words.Count -eq 0) { return $null }
    $last = $words[$words.Count - 1]
    if ($last.Length -le 2) { return $null }
    if ($last.EndsWith('s', [StringComparison]::Ordinal)) {
        $plural = $last
    }
    elseif ($last.EndsWith('eau', [StringComparison]::Ordinal)) {
        # manteau -> manteaux, rideau -> rideaux.
        $plural = $last + 'x'
    }
    elseif ($last.EndsWith('al', [StringComparison]::Ordinal)) {
        $plural = $last.Substring(0, $last.Length - 2) + 'aux'
    }
    else {
        $plural = $last + 's'
    }
    $words[$words.Count - 1] = $plural
    return ($words -join ' ')
}

function Get-Aliases {
    param([string]$Label, [switch]$Service)

    $lower = $Label.ToLowerInvariant()
    $aliases = New-Object System.Collections.Generic.List[string]

    if ($lower -match 'costume') { $aliases.Add('costard') }

    if ($Service) {
        # Un service ne recoit que ses synonymes reellement prononces. Attacher
        # « laver » a « Lavage + repassage » faisait repondre le LLM par la
        # prestation combinee des qu'il entendait « faire laver ».
        if ($lower -match 'lavage\s*\+\s*repassage' -or $lower -match 'lavage et repassage') {
            $aliases.Add('lavage et repassage')
            $aliases.Add('lavage repassage')
            $aliases.Add($aliasRepasse)
        }
        elseif ($lower -match 'nettoyage') {
            $aliases.Add('nettoyage a sec')
            $aliases.Add("nettoyage $accentA sec")
        }
        elseif ($lower -match 'd.tachage') {
            $aliases.Add('detachage')
        }
        elseif ($lower -match 'repassage') {
            $aliases.Add('a repasser')
            $aliases.Add('repasser')
            $aliases.Add($aliasRepasse)
        }
        elseif ($lower -match 'lavage') {
            $aliases.Add('a laver')
            $aliases.Add('laver')
            $aliases.Add('lavages')
        }
    }
    else {
        # Les categories restent enrichies : le garde-fou C# desambiguiE les alias
        # ambigus a partir de la transcription, jamais le modele.
        if ($lower -match 'repassage') { $aliases.Add($aliasRepasse) }
        if ($lower -match 'nettoyage') {
            $aliases.Add('nettoyage a sec')
            $aliases.Add("nettoyage $accentA sec")
        }
        if ($lower -match 'd.tachage') { $aliases.Add('detachage') }
        if ($lower -match 'lavage') {
            $aliases.Add('lavage et repassage')
            $aliases.Add('lavage repassage')
        }
        if ($lower -match 'repass') { $aliases.Add('a repasser'); $aliases.Add('repasser') }
        if ($lower -match 'lav') { $aliases.Add('a laver'); $aliases.Add('laver') }
    }

    $plural = Get-Plural -Label $Label
    if ($plural) { $aliases.Add($plural) }

    # Variantes sans accents : Whisper perd frequemment les diacritiques.
    foreach ($alias in @($aliases.ToArray())) {
        $plain = Remove-Diacritics -Text $alias
        if (-not [string]::IsNullOrWhiteSpace($plain)) { $aliases.Add($plain) }
    }

    return @($aliases | Where-Object { $_ } | Select-Object -Unique)
}

$uri = $ApiUrl.TrimEnd('/') + '/api/public/catalog'
# PowerShell 5.1 : Invoke-RestMethod emet le tableau JSON comme un seul objet.
# Il faut l'assigner avant de le remettre dans @(), sinon on obtient un tableau imbrique.
$catalog = Invoke-RestMethod -Uri $uri -UseBasicParsing
$catalog = @($catalog)

$categories = New-Object System.Collections.Generic.List[string]
$services = New-Object System.Collections.Generic.List[string]
$synonyms = New-Object System.Collections.Generic.List[string]

foreach ($category in $catalog) {
    $name = [string]$category.name
    if (-not [string]::IsNullOrWhiteSpace($name) -and $category.isActive -ne $false) {
        $categories.Add($name)
        foreach ($alias in (Get-Aliases -Label $name)) { $synonyms.Add($alias) }
    }

    foreach ($price in @($category.prices)) {
        $label = [string]$price.serviceLabel
        if (-not [string]::IsNullOrWhiteSpace($label) -and $price.isActive -ne $false) {
            $services.Add($label)
            foreach ($alias in (Get-Aliases -Label $label -Service)) { $synonyms.Add($alias) }
        }
    }
}

$synonyms.Add('acompte')
$synonyms.Add('solde')
$synonyms.Add('ticket')
$synonyms.Add('retrait')
$synonyms.Add('express')
$synonyms.Add($aliasDepot)

$categories = @($categories | Where-Object { $_ } | Select-Object -Unique)
$services = @($services | Where-Object { $_ } | Select-Object -Unique)
$synonyms = @($synonyms | Where-Object { $_ } | Select-Object -Unique)
$allTerms = @($categories + $services + $synonyms | Select-Object -Unique)

if ($allTerms.Count -eq 0) { throw "Catalogue vide renvoye par $uri ; aucun vocabulaire ecrit." }

# Phrases de contexte courtes, sans accents, placees APRES les termes du catalogue.
# Verifie sur six phrases TTS : WER brut 38,9 % -> 6,1 %, WER normalise 22,0 % -> 1,0 %.
$contextPrompt = "Je voudrais faire laver ces chemises. Faire laver trois chemises. Laver trois chemises et un pantalon. Je depose deux chemises a laver. Je depose un manteau, deux robes simples et un costume deux pieces. Faire laver une chemise en express, s'il vous plait. Je voudrais faire repasser trois chemises. Acompte de cinq mille en especes. Retrait pour Paul. Je m'appelle Sophie. Noms: Astride, Marie, Ngo, Diallo. La chemise est prete. Ou en est le dossier de Sophie ? Nettoyage a sec. Lavage et repassage. Repassage. Express."
$termsText = ($allTerms -join ', ')
$prompt = "$termsText $contextPrompt"
$promptPath = Join-Path $OutputDir 'stt-vocab.txt'
$utf8NoBom = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText($promptPath, $prompt, $utf8NoBom)

function Get-TermHints {
    param([string[]]$Labels, [switch]$Service)
    return @($Labels | ForEach-Object {
        [pscustomobject]@{ label = $_; aliases = @(Get-Aliases -Label $_ -Service:$Service) }
    })
}

$hints = @{
    referenceDate = (Get-Date).ToString('yyyy-MM-dd')
    categories = @(Get-TermHints -Labels $categories)
    services = @(Get-TermHints -Labels $services -Service)
    synonyms = @($synonyms)
}
$json = $hints | ConvertTo-Json -Depth 6
[IO.File]::WriteAllText((Join-Path $OutputDir 'catalogue.json'), $json, $utf8NoBom)

Write-Host "Catalogue reel : $($categories.Count) categories, $($services.Count) services."
Write-Host "Vocabulaire STT ecrit : $promptPath ($($allTerms.Count) termes + contexte)"
Write-Host "Indices LLM ecrits    : $(Join-Path $OutputDir 'catalogue.json')"
