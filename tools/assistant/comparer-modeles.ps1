<#
.SYNOPSIS
  Compare les configurations de modeles locales sans inventer de resultat.
.DESCRIPTION
  Pour chaque couple LLM/STT installe, arrete et redemarre les serveurs,
  execute le banc texte reel via EvaluationHarness, puis le banc STT TTS.
  Avec -Quick, utilise le jeu de 53 lignes au lieu des 150 lignes.
.EXAMPLE
  .\tools\assistant\comparer-modeles.ps1 -Threads 4
.EXAMPLE
  .\tools\assistant\comparer-modeles.ps1 -Quick -Threads 4
.EXAMPLE
  .\tools\assistant\comparer-modeles.ps1 -Only '3B-base' -Threads 4
#>
param(
    [ValidateRange(1, 64)]
    [int]$Threads = 0,
    [switch]$Quick,
    [ValidateSet('All', '1.5B-base', '1.5B-small', '3B-base', '3B-small')]
    [string]$Only = 'All',
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'eval\resultats\comparaisons')
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8; $OutputEncoding = [Text.Encoding]::UTF8 } catch { }

$assistantDir = $PSScriptRoot
$harnessProject = Join-Path $assistantDir 'eval\EvaluationHarness\EvaluationHarness.csproj'
$harnessDll = Join-Path $assistantDir 'eval\EvaluationHarness\bin\Debug\net10.0\EvaluationHarness.dll'
$dataset = if ($Quick) {
    Join-Path $assistantDir 'eval\dataset-fiabilite.jsonl'
}
else {
    Join-Path $assistantDir 'eval\dataset-synthetique.jsonl'
}
$stopScript = Join-Path $assistantDir 'stop-ai.ps1'
$startScript = Join-Path $assistantDir 'start-ai.ps1'
$sttScript = Join-Path $assistantDir 'bench-stt.ps1'

if ($Threads -le 0) { $Threads = [Math]::Max(2, [Math]::Min(8, [Environment]::ProcessorCount)) }
if (-not (Test-Path $dataset)) { throw "Jeu absent : $dataset" }
foreach ($required in @($stopScript, $startScript, $sttScript, $harnessProject)) {
    if (-not (Test-Path $required)) { throw "Script absent : $required" }
}

function Wait-PortFree {
    param([int]$Port)
    for ($i = 0; $i -lt 60; $i++) {
        $client = New-Object Net.Sockets.TcpClient
        try {
            $client.Connect('127.0.0.1', $Port)
            $client.Close()
            Start-Sleep -Milliseconds 250
        }
        catch { return }
        finally { $client.Close() }
    }
    throw "Port $Port toujours occupe apres l'arret."
}

function Assert-ModelStarted {
    param([string]$Key, [string]$File)
    $pidPath = Join-Path (Join-Path $env:LOCALAPPDATA 'Pressing\ai') "$Key.pid"
    if (-not (Test-Path $pidPath)) { throw "PID $Key absent apres demarrage." }
    $raw = (Get-Content $pidPath -Raw).Trim()
    [int]$processId = 0
    if (-not [int]::TryParse($raw, [ref]$processId)) { throw "PID $Key invalide : $raw" }
    $process = Get-CimInstance Win32_Process -Filter "ProcessId=$processId" -ErrorAction SilentlyContinue
    if (-not $process) { throw "Processus $Key (PID $processId) absent." }
    if ($process.CommandLine -notlike "*$File*") {
        throw "$Key demarre avec un autre modele : $($process.CommandLine)"
    }
}

function Wait-LlmReady {
    # Le port est ouvert avant la fin du chargement du GGUF : llama.cpp répond
    # alors 503 "Loading model". Une requête minimale prouve que le modèle est prêt.
    $body = '{"model":"pressing-local","messages":[{"role":"user","content":"Bonjour."}],"max_tokens":1,"temperature":0}'
    for ($i = 0; $i -lt 120; $i++) {
        try {
            $response = Invoke-WebRequest -Uri 'http://127.0.0.1:8081/v1/chat/completions' `
                -Method Post -Body $body -ContentType 'application/json' -TimeoutSec 30 -UseBasicParsing
            if ($response.StatusCode -eq 200) { return }
            throw "Statut inattendu : $($response.StatusCode)"
        }
        catch {
            $response = $_.Exception.Response
            if ($null -eq $response -or [int]$response.StatusCode -ne 503) {
                throw "LLM joignable mais pas prêt : $($_.Exception.Message)"
            }
        }
        Start-Sleep -Milliseconds 500
    }
    throw 'LLM toujours en chargement après 60 secondes.'
}

function Start-Config {
    param([string]$Llm, [string]$Stt)
    & $stopScript
    Wait-PortFree 8081
    Wait-PortFree 8082
    & $startScript -LlmModel $Llm -WhisperModel $Stt -Threads $Threads
    $llmFile = if ($Llm -eq '3B') { 'qwen2.5-3b-instruct-q4_k_m.gguf' } else { 'qwen2.5-1.5b-instruct-q4_k_m.gguf' }
    $sttFile = if ($Stt -eq 'small') { 'ggml-small.bin' } else { 'ggml-base.bin' }
    Assert-ModelStarted -Key 'llama' -File $llmFile
    Assert-ModelStarted -Key 'whisper' -File $sttFile
    Wait-LlmReady
}

Write-Host 'Build du harnais...' -ForegroundColor Cyan
dotnet build $harnessProject -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Build du harnais echoue.' }

New-Item -ItemType Directory -Force $OutputRoot | Out-Null
$suffix = if ($Quick) { 'rapide' } else { '150' }

$configs = @(
    @{ Base = '1.5B-base'; Name = "1.5B-base-$suffix"; Llm = '1.5B'; Stt = 'base' },
    @{ Base = '1.5B-small'; Name = "1.5B-small-$suffix"; Llm = '1.5B'; Stt = 'small' },
    @{ Base = '3B-base'; Name = "3B-base-$suffix"; Llm = '3B'; Stt = 'base' },
    @{ Base = '3B-small'; Name = "3B-small-$suffix"; Llm = '3B'; Stt = 'small' }
)

try {
    foreach ($config in ($configs | Where-Object { $Only -eq 'All' -or $_.Base -eq $Only })) {
        Write-Host "`n==> $($config.Name) (threads=$Threads)" -ForegroundColor Cyan
        Start-Config -Llm $config.Llm -Stt $config.Stt

        $output = Join-Path $OutputRoot $config.Name
        dotnet $harnessDll $dataset $output $config.Name
        if ($LASTEXITCODE -ne 0) { throw "Banc texte echoue pour $($config.Name)." }

        $sttOutput = Join-Path $assistantDir "eval\latency\stt-$($config.Stt)-$($config.Llm)-$Threads-$suffix.jsonl"
        & $sttScript -Output $sttOutput
        if ($LASTEXITCODE -ne 0) { throw "Banc STT echoue pour $($config.Name)." }
    }
}
finally {
    Write-Host "`nRestauration de la configuration par defaut 1.5B/base..." -ForegroundColor Yellow
    Start-Config -Llm '1.5B' -Stt 'base'
}

Write-Host "Comparaisons terminees : $OutputRoot" -ForegroundColor Green
