<#
.SYNOPSIS
  Valide la verite terrain d'un jeu JSONL avant toute execution du banc.
.DESCRIPTION
  Verifie le format, les identifiants, la source, la recopie exacte
  de la reference et la coherence des libelles avec le catalogue local.
  Avec -RequireAudio, verifie aussi les WAV reels (16 kHz mono PCM 16 bits, <= 20 s).
  Aucun appel reseau, aucun LLM, aucune ecriture.
.EXAMPLE
  .\tools\assistant\eval\valider-dataset.ps1
.EXAMPLE
  .\tools\assistant\eval\valider-dataset.ps1 -Source reel -Prefix R -RequireAudio -AudioRoot .\tools\assistant\eval
#>
param(
    [string]$Dataset = (Join-Path $PSScriptRoot 'dataset-synthetique.jsonl'),
    [string]$Catalogue = (Join-Path $env:LOCALAPPDATA 'Pressing\ai\catalogue.json'),
    [int]$Count = 150,
    [string]$Prefix = 'S',
    [ValidateSet('synthetique', 'reel')]
    [string]$Source = 'synthetique',
    [switch]$RequireAudio,
    [string]$AudioRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8; $OutputEncoding = [Text.Encoding]::UTF8 } catch { }

if (-not (Test-Path $Dataset)) { throw "Jeu absent : $Dataset" }
if (-not (Test-Path $Catalogue)) { throw "Catalogue absent : $Catalogue" }

$catalog = [IO.File]::ReadAllText($Catalogue, [Text.Encoding]::UTF8) | ConvertFrom-Json
$categories = @($catalog.categories | ForEach-Object { [string]$_.label })
$services = @($catalog.services | ForEach-Object { [string]$_.label })
$intents = @('create_order', 'register_pickup', 'add_payment', 'update_item_status', 'find_order', 'unknown')
$payments = @('cash', 'mobile_money', 'card', 'transfer')
$statuses = @('in_progress', 'ready')

$lines = @([IO.File]::ReadAllLines($Dataset, [Text.Encoding]::UTF8) | Where-Object { $_.Trim() })
if ($lines.Count -ne $Count) { throw "Le jeu contient $($lines.Count) lignes au lieu de $Count." }

$ids = New-Object System.Collections.Generic.HashSet[string]
$errors = New-Object System.Collections.Generic.List[string]
$distribution = @{}

function Test-RealWav {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return "fichier absent : $Path" }
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 44) { return 'WAV trop court' }
    $riff = [Text.Encoding]::ASCII.GetString($bytes, 0, 4)
    $wave = [Text.Encoding]::ASCII.GetString($bytes, 8, 4)
    if ($riff -cne 'RIFF' -or $wave -cne 'WAVE') { return 'en-tete RIFF/WAVE invalide' }
    $format = [BitConverter]::ToUInt16($bytes, 20)
    $channels = [BitConverter]::ToUInt16($bytes, 22)
    $sampleRate = [BitConverter]::ToInt32($bytes, 24)
    $bits = [BitConverter]::ToUInt16($bytes, 34)
    $dataLength = [BitConverter]::ToInt32($bytes, 40)
    if ($format -ne 1) { return 'format PCM requis' }
    if ($channels -ne 1) { return 'mono requis' }
    if ($sampleRate -ne 16000) { return '16 kHz requis' }
    if ($bits -ne 16) { return 'PCM 16 bits requis' }
    if ($dataLength -le 0) { return 'donnees audio vides' }
    $seconds = $dataLength / ([double]$sampleRate * $channels * ($bits / 8))
    if ($seconds -gt 20) { return "duree $seconds s > 20 s" }
    return $null
}

