<#
.SYNOPSIS
  Génère 150 WAV par synthèse vocale Windows et le jeu `dataset-tts.jsonl`.
.DESCRIPTION
  Solution professionnelle sans enregistrement humain : vérité terrain connue,
  format WAV 16 kHz mono PCM 16 bits, exécution reproductible. La source est
  explicitement `tts` : aucun WAV n'est une voix réelle et aucun WER issu de ce
  jeu ne doit être présenté comme un WER client. Les WAV sont ignorés par Git.
.EXAMPLE
  .\tools\assistant\eval\generer-dataset-tts.ps1
  .\tools\assistant\eval\generer-dataset-tts.ps1 -Force
#>
param(
    [string]$Source = (Join-Path $PSScriptRoot 'dataset-synthetique.jsonl'),
    [string]$Output = (Join-Path $PSScriptRoot 'dataset-tts.jsonl'),
    [string]$AudioRoot = $PSScriptRoot,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

if (-not (Test-Path $Source)) { throw "Jeu source absent : $Source" }
Add-Type -AssemblyName System.Speech -ErrorAction Stop

$audioDir = Join-Path $AudioRoot 'audio-tts'
New-Item -ItemType Directory -Force $audioDir | Out-Null

$rows = @([IO.File]::ReadAllLines($Source, [Text.Encoding]::UTF8) |
    Where-Object { $_.Trim() } |
    ForEach-Object { $_ | ConvertFrom-Json })
if ($rows.Count -ne 150) { throw "Le jeu source contient $($rows.Count) lignes au lieu de 150." }

$voice = $null
try {
    $installed = @((New-Object System.Speech.Synthesis.SpeechSynthesizer).GetInstalledVoices() |
        ForEach-Object { $_.VoiceInfo } | Where-Object { $_.Culture.Name -like 'fr-*' })
    if ($installed.Count -gt 0) { $voice = $installed[0].Name }
}
catch { }

$out = foreach ($row in $rows) {
    $id = 'T' + ([string]$row.id).Substring(1)
    $relative = "audio-tts/$id.wav"
    $wav = Join-Path $AudioRoot $relative
    $reference = [string]$row.reference

    if ($Force -or -not (Test-Path $wav)) {
        $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
        try {
            if ($voice) { $synth.SelectVoice($voice) }
            $format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(
                16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,
                [System.Speech.AudioFormat.AudioChannel]::Mono)
            $synth.SetOutputToWaveFile($wav, $format)
            $synth.Speak($reference)
        }
        finally { $synth.Dispose() }
    }

    [pscustomobject][ordered]@{
        id = $id
        source = 'tts'
        consentement = 'sans objet - voix de synthese Windows'
        audio = $relative
        reference = $reference
        expected = $row.expected
    } | ConvertTo-Json -Compress -Depth 12
}

[IO.File]::WriteAllLines($Output, $out, (New-Object Text.UTF8Encoding($false)))
Write-Host ("Jeu TTS prêt : {0} lignes, WAV dans {1}." -f $out.Count, $audioDir) -ForegroundColor Green
Write-Host ("Voix : {0}" -f $(if ($voice) { $voice } else { 'voix Windows par defaut (aucune voix francaise detectee)' })) -ForegroundColor DarkCyan
Write-Host 'Source tts : aucun enregistrement humain, aucun WER client, aucun resultat simule.' -ForegroundColor Yellow
