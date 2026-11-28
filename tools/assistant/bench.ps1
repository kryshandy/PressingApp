<#
.SYNOPSIS
  Mesure le banc d'essai IA local à partir d'enregistrements réels annotés.
.DESCRIPTION
  Appelle uniquement les serveurs locaux (STT 8082, LLM 8081). N'écrit RESULTATS.md
  que si toutes les phrases sont présentes et traitées. Aucun résultat n'est simulé.
.EXAMPLE
  .\tools\assistant\bench.ps1
  .\tools\assistant\bench.ps1 -LlmModel 3B -WhisperModel small
#>
param(
    [string]$Dataset = (Join-Path $PSScriptRoot 'eval\dataset.jsonl'),
    [string]$AudioRoot = (Join-Path $PSScriptRoot 'eval'),
    [string]$SttUrl = 'http://127.0.0.1:8082',
    [string]$LlmUrl = 'http://127.0.0.1:8081',
    [string]$Catalogue = (Join-Path $env:LOCALAPPDATA 'Pressing\ai\catalogue.json'),
    [string]$Output = (Join-Path $PSScriptRoot 'RESULTATS.md'),
    [ValidateSet('1.5B', '3B')]
    [string]$LlmModel = '1.5B',
    [ValidateSet('base', 'small')]
    [string]$WhisperModel = 'base',
    [ValidateSet('reel', 'tts')]
    [string]$Source = 'reel',
    [string]$RawDump
)

$ErrorActionPreference = 'Stop'
$assistantDir = $PSScriptRoot

if (-not (Test-Path $Dataset)) {
    throw "Jeu d'évaluation absent : $Dataset. Consultez tools\assistant\eval\README.md. Aucun résultat n'est écrit."
}
if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) {
    throw 'curl.exe est requis pour appeler les moteurs locaux. Aucun résultat n''est écrit.'
}

function Test-LoopbackPort { param([int]$Port)
    $client = New-Object System.Net.Sockets.TcpClient
    try { $client.Connect('127.0.0.1', $Port); return $true } catch { return $false } finally { $client.Close() }
}
if (-not (Test-LoopbackPort 8082)) { throw 'whisper-server ne répond pas sur 127.0.0.1:8082. Lancez start-ai.ps1.' }
if (-not (Test-LoopbackPort 8081)) { throw 'llama-server ne répond pas sur 127.0.0.1:8081. Lancez start-ai.ps1.' }

$hints = @{
    referenceDate = (Get-Date).ToString('yyyy-MM-dd')
    categories = @()
    services = @()
    synonyms = @()
}
if (Test-Path $Catalogue) {
    $loaded = [IO.File]::ReadAllText($Catalogue, [Text.Encoding]::UTF8) | ConvertFrom-Json
    $hints.referenceDate = [string]$loaded.referenceDate
    $hints.categories = @($loaded.categories)
    $hints.services = @($loaded.services)
    $hints.synonyms = @($loaded.synonyms)
}