for ($i = 0; $i -lt $lines.Count; $i++) {
    $row = $null
    try { $row = $lines[$i] | ConvertFrom-Json } catch { $errors.Add("Ligne $($i + 1) : JSON invalide") ; continue }
    $id = [string]$row.id
    if (-not $ids.Add($id)) { $errors.Add("$id : identifiant duplique") }
    if ($id -ne ('{0}{1:D3}' -f $Prefix, ($i + 1))) { $errors.Add("Ligne $($i + 1) : identifiant inattendu $id") }
    if ([string]$row.source -cne $Source) { $errors.Add("$id : source attendue $Source") }
    if ($Source -eq 'reel') {
        $consentement = [string]$row.consentement
        if ([string]::IsNullOrWhiteSpace($consentement) -or $consentement -match 'attente|à recueillir|a recueillir') {
            $errors.Add("$id : consentement explicite non renseigne")
        }
    }
    if ($RequireAudio) {
        $audio = [string]$row.audio
        if ([string]::IsNullOrWhiteSpace($audio)) { $errors.Add("$id : champ audio absent") }
        else {
            if (-not [IO.Path]::IsPathRooted($audio)) { $audio = Join-Path $AudioRoot $audio }
            $wavError = Test-RealWav -Path $audio
            if ($wavError) { $errors.Add("$id : $wavError") }
        }
    }
    elseif ($null -ne $row.audio) { $errors.Add("$id : audio non nul") }
    $reference = [string]$row.reference
    if ($reference -cne [string]$row.expected.transcript) { $errors.Add("$id : transcript different de la reference") }

    $expected = $row.expected
    $intent = [string]$expected.intent
    if ($intents -notcontains $intent) { $errors.Add("$id : intention inconnue $intent") }
    if (-not $distribution.ContainsKey($intent)) { $distribution[$intent] = 0 }
    $distribution[$intent]++

    if ($intent -eq 'unknown' -and @($expected.items).Count -ne 0) {
        $errors.Add("$id : unknown avec des articles")
    }
    if ($null -ne $expected.payment_method -and $payments -notcontains [string]$expected.payment_method) {
        $errors.Add("$id : payment_method invalide")
    }
    if ($null -ne $expected.item_status -and $statuses -notcontains [string]$expected.item_status) {
        $errors.Add("$id : item_status invalide")
    }
    if ($null -ne $expected.amount -and [double]$expected.amount -lt 0) { $errors.Add("$id : amount negatif") }
    if ($null -ne $expected.deposit -and $null -ne $expected.amount -and [double]$expected.deposit -gt [double]$expected.amount) {
        $errors.Add("$id : deposit > amount")
    }

    foreach ($item in @($expected.items)) {
        if ($null -ne $item.category -and $categories -notcontains [string]$item.category) {
            $errors.Add("$id : categorie absente du catalogue : $($item.category)")
        }
        if ($null -ne $item.service -and $services -notcontains [string]$item.service) {
            $errors.Add("$id : service absent du catalogue : $($item.service)")
        }
        if ($null -ne $item.quantity -and [int]$item.quantity -lt 1) { $errors.Add("$id : quantite < 1") }
        if ($null -ne $item.express -and [string]$item.express -notin @('True', 'False')) {
            $errors.Add("$id : express n'est pas un booleen")
        }
    }
}

if ($errors.Count -gt 0) {
    foreach ($err in $errors) { Write-Host "ERREUR : $err" -ForegroundColor Red }
    throw "$($errors.Count) erreur(s) dans le jeu."
}

Write-Host ("Jeu valide : {0} lignes, source {1}, identifiants {2}001..{2}{3:D3} uniques." -f $lines.Count, $Source, $Prefix, $Count) -ForegroundColor Green
if ($RequireAudio) { Write-Host 'Audio verifie : WAV 16 kHz mono PCM 16 bits, <= 20 s.' -ForegroundColor DarkCyan }
$distribution.GetEnumerator() | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0,-22} {1,3}" -f $_.Key, $_.Value)
}
Write-Host 'Libelles verifies contre le catalogue local ; aucun appel LLM.' -ForegroundColor DarkCyan
