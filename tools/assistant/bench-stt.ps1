<#
.SYNOPSIS
  Mesure seul le serveur whisper.cpp local (127.0.0.1:8082) sur des phrases TTS.
.DESCRIPTION
  Génère trois WAV temporaires avec la synthèse vocale Windows, les envoie à /inference,
  enregistre la latence STT et la transcription, puis supprime les WAV. Aucun LLM n'est appelé.
  Les phrases sont synthétiques : cette mesure ne remplace pas un banc de 150 enregistrements réels.
.EXAMPLE
  .\tools\assistant\bench-stt.ps1 -Output .\tools\assistant\eval\latency\stt-base.jsonl
#>
param(
    [string]$SttUrl = 'http://127.0.0.1:8082',
    [string]$Output = (Join-Path $PSScriptRoot 'eval\latency\stt-base.jsonl')
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8; $OutputEncoding = [Text.Encoding]::UTF8 } catch { }

if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) { throw 'curl.exe est requis.' }
try {
    $health = Invoke-RestMethod "$SttUrl/health" -TimeoutSec 5
    if ([string]$health.status -ne 'ok') { throw "STT /health : $($health | ConvertTo-Json -Compress)" }
} catch { throw "whisper.cpp indisponible sur $SttUrl : $($_.Exception.Message)" }

Add-Type -AssemblyName System.Speech -ErrorAction Stop
$references = @(
    "Bonjour, je voudrais faire laver trois chemises et un pantalon. Je m'appelle Astride.",
    'Bonjour, je depose deux chemises a laver, trois pantalons a repasser et une cravate en nettoyage a sec.',
    'Je voudrais faire laver une chemise en express, s il vous plait.'
)

function Get-Tokens { param([string]$Text, [switch]$Normalize)
    if ($Normalize) {
        # Comparaison insensible aux accents, apostrophes et ponctuation : le vrai ecart
        # porte sur les mots, pas sur la typographie produite par Whisper.
        $decomposed = $Text.Normalize([Text.NormalizationForm]::FormD)
        $builder = New-Object Text.StringBuilder
        foreach ($character in $decomposed.ToCharArray()) {
            $category = [Globalization.CharUnicodeInfo]::GetUnicodeCategory($character)
            if ($category -ne [Globalization.UnicodeCategory]::NonSpacingMark) { [void]$builder.Append($character) }
        }
        $Text = $builder.ToString().ToLowerInvariant() -replace "['’]", ' ' -replace '[^\p{L}\p{N}\s]', ' '
    }
    return @($Text -split '\s+' | Where-Object { $_ })
}

function Get-Wer { param([string]$Reference, [string]$Hypothesis, [switch]$Normalize)
    $a = @(Get-Tokens -Text $Reference -Normalize:$Normalize)
    $b = @(Get-Tokens -Text $Hypothesis -Normalize:$Normalize)
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

$lines = New-Object System.Collections.Generic.List[string]
$wavs = New-Object System.Collections.Generic.List[string]
try {
    for ($i = 0; $i -lt $references.Count; $i++) {
        $wav = Join-Path $env:TEMP ("pressing-stt-" + [guid]::NewGuid().ToString('N') + '.wav')
        $wavs.Add($wav)
        $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
        $format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(
            16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,
            [System.Speech.AudioFormat.AudioChannel]::Mono)
        $synth.SetOutputToWaveFile($wav, $format)
        $synth.Speak($references[$i])
        $synth.Dispose()

        $watch = [Diagnostics.Stopwatch]::StartNew()
        $sttRaw = & curl.exe -sS -F "file=@$wav" -F 'language=fr' -F 'response_format=json' "$SttUrl/inference" 2>$null
        $watch.Stop()
        if ($LASTEXITCODE -ne 0) { throw "STT a échoué pour la phrase $($i + 1)." }
        $stt = ($sttRaw -join "`n") | ConvertFrom-Json
        $transcript = [string]$stt.text
        $wer = Get-Wer -Reference $references[$i] -Hypothesis $transcript
        $werNormalise = Get-Wer -Reference $references[$i] -Hypothesis $transcript -Normalize
        $record = [ordered]@{
            index = $i + 1
            source = 'tts-windows-synthetique'
            reference = $references[$i]
            transcript = $transcript
            wavBytes = (Get-Item $wav).Length
            sttSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 3)
            wer = [Math]::Round($wer, 3)
            werNormalise = [Math]::Round($werNormalise, 3)
        }
        $lines.Add(($record | ConvertTo-Json -Depth 6 -Compress))
        Write-Host ("Phrase {0} : {1:F2}s STT, WER brut {2:P1}, normalise {3:P1}" -f ($i + 1), $watch.Elapsed.TotalSeconds, $wer, $werNormalise)
        Write-Host "  transcription : $transcript"
    }
}
finally {
    foreach ($wav in $wavs) { if (Test-Path $wav) { Remove-Item $wav -Force } }
    Write-Host 'WAV temporaires supprimés.' -ForegroundColor DarkCyan
}

$full = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path (Get-Location).Path $Output }
New-Item -ItemType Directory -Force (Split-Path -Parent $full) | Out-Null
$utf8NoBom = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllLines($full, $lines, $utf8NoBom)
Write-Host "Mesures STT écrites : $full" -ForegroundColor Green
