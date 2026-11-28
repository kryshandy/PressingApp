<#
.SYNOPSIS
  Copie exacte des ressources IA du depot vers %LOCALAPPDATA%\Pressing\ai\.
.DESCRIPTION
  Le serveur llama.cpp lit grammar.gbnf au demarrage ; les scripts de banc lisent
  le prompt et le schema locaux. Cette copie octet pour octet evite les mojibakes
  de PowerShell 5.1 et garantit que les mesures utilisent les memes fichiers que
  le code C# (ressources embarquees). Aucun modele n'est telecharge ici.
.EXAMPLE
  .\tools\assistant\sync-ressources.ps1
#>
param(
    [string]$OutputDir = (Join-Path $env:LOCALAPPDATA 'Pressing\ai')
)

$ErrorActionPreference = 'Stop'
$sourceDir = $PSScriptRoot
New-Item -ItemType Directory -Force $OutputDir | Out-Null

foreach ($name in @('prompt-systeme.txt', 'grammar.gbnf', 'intent-schema.json', 'vocab-default.txt')) {
    $source = Join-Path $sourceDir $name
    $destination = Join-Path $OutputDir $name
    if (-not (Test-Path $source)) { throw "Ressource source absente : $source" }
    [IO.File]::Copy($source, $destination, $true)
    $left = (Get-FileHash -Path $source -Algorithm SHA256).Hash
    $right = (Get-FileHash -Path $destination -Algorithm SHA256).Hash
    if ($left -ne $right) { throw "Copie non conforme pour $name" }
    Write-Host ("  {0,-22} SHA256 {1}" -f $name, $left.Substring(0, 16))
}
Write-Host 'Ressources locales synchronisees avec le depot.' -ForegroundColor Green
