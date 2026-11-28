<#
.SYNOPSIS
  Test live de N requêtes envoyées en même temps aux deux moteurs locaux.
.DESCRIPTION
  Envoie $Count requêtes LLM et $Count requêtes STT en parallèle vers 127.0.0.1.
  Vérifie que chaque réponse revient et contient un résultat exploitable.
  Écrit un rapport avec les latences réellement mesurées. Aucune donnée simulée.
.EXAMPLE
  .\tools\assistant\bench-concurrence.ps1 -Count 10
#>
param(
    [ValidateRange(1, 50)]
    [int]$Count = 10,
    [string]$LlmUrl = 'http://127.0.0.1:8081',
    [string]$SttUrl = 'http://127.0.0.1:8082',
    [string]$AudioRoot = (Join-Path $PSScriptRoot 'eval'),
    [string]$Output = (Join-Path $PSScriptRoot 'eval\resultats\concurrence-live.md')
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
$assistantDir = $PSScriptRoot

function Test-LoopbackPort { param([int]$Port)
    $client = New-Object Net.Sockets.TcpClient
    try { $client.Connect('127.0.0.1', $Port); return $true } catch { return $false } finally { $client.Close() }
}
if (-not (Test-LoopbackPort 8081)) { throw 'llama-server absent sur 127.0.0.1:8081. Lancez start-ai.ps1.' }
if (-not (Test-LoopbackPort 8082)) { throw 'whisper-server absent sur 127.0.0.1:8082. Lancez start-ai.ps1.' }

$schema = [IO.File]::ReadAllText((Join-Path $assistantDir 'intent-schema.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json
$llmBody = @{
    model = 'pressing-local'
    messages = @(
        @{ role = 'user'; content = 'Bonjour, je voudrais faire laver une chemise.' }
    )
    temperature = 0.1
    max_tokens = 512
    response_format = @{ type = 'json_schema'; json_schema = @{ name = 'pressing_intent'; strict = $true; schema = $schema } }
} | ConvertTo-Json -Depth 20 -Compress

$jobs = New-Object System.Collections.Generic.List[object]

for ($i = 1; $i -le $Count; $i++) {
    $jobs.Add((Start-Job -Name "llm-$i" -ArgumentList $i, $LlmUrl, $llmBody -ScriptBlock {
        param([int]$Index, [string]$Url, [string]$BodyJson)
        $file = Join-Path $env:TEMP ("pressing-conc-live-" + $Index + "-" + [guid]::NewGuid().ToString('N') + '.json')
        try {
            [IO.File]::WriteAllText($file, $BodyJson, (New-Object Text.UTF8Encoding($false)))
            $watch = [Diagnostics.Stopwatch]::StartNew()
            $raw = & curl.exe -sS --max-time 180 -H 'Content-Type: application/json' --data-binary "@$file" "$Url/v1/chat/completions" 2>&1
            $code = $LASTEXITCODE
            $watch.Stop()
            $detail = ''
            $ok = $false
            if ($code -eq 0) {
                try {
                    $json = ($raw -join "`n") | ConvertFrom-Json
                    $content = [string]$json.choices[0].message.content
                    $payload = $content | ConvertFrom-Json
                    $ok = $null -ne $payload.PSObject.Properties['intent']
                    $detail = "intent=$($payload.intent)"
                } catch { $detail = "JSON invalide : $($_.Exception.Message)" }
            } else { $detail = ($raw | Out-String).Trim() }
            [pscustomobject]@{ Index = $Index; Kind = 'llm'; Ok = $ok; Seconds = $watch.Elapsed.TotalSeconds; Detail = $detail }
        } finally { if (Test-Path $file) { Remove-Item $file -Force } }
    }))

    $jobs.Add((Start-Job -Name "stt-$i" -ArgumentList $i, $SttUrl, $AudioRoot -ScriptBlock {
        param([int]$Index, [string]$Url, [string]$Root)
        $wav = Join-Path $Root ("audio-tts/T{0:D3}.wav" -f $Index)
        if (-not (Test-Path $wav)) { return [pscustomobject]@{ Index = $Index; Kind = 'stt'; Ok = $false; Seconds = 0.0; Detail = "WAV absent : $wav" } }
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $raw = & curl.exe -sS --max-time 180 -F "file=@$wav" -F 'language=fr' -F 'response_format=json' "$Url/inference" 2>&1
        $code = $LASTEXITCODE
        $watch.Stop()
        $detail = ''
        $ok = $false
        if ($code -eq 0) {
            try {
                $json = ($raw -join "`n") | ConvertFrom-Json
                $text = [string]$json.text
                $ok = -not [string]::IsNullOrWhiteSpace($text)
                $detail = "transcription=$text"
            } catch { $detail = "JSON invalide : $($_.Exception.Message)" }
        } else { $detail = ($raw | Out-String).Trim() }
        [pscustomobject]@{ Index = $Index; Kind = 'stt'; Ok = $ok; Seconds = $watch.Elapsed.TotalSeconds; Detail = $detail }
    }))
}

Write-Host "Attente de $($jobs.Count) requetes paralleleles..." -ForegroundColor DarkCyan
Wait-Job -Job $jobs | Out-Null
$results = @($jobs | ForEach-Object { Receive-Job $_; Remove-Job $_ })

$llm = @($results | Where-Object { $_.Kind -eq 'llm' } | Sort-Object Index)
$stt = @($results | Where-Object { $_.Kind -eq 'stt' } | Sort-Object Index)
$all = @($results)
$okCount = @($all | Where-Object { $_.Ok }).Count
$failures = @($all | Where-Object { -not $_.Ok })

function Get-Stats { param([object[]]$Items)
    if ($Items.Count -eq 0) { return 'n/a' }
    $values = @($Items | ForEach-Object { [double]$_.Seconds } | Sort-Object)
    $p50 = $values[[Math]::Floor(0.5 * ($values.Count - 1))]
    $p95 = $values[[Math]::Min($values.Count - 1, [Math]::Ceiling(0.95 * ($values.Count - 1)))]
    return ('min {0:F2} s · p50 {1:F2} s · p95 {2:F2} s · max {3:F2} s' -f $values[0], $p50, $p95, $values[-1])
}

$startScript = [IO.File]::ReadAllText((Join-Path $assistantDir 'start-ai.ps1'), [Text.Encoding]::UTF8)
$np = if ($startScript -match "'-np',\s*'1'") { '-np 1' } else { '-np non verifie' }
$pp = if ($startScript -match "'-p',\s*'1'") { '-p 1' } else { '-p non verifie' }

$lines = @()
$lines += '# Test de concurrence live — moteurs IA locaux'
$lines += ''
$lines += "## Mesure du $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
$lines += ''
$lines += "- Requêtes envoyées en parallèle : $($all.Count) ($Count LLM + $Count STT)"
$lines += "- Serveurs : 127.0.0.1:8081 et 127.0.0.1:8082 uniquement"
$lines += "- Configuration lue dans start-ai.ps1 : $np, $pp"
$lines += "- Aucune donnée envoyée hors de la machine."
$lines += ''
$lines += '| Famille | Réussites | Latences |'
$lines += '|---|---:|---|'
$lines += "| LLM | $(@($llm | Where-Object { $_.Ok }).Count)/$Count | $(Get-Stats $llm) |"
$lines += "| STT | $(@($stt | Where-Object { $_.Ok }).Count)/$Count | $(Get-Stats $stt) |"
$lines += "| Total | $okCount/$($all.Count) | $(Get-Stats $all) |"
$lines += ''
if ($failures.Count -eq 0) {
    $lines += 'Verdict : **file d’attente vérifiée** — toutes les requêtes simultanées sont revenues avec un résultat exploitable.'
} else {
    $lines += 'Verdict : **' + $failures.Count + ' echec(s)** - la file d attente n a pas absorbe toute la charge.'
    $lines += ''
    foreach ($f in $failures) { $lines += "- $($f.Kind) #$($f.Index) : $($f.Detail)" }
}
$lines += ''
$lines += 'Ce test vérifie la tenue sous charge parallèle. Il ne mesure pas la latence d’un usage séquentiel et ne constitue pas une validation client.'

[IO.File]::WriteAllLines((Join-Path (Split-Path -Parent $Output) ([IO.Path]::GetFileName($Output))), $lines, (New-Object Text.UTF8Encoding($true)))
Write-Host "Rapport ecrit dans $Output" -ForegroundColor Green
Write-Host "Reussites : $okCount / $($all.Count)" -ForegroundColor $(if ($okCount -eq $all.Count) { 'Green' } else { 'Yellow' })