$template = [IO.File]::ReadAllText((Join-Path $assistantDir 'prompt-systeme.txt'), [Text.Encoding]::UTF8)
function Get-PromptBlock { param($Items)
    if (-not $Items -or $Items.Count -eq 0) { return '(aucun)' }
    return (@($Items | ForEach-Object {
        $label = [string]$_.label
        $aliases = @($_.aliases | Where-Object { $_ })
        if ($aliases.Count -gt 0) { "$label ($($aliases -join ', '))" } else { $label }
    }) -join ', ')
}
$catalogText = "Catégories : $(Get-PromptBlock $hints.categories)`nServices : $(Get-PromptBlock $hints.services)`nSynonymes : $(@($hints.synonyms) -join ', ')"
$system = $template.Replace('{{DATE_REFERENCE}}', $hints.referenceDate).Replace('{{CATALOGUE}}', $catalogText)
$schema = [IO.File]::ReadAllText((Join-Path $assistantDir 'intent-schema.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json

$script:fieldTotal = 0
$script:fieldOk = 0
$script:intentTotal = 0
$script:intentOk = 0
$script:itemTotal = 0
$script:itemOk = 0
$script:schemaTotal = 0
$script:schemaOk = 0
$script:werTotal = 0.0
$latencies = New-Object System.Collections.Generic.List[double]
# Trace brute facultative : permet de rejouer les memes reponses a travers les
# garde-fous C# avec EvaluationHarness --recompute-from-jsonl, sans nouvel appel reseau.
$rawRecords = New-Object System.Collections.Generic.List[object]
$rows = @([IO.File]::ReadAllLines($Dataset, [Text.Encoding]::UTF8) | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })

function Get-Wer { param([string]$Reference, [string]$Hypothesis)
    $a = @($Reference -split '\s+' | Where-Object { $_ })
    $b = @($Hypothesis -split '\s+' | Where-Object { $_ })
    if ($a.Count -eq 0) { return $(if ($b.Count -eq 0) { 0.0 } else { 1.0 }) }
    $d = New-Object 'int[,]' ($a.Count + 1), ($b.Count + 1)
    for ($i = 0; $i -le $a.Count; $i++) { $d[$i, 0] = $i }
    for ($j = 0; $j -le $b.Count; $j++) { $d[0, $j] = $j }
    for ($i = 1; $i -le $a.Count; $i++) {
        for ($j = 1; $j -le $b.Count; $j++) {
            $cost = if ($a[$i - 1] -ceq $b[$j - 1]) { 0 } else { 1 }
            $up = $d[($i - 1), $j] + 1
            $left = $d[$i, ($j - 1)] + 1
            $diagonal = $d[($i - 1), ($j - 1)] + $cost
            $d[$i, $j] = [Math]::Min([Math]::Min($up, $left), $diagonal)
        }
    }
    return ([double]$d[$a.Count, $b.Count] / [double]$a.Count)
}

function Assert-FieldEqual {
    param([string]$Name, $Actual, $Expected)
    $script:fieldTotal++
    $ok = $false
    if ($null -eq $Expected) {
        $ok = $null -eq $Actual -or [string]::IsNullOrWhiteSpace([string]$Actual)
    }
    elseif ($Expected -is [bool]) { $ok = $Actual -is [bool] -and [bool]$Actual -eq [bool]$Expected }
    elseif ($Expected -is [int] -or $Expected -is [long] -or $Expected -is [double] -or $Expected -is [decimal]) {
        $ok = $null -ne $Actual -and [decimal]$Actual -eq [decimal]$Expected
    }
    else { $ok = $null -ne $Actual -and ([string]$Actual).Trim() -ieq ([string]$Expected).Trim() }
    if ($ok) { $script:fieldOk++ }
    return $ok
}

function Test-SchemaShape { param($Object)
    if ($null -eq $Object) { return $false }
    foreach ($key in @('intent','client','items','ticket','amount','payment_method','deposit','pickup_date','item_status','confidence','transcript')) {
        if ($null -eq $Object.PSObject.Properties[$key]) { return $false }
    }
    return $true
}

foreach ($row in $rows) {
    if ([string]$row.source -cne $Source) {
        throw "Ligne $($row.id) : source '$Source' attendue. Aucun résultat n'est écrit."
    }
    $consentement = [string]$row.consentement
    if ($Source -eq 'reel' -and ([string]::IsNullOrWhiteSpace($consentement) -or $consentement -match 'attente|à recueillir|a recueillir')) {
        throw "Ligne $($row.id) : consentement explicite non renseigné. Aucun résultat n'est écrit."
    }
    if ($Source -eq 'tts' -and $consentement -notmatch 'sans objet|synthese') {
        throw "Ligne $($row.id) : jeu TTS attendu avec consentement 'sans objet - voix de synthese'."
    }
    $audio = [string]$row.audio
    if ([string]::IsNullOrWhiteSpace($audio)) { throw "Ligne $($row.id) : champ audio absent." }
    if (-not [System.IO.Path]::IsPathRooted($audio)) { $audio = Join-Path $AudioRoot $audio }
    if (-not (Test-Path $audio)) { throw "Ligne $($row.id) : fichier audio absent : $audio" }

    $watch = [Diagnostics.Stopwatch]::StartNew()
    $sttRaw = & curl.exe -sS -F "file=@$audio" -F 'language=fr' -F 'response_format=json' "$SttUrl/inference" 2>$null
    if ($LASTEXITCODE -ne 0) { throw "STT a échoué pour $($row.id)." }
    $stt = ($sttRaw -join "`n") | ConvertFrom-Json
    $transcript = [string]$stt.text
    if ([string]::IsNullOrWhiteSpace($transcript)) { throw "STT a renvoyé une transcription vide pour $($row.id)." }

    $body = @{
        model = 'pressing-local'
        messages = @(
            @{ role = 'system'; content = $system },
            @{ role = 'user'; content = $transcript }
        )
        temperature = 0.1
        max_tokens = 512
        response_format = @{ type = 'json_schema'; json_schema = @{ name = 'pressing_intent'; strict = $true; schema = $schema } }
    }
    $bodyFile = Join-Path $env:TEMP ("pressing-bench-" + [guid]::NewGuid().ToString('N') + '.json')
    try {
        $body | ConvertTo-Json -Depth 20 -Compress | Set-Content $bodyFile -Encoding UTF8
        $llmRaw = & curl.exe -sS -H 'Content-Type: application/json' --data-binary "@$bodyFile" "$LlmUrl/v1/chat/completions" 2>$null
        if ($LASTEXITCODE -ne 0) { throw "LLM a échoué pour $($row.id)." }
    }
    finally { if (Test-Path $bodyFile) { Remove-Item $bodyFile -Force } }
    $watch.Stop()
    $latencies.Add($watch.Elapsed.TotalSeconds)

    $llm = ($llmRaw -join "`n") | ConvertFrom-Json
    $content = [string]$llm.choices[0].message.content
    if ([string]::IsNullOrWhiteSpace($content)) { throw "LLM a renvoyé une réponse vide pour $($row.id)." }
    $actual = $content | ConvertFrom-Json

    if ($RawDump) {
        $rawRecords.Add([pscustomobject][ordered]@{
            id           = [string]$row.id
            source       = $Source
            reference    = [string]$row.reference
            expected     = $row.expected
            raw          = $actual
            totalSeconds = $watch.Elapsed.TotalSeconds
        })
    }

    $script:schemaTotal++
    if (Test-SchemaShape $actual) { $script:schemaOk++ }

    $expected = $row.expected
    if ($null -eq $expected) { throw "Ligne $($row.id) : expected absent." }

    $script:intentTotal++
    if (Assert-FieldEqual 'intent' $actual.intent $expected.intent) { $script:intentOk++ }

    if ($null -ne $expected.client) {
        Assert-FieldEqual 'client.name' $actual.client.name $expected.client.name | Out-Null
        Assert-FieldEqual 'client.phone' $actual.client.phone $expected.client.phone | Out-Null
    }
    foreach ($key in @('ticket','amount','payment_method','deposit','pickup_date','item_status','transcript')) {
        if ($null -ne $expected.PSObject.Properties[$key]) {
            $value = if ($key -eq 'transcript') { $transcript } else { $actual.PSObject.Properties[$key].Value }
            Assert-FieldEqual $key $value $expected.PSObject.Properties[$key].Value | Out-Null
        }
    }

    $expectedItems = @($expected.items)
    $actualItems = @($actual.items)
    $script:itemTotal += $expectedItems.Count
    for ($i = 0; $i -lt $expectedItems.Count; $i++) {
        if ($i -ge $actualItems.Count) { continue }
        $all = $true
        foreach ($key in @('category','service','quantity','express')) {
            if (-not (Assert-FieldEqual "items[$i].$key" $actualItems[$i].$key $expectedItems[$i].$key)) { $all = $false }
        }
        if ($all) { $script:itemOk++ }
    }

    $script:werTotal += Get-Wer -Reference ([string]$row.reference) -Hypothesis $transcript
}

$sorted = @($latencies | Sort-Object)
$p50 = [double]$sorted[[Math]::Floor(0.5 * ($sorted.Count - 1))]
$p95 = [double]$sorted[[Math]::Min($sorted.Count - 1, [Math]::Ceiling(0.95 * ($sorted.Count - 1)))]

function Get-Percent { param([int]$Ok, [int]$Total)
    if ($Total -eq 0) { return 'n/a' }
    return ('{0:P1}' -f ([double]$Ok / [double]$Total))
}

$complete = $rows.Count -eq 150
$verdict = if ($complete -and ($script:intentOk / $script:intentTotal) -ge 0.9 -and ($script:itemOk / $script:itemTotal) -ge 0.85 -and $p95 -lt 5.0 -and $script:schemaOk -eq $script:schemaTotal) {
    if ($Source -eq 'tts') { 'BANC TTS (non réel) - CONFORME aux seuils techniques.' } else { 'CONFORME aux seuils de la feuille de route v3.' }
} else {
    'NON CONFORME ou jeu incomplet : ne pas publier comme validation.'
}

$report = @"
# Résultats du banc d'essai — assistant vocal local

## Mesure $(if ($Source -eq 'reel') { 'réelle' } else { 'TTS synthétique - non réel' }) du $(Get-Date -Format 'yyyy-MM-dd HH:mm')

- Jeu : $Dataset
- Source : $Source
- Phrases traitées : $($rows.Count) / 150 $(if ($complete) { '(complet)' } else { '(INCOMPLET)' })
- LLM : $LlmModel · STT : $WhisperModel
- Serveurs : $LlmUrl et $SttUrl (127.0.0.1 uniquement)

| Mesure | Résultat | Objectif |
|---|---:|---:|
| Intentions exactes | $(Get-Percent $script:intentOk $script:intentTotal) ($script:intentOk/$script:intentTotal) | ≥ 90 % |
| Articles exacts | $(Get-Percent $script:itemOk $script:itemTotal) ($script:itemOk/$script:itemTotal) | ≥ 85 % |
| Champs exacts | $(Get-Percent $script:fieldOk $script:fieldTotal) ($script:fieldOk/$script:fieldTotal) | informationnel |
| Sorties conformes au schéma | $(Get-Percent $script:schemaOk $script:schemaTotal) ($script:schemaOk/$script:schemaTotal) | 100 % |
| WER moyen | $('{0:P1}' -f ($script:werTotal / [Math]::Max(1, $rows.Count))) | comparatif base/small |
| Latence p50 | $('{0:F2}' -f $p50) s | informationnel |
| Latence p95 | $('{0:F2}' -f $p95) s | < 5 s |

Verdict : $verdict

La latence est mesurée entre l'envoi du WAV au STT et la réception du JSON du LLM. Aucune donnée n'est envoyée hors de la machine.
$(if ($Source -eq 'tts') { "`nCe banc utilise une voix de synthese Windows : les WER et latences mesurent la chaine technique, pas la clientele reelle. Aucun resultat ne doit etre publie comme WER client." } else { '' })
"@
$report | Set-Content -Path $Output -Encoding UTF8
if ($RawDump) {
    $lines = @($rawRecords | ForEach-Object { $_ | ConvertTo-Json -Compress -Depth 20 })
    [IO.File]::WriteAllLines((Join-Path (Split-Path -Parent $RawDump) ([IO.Path]::GetFileName($RawDump))), $lines, (New-Object Text.UTF8Encoding($false)))
    Write-Host "Reponses brutes tracees dans $RawDump (rejouables sans appel reseau)" -ForegroundColor DarkCyan
}
Write-Host "Résultats écrits dans $Output" -ForegroundColor Green
Write-Host $verdict -ForegroundColor $(if ($verdict.StartsWith('CONFORME')) { 'Green' } else { 'Yellow' })
