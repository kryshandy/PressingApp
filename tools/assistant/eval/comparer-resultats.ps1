<#
.SYNOPSIS
  Compare deux resumes du banc d'evaluation (avant/apres) et ecrit un tableau Markdown.
.DESCRIPTION
  Les entrees sont les fichiers *.resume.json produits par EvaluationHarness.
  Aucune valeur n'est calculee a partir d'autres donnees que ces resumes reels.
.EXAMPLE
  .\tools\assistant\eval\comparer-resultats.ps1 `
    -Avant .\tools\assistant\eval\resultats\avant-1.5b.resume.json `
    -Apres .\tools\assistant\eval\resultats\apres-0.5b.resume.json `
    -Output .\tools\assistant\eval\resultats\comparaison.md
#>
param(
    [Parameter(Mandatory = $true)][string]$Avant,
    [Parameter(Mandatory = $true)][string]$Apres,
    [string]$Output = (Join-Path $PSScriptRoot 'resultats\comparaison.md')
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8; $OutputEncoding = [Text.Encoding]::UTF8 } catch { }

function Read-Resume { param([string]$Path)
    $full = if ([IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path (Get-Location).Path $Path }
    if (-not (Test-Path $full)) { throw "Resume absent : $full" }
    return [IO.File]::ReadAllText($full, [Text.Encoding]::UTF8) | ConvertFrom-Json
}

function Get-Ratio { param($Section, [string]$Name)
    $value = $Section.$Name
    if ($null -eq $value) { return '' }
    return ('{0}/{1} ({2:F1} %)' -f [int]$value.ok, [int]$value.total, [double]$value.pourcent)
}

function Get-Count { param($Section, [string]$Name)
    $value = $Section.$Name
    if ($null -eq $value) { return '' }
    return [string][int]$value
}

$before = Read-Resume -Path $Avant
$after = Read-Resume -Path $Apres

$rows = New-Object System.Collections.Generic.List[string]
$em = [char]0x2014
$rows.Add('# Comparaison avant / apres ' + $em + ' banc texte 150 scenarios')
$rows.Add('')
$rows.Add(('* Genere le : {0} *' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
$rows.Add('')
$rows.Add('| Mesure | Avant (`' + [string]$before.label + '`) | Apres (`' + [string]$after.label + '`) | Seuil |')
$rows.Add('|---|---:|---:|---:|')

$criteria = @(
    @{ Key = 'intentionsExactes'; Label = 'Intentions exactes'; Threshold = 90; Kind = 'ratio' },
    @{ Key = 'articlesExactes'; Label = 'Articles exacts'; Threshold = 85; Kind = 'ratio' },
    @{ Key = 'champsExact'; Label = 'Champs exacts'; Threshold = 0; Kind = 'ratio' },
    @{ Key = 'expressCorrect'; Label = 'Express correct'; Threshold = 0; Kind = 'ratio' },
    @{ Key = 'categorieCorrect'; Label = 'Categorie correcte'; Threshold = 0; Kind = 'ratio' },
    @{ Key = 'serviceCorrect'; Label = 'Service correct'; Threshold = 0; Kind = 'ratio' },
    @{ Key = 'quantiteCorrect'; Label = 'Quantite correcte'; Threshold = 0; Kind = 'ratio' },
    @{ Key = 'nullCorrect'; Label = 'Valeurs null correctes'; Threshold = 0; Kind = 'ratio' },
    @{ Key = 'transcriptionExacte'; Label = 'Transcription exacte'; Threshold = 0; Kind = 'ratio' },
    @{ Key = 'schemaValide'; Label = 'Schema valide'; Threshold = 100; Kind = 'ratio' },
    @{ Key = 'informationsInventees'; Label = 'Informations inventees'; Threshold = 0; Kind = 'count' },
    @{ Key = 'intentionsUnknown'; Label = 'Intentions unknown'; Threshold = 0; Kind = 'ratio' }
)

foreach ($criterion in $criteria) {
    $key = [string]$criterion.Key
    if ($criterion.Kind -eq 'ratio') {
        $b = Get-Ratio -Section $before.final -Name $key
        $a = Get-Ratio -Section $after.final -Name $key
    }
    else {
        $b = Get-Count -Section $before.final -Name $key
        $a = Get-Count -Section $after.final -Name $key
    }
    $threshold = if ([int]$criterion.Threshold -eq 0) { [string]$em } else { '>= ' + [string]$criterion.Threshold + ' %' }
    if ($key -eq 'informationsInventees') { $threshold = '0' }
    $rows.Add(('| {0} | {1} | {2} | {3} |' -f $criterion.Label, $b, $a, $threshold))
}

$rows.Add('')
$rows.Add('## Latence (pipeline complet C#, 150 appels)')
$rows.Add('')
$rows.Add('| Phase | Avant | Apres | Objectif |')
$rows.Add('|---|---:|---:|---:|')
$b95 = [double]$before.phases.totalP95
$a95 = [double]$after.phases.totalP95
$bh95 = [double]$before.phases.httpP95
$ah95 = [double]$after.phases.httpP95
$rows.Add(('| HTTP p95 | {0:F3} s | {1:F3} s | < 5 s |' -f $bh95, $ah95))
$rows.Add(('| Total p95 | {0:F3} s | {1:F3} s | < 5 s |' -f $b95, $a95))
$rows.Add(('| HTTP moyen | {0:F3} s | {1:F3} s | {2} |' -f [double]$before.phases.httpMoyenne, [double]$after.phases.httpMoyenne, $em))
$rows.Add(('| Total moyen | {0:F3} s | {1:F3} s | {2} |' -f [double]$before.phases.totalMoyen, [double]$after.phases.totalMoyen, $em))
$rows.Add(('| Validation JSON (isolee) | {0:F3} ms | {1:F3} ms | {2} |' -f [double]$before.phases.validationMoyenneMs, [double]$after.phases.validationMoyenneMs, $em))
$rows.Add('')
$rows.Add('## Verdict')
$rows.Add('')
$intentOk = [double]$after.final.intentionsExactes.pourcent -ge 90
$itemOk = [double]$after.final.articlesExactes.pourcent -ge 85
$schemaOk = [double]$after.final.schemaValide.pourcent -eq 100
$latencyOk = $a95 -lt 5
$inventOk = [int]$after.final.informationsInventees -eq 0
$rows.Add(('- Intentions >= 90 % : **{0}**' -f $(if ($intentOk) { 'ATTEINT' } else { 'NON ATTEINT' })))
$rows.Add(('- Articles >= 85 % : **{0}**' -f $(if ($itemOk) { 'ATTEINT' } else { 'NON ATTEINT' })))
$rows.Add(('- Schema 100 % : **{0}**' -f $(if ($schemaOk) { 'ATTEINT' } else { 'NON ATTEINT' })))
$rows.Add(('- p95 < 5 s : **{0}**' -f $(if ($latencyOk) { 'ATTEINT' } else { 'NON ATTEINT' })))
$rows.Add(('- Aucune information inventee : **{0}**' -f $(if ($inventOk) { 'ATTEINT' } else { 'NON ATTEINT' })))
$rows.Add('')
$rows.Add('*Jeu syntetique : aucun audio reel, aucun consentement, WER non mesure. Les seuils d intention et d article sont calcules sur le resultat final apres garde-fous C#. *')

$fullOutput = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path (Get-Location).Path $Output }
New-Item -ItemType Directory -Force (Split-Path -Parent $fullOutput) | Out-Null
$utf8NoBom = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllLines($fullOutput, $rows, $utf8NoBom)
Write-Host ('Comparaison ecrite : ' + $fullOutput) -ForegroundColor Green
$rows | ForEach-Object { Write-Host $_ }
