<#
.SYNOPSIS
  Installe les moteurs IA locaux et les modèles vérifiés dans %LOCALAPPDATA%\Pressing\ai.
.DESCRIPTION
  Télécharge des artefacts épinglés, vérifie leur SHA-256 officiel avant toute installation,
  puis range les binaires et modèles. Aucun modèle n'est commité. Si une empreinte ne
  correspond pas, l'installation s'arrête immédiatement.
.EXAMPLE
  .\tools\assistant\install.ps1
  .\tools\assistant\install.ps1 -LlmModel 0.5B -WhisperModel base
  .\tools\assistant\install.ps1 -LlmModel 1.5B -WhisperModel small
  .\tools\assistant\install.ps1 -LlmModel 3B -WhisperModel small -Force
#>
param(
    [ValidateSet('0.5B', '1.5B', '3B')]
    [string]$LlmModel = '1.5B',
    [ValidateSet('base', 'small')]
    [string]$WhisperModel = 'base',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$assistantDir = $PSScriptRoot
$aiRoot = Join-Path $env:LOCALAPPDATA 'Pressing\ai'
$dirs = @{
    Llama   = Join-Path $aiRoot 'llama'
    Whisper = Join-Path $aiRoot 'whisper'
    Models  = Join-Path $aiRoot 'models'
    Logs    = Join-Path $aiRoot 'logs'
    Temp    = Join-Path $aiRoot 'tmp'
}
foreach ($dir in $dirs.Values) { New-Item -ItemType Directory -Force $dir | Out-Null }

$llamaTag = 'b12810'
$llamaAsset = 'llama-b12810-bin-win-cpu-x64.zip'
$llamaSha = '845e25154ae4051659cf7363908a663cf15f99c57f8d5157d0f38f468acf660d'
$llamaSize = 25324700
$llamaUrl = "https://github.com/ggml-org/llama.cpp/releases/download/$llamaTag/$llamaAsset"

$whisperTag = 'b5907'
$whisperAsset = 'whisper-bin-x64.zip'
$whisperSha = 'ecca07b277d943e9b9f4712856a144b857271a6b333104e511e1988e80db582b'
$whisperSize = 9168428
$whisperUrl = "https://github.com/ggml-org/whisper.cpp/releases/download/$whisperTag/$whisperAsset"

$models = @(
    @{
        Name = 'STT base'; File = 'ggml-base.bin'; Sha = '60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe'; Size = 147951465
        Url = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin'; Selected = ($WhisperModel -eq 'base')
    },
    @{
        Name = 'STT small'; File = 'ggml-small.bin'; Sha = '1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b'; Size = 487601967
        Url = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin'; Selected = ($WhisperModel -eq 'small')
    },
    @{
        Name = 'LLM 0.5B'; File = 'qwen2.5-0.5b-instruct-q4_k_m.gguf'; Sha = '74a4da8c9fdbcd15bd1f6d01d621410d31c6fc00986f5eb687824e7b93d7a9db'; Size = 491400032
        Url = 'https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/qwen2.5-0.5b-instruct-q4_k_m.gguf'; Selected = ($LlmModel -eq '0.5B')
    },
    @{
        Name = 'LLM 1.5B'; File = 'qwen2.5-1.5b-instruct-q4_k_m.gguf'; Sha = '6a1a2eb6d15622bf3c96857206351ba97e1af16c30d7a74ee38970e434e9407e'; Size = 1117320736
        Url = 'https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf'; Selected = ($LlmModel -eq '1.5B')
    },
    @{
        Name = 'LLM 3B'; File = 'qwen2.5-3b-instruct-q4_k_m.gguf'; Sha = '626b4a6678b86442240e33df819e00132d3ba7dddfe1cdc4fbb18e0a9615c62d'; Size = 2104932768
        Url = 'https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/qwen2.5-3b-instruct-q4_k_m.gguf'; Selected = ($LlmModel -eq '3B')
    }
)

function Assert-ValidatedFile {
    param(
        [string]$Label,
        [string]$Url,
        [string]$ExpectedSha,
        [long]$ExpectedSize,
        [string]$Destination
    )

    if ((Test-Path $Destination) -and -not $Force) {
        $current = (Get-FileHash -Path $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($current -eq $ExpectedSha -and (Get-Item $Destination).Length -eq $ExpectedSize) {
            Write-Host "  $Label : déjà présent et vérifié." -ForegroundColor Green
            return
        }
        Write-Warning "$Label : empreinte ou taille différente de celle attendue ; nouveau téléchargement."
    }

    $temporary = "$Destination.download"
    try {
        Write-Host "  $Label : téléchargement..." -ForegroundColor Cyan
        if (Get-Command curl.exe -ErrorAction SilentlyContinue) {
            & curl.exe -fL --retry 5 --retry-delay 2 -C - -o $temporary $Url
            if ($LASTEXITCODE -ne 0) {
                throw "Téléchargement interrompu pour $Label (curl exit $LASTEXITCODE)."
            }
        }
        else {
            Invoke-WebRequest -Uri $Url -OutFile $temporary -UseBasicParsing
        }
        $actual = (Get-FileHash -Path $temporary -Algorithm SHA256).Hash.ToLowerInvariant()
        $length = (Get-Item $temporary).Length
        if ($actual -ne $ExpectedSha) {
            throw "SHA-256 inattendu pour $Label : $actual (attendu $ExpectedSha). Installation interrompue."
        }
        if ($length -ne $ExpectedSize) {
            throw "Taille inattendue pour $Label : $length octets (attendu $ExpectedSize). Installation interrompue."
        }
        Move-Item -Path $temporary -Destination $Destination -Force
        Write-Host "  $Label : SHA-256 vérifié." -ForegroundColor Green
    }
    finally {
        if (Test-Path $temporary) { Remove-Item $temporary -Force }
    }
}

function Expand-FlatArchive {
    param([string]$Archive, [string]$Destination)

    $extract = Join-Path $dirs.Temp ("extract-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $extract | Out-Null
    try {
        Expand-Archive -Path $Archive -DestinationPath $extract -Force
        Get-ChildItem -Path $extract -Recurse -File | ForEach-Object {
            Copy-Item -Path $_.FullName -Destination (Join-Path $Destination $_.Name) -Force
        }
    }
    finally {
        if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
    }
}

Write-Host "==> Installation IA locale dans $aiRoot" -ForegroundColor Cyan

$llamaZip = Join-Path $dirs.Temp $llamaAsset
$whisperZip = Join-Path $dirs.Temp $whisperAsset

Assert-ValidatedFile -Label 'llama.cpp Windows CPU' -Url $llamaUrl -ExpectedSha $llamaSha -ExpectedSize $llamaSize -Destination $llamaZip
Assert-ValidatedFile -Label 'whisper.cpp Windows CPU' -Url $whisperUrl -ExpectedSha $whisperSha -ExpectedSize $whisperSize -Destination $whisperZip

foreach ($model in $models | Where-Object { $_.Selected }) {
    Assert-ValidatedFile -Label $model.Name -Url $model.Url -ExpectedSha $model.Sha -ExpectedSize $model.Size -Destination (Join-Path $dirs.Models $model.File)
}

$llamaExe = Join-Path $dirs.Llama 'llama-server.exe'
$whisperExe = Join-Path $dirs.Whisper 'whisper-server.exe'
if ($Force -or -not (Test-Path $llamaExe)) {
    Write-Host "  Extraction llama.cpp..." -ForegroundColor Cyan
    Expand-FlatArchive -Archive $llamaZip -Destination $dirs.Llama
}
if ($Force -or -not (Test-Path $whisperExe)) {
    Write-Host "  Extraction whisper.cpp..." -ForegroundColor Cyan
    Expand-FlatArchive -Archive $whisperZip -Destination $dirs.Whisper
}

if (-not (Test-Path $llamaExe)) { throw "llama-server.exe est absent après extraction." }
if (-not (Test-Path $whisperExe)) { throw "whisper-server.exe est absent après extraction." }

foreach ($name in @('prompt-systeme.txt', 'grammar.gbnf', 'intent-schema.json', 'vocab-default.txt')) {
    Copy-Item -Path (Join-Path $assistantDir $name) -Destination (Join-Path $aiRoot $name) -Force
}
if (-not (Test-Path (Join-Path $aiRoot 'stt-vocab.txt'))) {
    Copy-Item -Path (Join-Path $assistantDir 'vocab-default.txt') -Destination (Join-Path $aiRoot 'stt-vocab.txt') -Force
}

$versions = @{
    VerifiedOn = '2026-11-27'
    Llama = @{ Tag = $llamaTag; Asset = $llamaAsset; Sha256 = $llamaSha; Size = $llamaSize; Url = $llamaUrl }
    Whisper = @{ Tag = $whisperTag; Asset = $whisperAsset; Sha256 = $whisperSha; Size = $whisperSize; Url = $whisperUrl }
    Models = @($models | ForEach-Object { @{ Name = $_.Name; File = $_.File; Sha256 = $_.Sha; Size = $_.Size; Url = $_.Url } })
}
$versions | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $aiRoot 'versions.json') -Encoding UTF8

Write-Host ""
Write-Host "Installation terminée." -ForegroundColor Green
Write-Host "  LLM      : $llamaExe"
Write-Host "  STT      : $whisperExe"
Write-Host "  Modèles  : $($dirs.Models)"
Write-Host "  Suivant  : .\tools\assistant\update-vocab.ps1 puis .\tools\assistant\start-ai.ps1"
