<#
.SYNOPSIS
  Mesure reelement les phases de latence du LLM local sur un jeu fixe de six phrases.
.DESCRIPTION
  Utilise uniquement 127.0.0.1:8081. Ecrit un JSONL avec, pour chaque appel :
  temps HTTP total, temps prompt et generation rapportes par llama.cpp, tokens,
  validite JSON et contenu brut. Aucune valeur n'est simulee.
.EXAMPLE
  .\tools\assistant\bench-latency.ps1 -Output .\tools\assistant\eval\latency\avant.jsonl -Count 6
#>
param(
    [string]$PromptFile = (Join-Path $PSScriptRoot 'prompt-systeme.txt'),
    [string]$CatalogFile = (Join-Path $env:LOCALAPPDATA 'Pressing\ai\catalogue.json'),
    [string]$Output = (Join-Path $PSScriptRoot 'eval\latency\mesure.jsonl'),
    [ValidateRange(1, 60)]
    [int]$Count = 6,
    [string]$Label = 'mesure'
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8; $OutputEncoding = [Text.Encoding]::UTF8 } catch { }
$llmUrl = 'http://127.0.0.1:8081'
$schemaPath = Join-Path $PSScriptRoot 'intent-schema.json'
$outDir = Split-Path -Parent $Output
New-Item -ItemType Directory -Force $outDir | Out-Null

foreach ($path in @($PromptFile, $CatalogFile, $schemaPath)) {
    if (-not (Test-Path $path)) { throw "Fichier absent : $path" }
}
$template = [IO.File]::ReadAllText($PromptFile, [Text.Encoding]::UTF8)
$catalog = [IO.File]::ReadAllText($CatalogFile, [Text.Encoding]::UTF8) | ConvertFrom-Json
$schema = [IO.File]::ReadAllText($schemaPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
$e = [char]0x00E9
$cats = @($catalog.categories | ForEach-Object { [string]$_.label })
$svcs = @($catalog.services | ForEach-Object { [string]$_.label })
$syn = @($catalog.synonyms | ForEach-Object { [string]$_ })
$block = "Cat${e}gories : $($cats -join ', ')`nServices : $($svcs -join ', ')`nSynonymes : $($syn -join ', ')"
$system = $template.Replace('{{DATE_REFERENCE}}', [string]$catalog.referenceDate).Replace('{{CATALOGUE}}', $block)

$phrases = @(
    "Bonjour, je voudrais faire laver trois chemises et un pantalon. Je m'appelle Astride.",
    'Bonjour, je depose deux chemises a laver, trois pantalons a repasser et une cravate en nettoyage a sec.',
    'Je voudrais faire laver une chemise en express, s il vous plait.',
    'Je depose une chemise a laver, s il vous plait.',
    'Bonjour, je voudrais faire laver trois chemises.',
    'Euh, je ne sais pas trop, peut-etre quelque chose a faire avec ce pantalon.'
)

$lines = New-Object System.Collections.Generic.List[string]
for ($i = 1; $i -le $Count; $i++) {
    $phrase = $phrases[($i - 1) % $phrases.Count]
    $body = [ordered]@{
        model = 'pressing-local'
        messages = @(@{ role = 'system'; content = $system }, @{ role = 'user'; content = $phrase })
        temperature = 0.1
        max_tokens = 512
        response_format = @{
            type = 'json_schema'
            json_schema = @{ name = 'pressing_intent'; strict = $true; schema = $schema }
        }
    }
    $bodyFile = Join-Path $env:TEMP ("pressing-latency-" + [guid]::NewGuid().ToString('N') + '.json')
    try {
        ($body | ConvertTo-Json -Depth 20 -Compress) | Set-Content $bodyFile -Encoding UTF8 -NoNewline
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $raw = & curl.exe -sS -H 'Content-Type: application/json' --data-binary "@$bodyFile" "$llmUrl/v1/chat/completions" 2>$null
        $watch.Stop()
        if ($LASTEXITCODE -ne 0) { throw "Appel LLM $i refuse." }
    } finally { if (Test-Path $bodyFile) { Remove-Item $bodyFile -Force } }
    $envelope = ($raw -join "`n") | ConvertFrom-Json
    $content = [string]$envelope.choices[0].message.content
    $jsonValid = $false
    $intent = ''
    try {
        $parsed = $content | ConvertFrom-Json
        $jsonValid = $null -ne $parsed.PSObject.Properties['intent']
        $intent = [string]$parsed.intent
    } catch { }
    $timings = $envelope.timings
    $usage = $envelope.usage
    $record = [ordered]@{
        label = $Label
        index = $i
        phrase = $phrase
        httpSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 3)
        serverPromptMs = [Math]::Round([double]$timings.prompt_ms, 1)
        serverPredictedMs = [Math]::Round([double]$timings.predicted_ms, 1)
        serverTotalMs = [Math]::Round([double]$timings.total_ms, 1)
        promptTokens = [int]$usage.prompt_tokens
        completionTokens = [int]$usage.completion_tokens
        cachedTokens = [int]$usage.prompt_tokens_details.cached_tokens
        jsonValid = $jsonValid
        intent = $intent
        content = $content
    }
    $lines.Add(($record | ConvertTo-Json -Depth 8 -Compress))
    Write-Host ("{0} #{1}: {2:F2}s HTTP, {3:F2}s generation, {4} tokens" -f $Label, $i, $watch.Elapsed.TotalSeconds, ([double]$timings.predicted_ms / 1000), [int]$usage.completion_tokens)
}
$fullOutput = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path (Get-Location).Path $Output }
$utf8NoBom = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllLines($fullOutput, $lines, $utf8NoBom)
Write-Host "Mesures ecrites : $fullOutput" -ForegroundColor Green
