<#
.SYNOPSIS
  Démarre llama-server (8081) et whisper-server (8082) sur 127.0.0.1 uniquement.
.DESCRIPTION
  Avec -Install, installe d'abord les moteurs et modèles vérifiés puis les démarre :
  une seule commande suffit sur un poste neuf. Sans -Install, les artefacts doivent
  déjà être présents dans %LOCALAPPDATA%\Pressing\ai.
.EXAMPLE
  .\tools\assistant\start-ai.ps1
  .\tools\assistant\start-ai.ps1 -Install
  .\tools\assistant\start-ai.ps1 -Install -LlmModel 3B -WhisperModel small -Threads 4
  .\tools\assistant\start-ai.ps1 -LlmModel 0.5B -WhisperModel base
#>
param(
    [ValidateSet('0.5B', '1.5B', '3B')]
    [string]$LlmModel = '1.5B',
    [ValidateSet('base', 'small')]
    [string]$WhisperModel = 'base',
    [ValidateRange(1, 64)]
    [int]$Threads = 0,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'

$assistantDir = $PSScriptRoot
$repoRoot = Split-Path -Parent (Split-Path -Parent $assistantDir)
$aiRoot = Join-Path $env:LOCALAPPDATA 'Pressing\ai'
$logs = Join-Path $aiRoot 'logs'
New-Item -ItemType Directory -Force $logs | Out-Null

if ($Install) {
    $installScript = Join-Path $assistantDir 'install.ps1'
    Write-Host '==> Installation des moteurs et des modèles vérifiés' -ForegroundColor Cyan
    & $installScript -LlmModel $LlmModel -WhisperModel $WhisperModel
}

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

if ((Get-PressingSetting 'ASSISTANT_ENABLED' 'true').Trim().ToLowerInvariant() -eq 'false') {
    Write-Host "Assistant désactivé (ASSISTANT_ENABLED=false)." -ForegroundColor Yellow
    return
}

if ($Threads -le 0) {
    $Threads = [Math]::Max(2, [Math]::Min(8, [Environment]::ProcessorCount))
}

$llmFile = if ($LlmModel -eq '3B') { 'qwen2.5-3b-instruct-q4_k_m.gguf' } elseif ($LlmModel -eq '1.5B') { 'qwen2.5-1.5b-instruct-q4_k_m.gguf' } else { 'qwen2.5-0.5b-instruct-q4_k_m.gguf' }
$sttFile = if ($WhisperModel -eq 'small') { 'ggml-small.bin' } else { 'ggml-base.bin' }
$llmPath = Join-Path (Join-Path $aiRoot 'models') $llmFile
$sttPath = Join-Path (Join-Path $aiRoot 'models') $sttFile
$llamaExe = Join-Path (Join-Path $aiRoot 'llama') 'llama-server.exe'
$whisperExe = Join-Path (Join-Path $aiRoot 'whisper') 'whisper-server.exe'
$grammar = Join-Path $aiRoot 'grammar.gbnf'
$alias = Get-PressingSetting 'ASSISTANT_LLM_MODEL_ALIAS' 'pressing-local'

foreach ($required in @($llamaExe, $whisperExe, $llmPath, $sttPath, $grammar)) {
    if (-not (Test-Path $required)) {
        throw "Fichier absent : $required. Utilisez .\tools\assistant\start-ai.ps1 -Install puis vérifiez les journaux."
    }
}

function Test-LoopbackPort {
    param([int]$Port)
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $client.Connect('127.0.0.1', $Port)
        return $true
    }
    catch { return $false }
    finally { $client.Close() }
}

function Wait-LoopbackPort {
    param([int]$Port, [string]$Name)
    for ($i = 0; $i -lt 80; $i++) {
        if (Test-LoopbackPort $Port) { return }
        Start-Sleep -Milliseconds 500
    }
    throw "$Name ne répond pas sur 127.0.0.1:$Port. Consultez les journaux dans $logs."
}

function Assert-LoopbackOnly {
    param([int]$Port, [string]$Name)
    try {
        $outside = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
            Where-Object { $_.LocalAddress -ne '127.0.0.1' -and $_.LocalAddress -ne '::1' }
        if ($outside) {
            throw "$Name écoute hors de la boucle locale : $($outside.LocalAddress -join ', ')."
        }
    }
    catch [System.Management.Automation.CommandNotFoundException] {
        # Get-NetTCPConnection absent : les arguments de lancement imposent déjà 127.0.0.1.
    }
}

function Start-AiProcess {
    param([string]$Name, [string]$Executable, [string[]]$Arguments, [string]$Key)

    $out = Join-Path $logs "$Key.log"
    $err = Join-Path $logs "$Key.err.log"
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments `
        -WorkingDirectory (Split-Path -Parent $Executable) -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $out -RedirectStandardError $err
    Set-Content -Path (Join-Path $aiRoot "$Key.pid") -Value $process.Id -Encoding ASCII
    Write-Host ("  {0,-8} PID {1,-6} 127.0.0.1:{2}" -f $Key, $process.Id, $(if ($Key -eq 'llama') { 8081 } else { 8082 }))
    return $process
}

if (Test-LoopbackPort 8081) {
    Write-Host "Port 8081 déjà occupé : llama-server considéré comme démarré." -ForegroundColor Yellow
}
else {
    $llamaArgs = @(
        '-m', "`"$llmPath`"",
        '--host', '127.0.0.1',
        '--port', '8081',
        '-c', '4096',
        '-n', '512',
        '-np', '1',
        '-t', "$Threads",
        '--alias', "$alias",
        '--grammar-file', "`"$grammar`"",
        '--no-webui'
    )
    Start-AiProcess -Name 'LLM' -Executable $llamaExe -Arguments $llamaArgs -Key 'llama' | Out-Null
    Wait-LoopbackPort 8081 'llama-server'
    Assert-LoopbackOnly 8081 'llama-server'
}

$vocabPath = Join-Path $aiRoot 'stt-vocab.txt'
if (-not (Test-Path $vocabPath)) { $vocabPath = Join-Path $assistantDir 'vocab-default.txt' }
$vocab = ([IO.File]::ReadAllText($vocabPath, [Text.Encoding]::UTF8)) -replace '\s+', ' '

if (Test-LoopbackPort 8082) {
    Write-Host "Port 8082 déjà occupé : whisper-server considéré comme démarré." -ForegroundColor Yellow
}
else {
    $sttArgs = @(
        '-m', "`"$sttPath`"",
        '--host', '127.0.0.1',
        '--port', '8082',
        '-l', 'fr',
        '-p', '1',
        '-t', "$Threads",
        '--inference-path', '/inference',
        '--prompt', "`"$vocab`""
    )
    Start-AiProcess -Name 'STT' -Executable $whisperExe -Arguments $sttArgs -Key 'whisper' | Out-Null
    Wait-LoopbackPort 8082 'whisper-server'
    Assert-LoopbackOnly 8082 'whisper-server'
}

Write-Host ""
Write-Host "IA locale prête :" -ForegroundColor Green
Write-Host "  LLM  : http://127.0.0.1:8081/v1/chat/completions"
Write-Host "  STT  : http://127.0.0.1:8082/inference"
Write-Host "  Arrêt : .\tools\assistant\stop-ai.ps1"
