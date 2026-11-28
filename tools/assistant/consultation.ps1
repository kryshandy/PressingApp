<#
.SYNOPSIS
  T1.6 : consultation en lecture seule, affichee a l'ecran.
.DESCRIPTION
  Demonstration M1 : phrase ou WAV -> intention JSON -> reponse lisible.
  Aucun acces SQLite, aucune commande creee, aucune route M2 ajoutee.
  Le resultat reste un brouillon a confirmer par M2 puis par l'humain.
.EXAMPLE
  .\tools\assistant\consultation.ps1 -Texte "Ou en est le ticket 142 ?"
.EXAMPLE
  .\tools\assistant\consultation.ps1 -Wav "C:\temp\question.wav"
.EXAMPLE
  .\tools\assistant\consultation.ps1 -Json '{"intent":"find_order",...}'
#>
param(
    [string]$Texte,
    [string]$Wav,
    [string]$Json,
    [string]$SttUrl = 'http://127.0.0.1:8082',
    [string]$LlmUrl = 'http://127.0.0.1:8081'
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8; $OutputEncoding = [Text.Encoding]::UTF8 } catch { }

$assistantDir = $PSScriptRoot
$repoRoot = Split-Path -Parent (Split-Path -Parent $assistantDir)
$aiRoot = Join-Path $env:LOCALAPPDATA 'Pressing\ai'
$catalogue = Join-Path $aiRoot 'catalogue.json'

$inputs = @($Texte, $Wav, $Json)
$count = @($inputs | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count
if ($count -ne 1) { throw 'Fournissez exactement une entree : -Texte, -Wav ou -Json.' }

function Get-PressingSetting {
    param([string]$Name, [string]$Default)
    $value = [Environment]::GetEnvironmentVariable($Name)
    if (-not [string]::IsNullOrWhiteSpace($value)) { return $value }
    $envPath = Join-Path $repoRoot '.env'
    if (Test-Path $envPath) {
        foreach ($line in Get-Content $envPath) {
            if ($line -match "^\s*$([regex]::Escape($Name))\s*=\s*(.*)$") {
                return $matches[1].Trim().Trim('"', "'")
            }
        }
    }
    return $Default
}

function Get-PromptBlock {
    param($Items)
    if (-not $Items -or $Items.Count -eq 0) { return '(aucun)' }
    return (@($Items | ForEach-Object {
        $label = [string]$_.label
        $aliases = @($_.aliases | Where-Object { $_ })
        if ($aliases.Count -gt 0) { "$label ($($aliases -join ', '))" } else { $label }
    }) -join ', ')
}

if (-not [string]::IsNullOrWhiteSpace($Wav)) {
    if (-not (Test-Path $Wav)) { throw "WAV absent : $Wav" }
    if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) { throw 'curl.exe requis.' }
    $sttRaw = & curl.exe -sS -F "file=@$Wav" -F 'language=fr' -F 'response_format=json' "$SttUrl/inference" 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Whisper indisponible ou WAV refuse.' }
    $stt = ($sttRaw -join "`n") | ConvertFrom-Json
    $transcript = [string]$stt.text
    if ([string]::IsNullOrWhiteSpace($transcript)) { throw 'Transcription vide.' }
}
elseif (-not [string]::IsNullOrWhiteSpace($Json)) {
    $transcript = '(JSON fourni directement)'
}
else {
    $transcript = $Texte
}

if ([string]::IsNullOrWhiteSpace($Json)) {
    $referenceDate = (Get-Date).ToString('yyyy-MM-dd')
    $categories = @()
    $services = @()
    $synonyms = @()
    if (Test-Path $catalogue) {
        $loaded = [IO.File]::ReadAllText($catalogue, [Text.Encoding]::UTF8) | ConvertFrom-Json
        $referenceDate = [string]$loaded.referenceDate
        $categories = @($loaded.categories)
        $services = @($loaded.services)
        $synonyms = @($loaded.synonyms)
    }
    $catalogText = "Categories : $(Get-PromptBlock $categories)`nServices : $(Get-PromptBlock $services)`nSynonymes : $(@($synonyms) -join ', ')"
    $template = [IO.File]::ReadAllText((Join-Path $assistantDir 'prompt-systeme.txt'), [Text.Encoding]::UTF8)
    $system = $template.Replace('{{DATE_REFERENCE}}', $referenceDate).Replace('{{CATALOGUE}}', $catalogText)
    $schema = [IO.File]::ReadAllText((Join-Path $assistantDir 'intent-schema.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json

    $body = @{
        model = (Get-PressingSetting 'ASSISTANT_LLM_MODEL_ALIAS' 'pressing-local')
        messages = @(
            @{ role = 'system'; content = $system }
            @{ role = 'user'; content = $transcript }
        )
        temperature = 0.1
        max_tokens = 512
        response_format = @{ type = 'json_schema'; json_schema = @{ name = 'pressing_intent'; strict = $true; schema = $schema } }
    }
    $bodyFile = Join-Path $env:TEMP ("pressing-consultation-" + [guid]::NewGuid().ToString('N') + '.json')
    try {
        $body | ConvertTo-Json -Depth 20 -Compress | Set-Content $bodyFile -Encoding UTF8
        $llmRaw = & curl.exe -sS -H 'Content-Type: application/json' --data-binary "@$bodyFile" "$LlmUrl/v1/chat/completions" 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'LLM indisponible.' }
    }
    finally { if (Test-Path $bodyFile) { Remove-Item $bodyFile -Force } }
    $llm = ($llmRaw -join "`n") | ConvertFrom-Json
    $Json = [string]$llm.choices[0].message.content
    if ([string]::IsNullOrWhiteSpace($Json)) { throw 'Reponse LLM vide.' }
}

$draft = $Json | ConvertFrom-Json
foreach ($key in @('intent','client','items','ticket','amount','payment_method','deposit','pickup_date','item_status','confidence','transcript')) {
    if ($null -eq $draft.PSObject.Properties[$key]) { throw "Brouillon incomplet : champ $key absent." }
}

$reply = if ($draft.intent -eq 'find_order') {
    if ([string]::IsNullOrWhiteSpace([string]$draft.ticket)) {
        'Consultation : aucun ticket identifie dans la phrase. Aucune donnee metier consulte : reponse a confirmer par M2 puis par l humain.'
    }
    else {
        "Consultation du ticket $($draft.ticket) : brouillon pret. Aucune donnee metier consulte : reponse a confirmer par M2 puis par l humain."
    }
}
else { 'Cette phrase ne demande pas une consultation.' }

Write-Host "Phrase  : $transcript"
Write-Host "Intention : $($draft.intent)"
Write-Host "Reponse : $reply" -ForegroundColor Green
Write-Host "Brouillon JSON : $Json" -ForegroundColor DarkGray
