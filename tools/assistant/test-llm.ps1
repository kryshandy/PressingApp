<#
.SYNOPSIS
  Test fonctionnel REEL du moteur LLM local (llama.cpp 127.0.0.1:8081).
.DESCRIPTION
  Envoie six demandes francaises representatives avec le prompt de production, le catalogue
  reel et le schema JSON de production (response_format). Verifie pour chaque reponse :
  HTTP, JSON, contrat, valeurs, catalogue, champs null et absence d'inventions.
  Le script reproduit le recalage de securite de LlmIntentExtractor (transcription exacte,
  express recale sur les mots prononces) et consigne les ecarts bruts du modele comme limites.

  Options :
    -InclureGrammaire     : envoie aussi une requete avec la GBNF en chaine ( hors production ).
    -InclureAudioPipeline : genere une phrase via la synthese vocale Windows, l'envoie a
                            Whisper (8082), puis la transcription au LLM, et supprime le WAV.
    -RepetitionsLatence N : ajoute N appels supplementaires de la demande simple pour la latence.

  Ce script ne modifie aucun fichier de production, ni le modele, ni la configuration.
  Les fichiers audio temporaires sont supprimes enfin de traitement.
.EXAMPLE
  .\tools\assistant\test-llm.ps1
  .\tools\assistant\test-llm.ps1 -InclureGrammaire -InclureAudioPipeline -RepetitionsLatence 4
#>
param(
    [switch]$InclureGrammaire,
    [switch]$InclureAudioPipeline,
    [ValidateRange(0, 20)]
    [int]$RepetitionsLatence = 0
)

$ErrorActionPreference = 'Stop'
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [System.Text.Encoding]::UTF8
} catch { }

$assistantDir = $PSScriptRoot
$aiRoot = Join-Path $env:LOCALAPPDATA 'Pressing\ai'
$llmUrl = 'http://127.0.0.1:8081'
$sttUrl = 'http://127.0.0.1:8082'
$model = 'pressing-local'
# PowerShell 5.1 lit ce script sans BOM en ANSI : construire le libelle accentue par code Unicode.
$accentA = [char]0x00E0
$serviceNettoyageSec = "Nettoyage ${accentA} sec"
$script:latencies = New-Object System.Collections.Generic.List[double]
$script:allProblems = New-Object System.Collections.Generic.List[string]
$script:modelLimitations = New-Object System.Collections.Generic.List[string]
$script:scenarioOk = 0

function Test-Port {
    param([int]$Port)
    $client = New-Object System.Net.Sockets.TcpClient
    try { $client.Connect('127.0.0.1', $Port); return $true }
    catch { return $false }
    finally { $client.Close() }
}

function Get-Value {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-ExpressProduction {
    param($RawExpress, [string]$Text)
    $decomposed = $Text.Normalize([System.Text.NormalizationForm]::FormD)
    $builder = New-Object System.Text.StringBuilder
    foreach ($c in $decomposed.ToCharArray()) {
        if ([System.Globalization.CharUnicodeInfo]::GetUnicodeCategory($c) -ne [System.Globalization.UnicodeCategory]::NonSpacingMark) {
            [void]$builder.Append($c)
        }
    }
    $normalized = $builder.ToString().ToLowerInvariant()
    if ($normalized.Contains('pas express') -or $normalized.Contains("pas d'express") -or
        $normalized.Contains('sans express') -or $normalized.Contains('non express') -or
        ($normalized.Contains('ne veux pas') -and $normalized.Contains('express'))) {
        return $false
    }
    if ($normalized.Contains('express')) { return $true }
    return $null
}

function Assert-Equal {
    param(
        [string]$Label, $Actual, $Expected,
        [System.Collections.Generic.List[string]]$Problems
    )
    $actualText = if ($null -eq $Actual) { '<null>' } else { [string]$Actual }
    $expectedText = if ($null -eq $Expected) { '<null>' } else { [string]$Expected }
    if ($null -eq $Expected) {
        if ($null -eq $Actual -or [string]::IsNullOrWhiteSpace([string]$Actual)) { return $true }
    }
    elseif ($null -ne $Actual -and ([string]$Actual).Trim() -ceq ([string]$Expected).Trim()) {
        return $true
    }
    $Problems.Add("$Label = $actualText (attendu : $expectedText)")
    Write-Host "  INCORRECT $Label = $actualText (attendu : $expectedText)" -ForegroundColor Red
    return $false
}

Write-Host '=== 1. DISPONIBILITE DES SERVICES ===' -ForegroundColor Cyan
foreach ($port in @(8081, 8082)) {
    if (Test-Port $port) { Write-Host "Port $port : OUVERT" -ForegroundColor Green }
    else { Write-Host "Port $port : FERME" -ForegroundColor Red }
}
if (-not (Test-Port 8081)) { throw 'llama.cpp indisponible sur 127.0.0.1:8081. Aucun test LLM effectue.' }
try {
    $health = Invoke-RestMethod "$llmUrl/health" -TimeoutSec 5
    Write-Host "LLM health : $($health | ConvertTo-Json -Compress)"
} catch { throw "LLM /health inaccessible : $($_.Exception.Message)" }
try {
    $sttHealth = Invoke-RestMethod "$sttUrl/health" -TimeoutSec 5
    Write-Host "STT health : $($sttHealth | ConvertTo-Json -Compress)"
} catch { Write-Host "STT /health inaccessible : $($_.Exception.Message)" -ForegroundColor Yellow }

Write-Host ''
Write-Host '=== 2. PROMPT, CATALOGUE ET SCHEMA ===' -ForegroundColor Cyan
$templatePath = Join-Path $assistantDir 'prompt-systeme.txt'
$schemaPath = Join-Path $assistantDir 'intent-schema.json'
$catalogPath = Join-Path $aiRoot 'catalogue.json'
$grammarPath = Join-Path $aiRoot 'grammar.gbnf'

foreach ($path in @($templatePath, $schemaPath, $catalogPath)) {
    if (-not (Test-Path $path)) { throw "Fichier absent : $path" }
}
$template = [System.IO.File]::ReadAllText($templatePath, [System.Text.Encoding]::UTF8)
$schema = [System.IO.File]::ReadAllText($schemaPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
$localCatalog = [System.IO.File]::ReadAllText($catalogPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json

$categories = @($localCatalog.categories | ForEach-Object { [string]$_.label })
$services = @($localCatalog.services | ForEach-Object { [string]$_.label })
$synonyms = @($localCatalog.synonyms | ForEach-Object { [string]$_ })
$catalogText = "Catégories : $($categories -join ', ')`nServices : $($services -join ', ')`nSynonymes : $($synonyms -join ', ')"
$systemPrompt = $template.Replace('{{DATE_REFERENCE}}', [string]$localCatalog.referenceDate)
$systemPrompt = $systemPrompt.Replace('{{CATALOGUE}}', $catalogText)

Write-Host "Prompt : $templatePath ($($template.Length) caracteres, catalogue injecte)"
Write-Host "Catalogue reel : $($categories.Count) categories, $($services.Count) services, $($synonyms.Count) synonymes"
Write-Host "Schema  : $schemaPath"
Write-Host "Modele  : $model | temperature 0,1 | response_format json_schema"

function New-RequestBody {
    param([string]$Text)
    $payload = [ordered]@{
        model = $model
        messages = @(
            @{ role = 'system'; content = $systemPrompt },
            @{ role = 'user'; content = $Text }
        )
        temperature = 0.1
        max_tokens = 512
        response_format = @{
            type = 'json_schema'
            json_schema = @{ name = 'pressing_intent'; strict = $true; schema = $schema }
        }
    }
    # Virgule devant le retour : PowerShell enumere sinon le tableau d'octets.
    return ,([System.Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json -Depth 20 -Compress)))
}

function Invoke-Llm {
    param([string]$Text)
    $bytes = New-RequestBody -Text $Text
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $response = Invoke-WebRequest -Uri "$llmUrl/v1/chat/completions" -Method Post `
        -Body $bytes -ContentType 'application/json; charset=utf-8' -TimeoutSec 120 -UseBasicParsing
    $watch.Stop()
    $raw = [System.Text.Encoding]::UTF8.GetString($response.RawContentStream.ToArray())
    $envelope = $raw | ConvertFrom-Json
    $choices = @($envelope.choices)
    if ($choices.Count -eq 0) { throw 'Reponse LLM sans choices.' }
    $content = [string]$choices[0].message.content
    return [pscustomobject]@{
        Raw = $raw
        Content = $content
        Seconds = $watch.Elapsed.TotalSeconds
        Status = [int]$response.StatusCode
    }
}

$allowedFields = @('intent', 'client', 'items', 'ticket', 'amount', 'payment_method',
    'deposit', 'pickup_date', 'item_status', 'confidence', 'transcript')
$allowedIntents = @('create_order', 'register_pickup', 'add_payment', 'update_item_status', 'find_order', 'unknown')

function Test-ContractShape {
    param($Payload, [System.Collections.Generic.List[string]]$Problems)
    foreach ($field in $allowedFields) {
        if ($null -eq $Payload.PSObject.Properties[$field]) {
            $Problems.Add("Champ obligatoire absent : $field")
            Write-Host "  MANQUANT    $field" -ForegroundColor Red
        }
    }
    foreach ($property in $Payload.PSObject.Properties) {
        if ($allowedFields -notcontains $property.Name) {
            $Problems.Add("Champ hors contrat : $($property.Name)")
            Write-Host "  HORS CONTRAT $($property.Name)" -ForegroundColor Red
        }
    }
    if ($allowedIntents -notcontains [string](Get-Value $Payload 'intent')) {
        $Problems.Add("Intention hors contrat : $(Get-Value $Payload 'intent')")
    }
    foreach ($item in @(Get-Value $Payload 'items')) {
        foreach ($field in @('category', 'service', 'quantity', 'express')) {
            if ($null -eq $item.PSObject.Properties[$field]) {
                $Problems.Add("items[].$field absent")
            }
        }
    }
}

function Test-Scenario {
    param($Scenario)

    Write-Host ''
    Write-Host "--- $($Scenario.Name) ---" -ForegroundColor Cyan
    Write-Host "Phrase : $($Scenario.Text)"
    try {
        $result = Invoke-Llm -Text $Scenario.Text
    } catch {
        $details = if ($_.ErrorDetails.Message) { " | $($_.ErrorDetails.Message)" } else { "" }
        $message = "APPEL ECHOUÉ : $($_.Exception.Message)$details"
        Write-Host "  $message" -ForegroundColor Red
        $script:allProblems.Add("$($Scenario.Name) : $message")
        return
    }

    Write-Host "HTTP $($result.Status) en $($result.Seconds.ToString('F2')) s"
    Write-Host 'Reponse brute :' -ForegroundColor DarkCyan
    Write-Host $result.Raw
    $script:latencies.Add($result.Seconds)

    $problems = New-Object System.Collections.Generic.List[string]
    $payload = $null
    try { $payload = $result.Content | ConvertFrom-Json } catch {
        $message = "Contenu du modele non analysable : $($_.Exception.Message)"
        Write-Host "  $message" -ForegroundColor Red
        $problems.Add($message)
    }

    if ($null -ne $payload) {
        Test-ContractShape $payload $problems | Out-Null

        $intent = Get-Value $payload 'intent'
        $confidence = -1.0
        try { $confidence = [Convert]::ToDouble((Get-Value $payload 'confidence'), [Globalization.CultureInfo]::InvariantCulture) } catch { }
        if ($confidence -lt 0 -or $confidence -gt 1) {
            $problems.Add("confidence hors borne : $confidence")
        }

        $intentOk = $Scenario.Intent -contains $intent
        if ($Scenario.AccepterFaibleConfiance -and $confidence -lt 0.6 -and $intent -ne 'unknown') {
            $intentOk = $true
        }
        if (-not $intentOk) {
            $problems.Add("intent = $intent (attendu : $($Scenario.Intent -join ' ou '))")
            Write-Host "  INCORRECT intent = $intent" -ForegroundColor Red
        }
        if ($intent -eq 'unknown' -and $confidence -ge 0.6) {
            $note = "$($Scenario.Name) : unknown avec confidence $confidence ; la regle metier declenche neanmoins NotUnderstood sur unknown."
            $script:modelLimitations.Add($note)
            Write-Host "  LIMITE MODELE : $note" -ForegroundColor Yellow
        }

        $client = Get-Value $payload 'client'
        Assert-Equal 'client.name' (Get-Value $client 'name') $Scenario.ExpectedName $problems | Out-Null
        Assert-Equal 'client.phone' (Get-Value $client 'phone') $null $problems | Out-Null

        $items = @(Get-Value $payload 'items')
        if ($items.Count -ne $Scenario.Items.Count) {
            $problems.Add("items contient $($items.Count) article(s), attendu $($Scenario.Items.Count)")
            Write-Host "  INCORRECT items = $($items.Count) (attendu $($Scenario.Items.Count))" -ForegroundColor Red
        }
        for ($i = 0; $i -lt $Scenario.Items.Count; $i++) {
            if ($i -ge $items.Count) { break }
            $expected = $Scenario.Items[$i]
            $item = $items[$i]
            Assert-Equal "items[$i].category" (Get-Value $item 'category') $expected.Category $problems | Out-Null
            Assert-Equal "items[$i].service" (Get-Value $item 'service') $expected.Service $problems | Out-Null
            Assert-Equal "items[$i].quantity" (Get-Value $item 'quantity') $expected.Quantity $problems | Out-Null
            $rawExpress = Get-Value $item 'express'
            $productionExpress = Get-ExpressProduction -RawExpress $rawExpress -Text $Scenario.Text
            if ($null -ne $rawExpress -and $rawExpress -ne $productionExpress) {
                $note = "$($Scenario.Name) : express brut = <$rawExpress> ; phrase sans refusal explicite, production impose <$productionExpress>."
                $script:modelLimitations.Add($note)
                Write-Host "  LIMITE MODELE : $note" -ForegroundColor Yellow
            }
            Assert-Equal "items[$i].express (production)" $productionExpress $expected.Express $problems | Out-Null

            $service = [string](Get-Value $item 'service')
            if ($service -ieq 'laver') {
                $problems.Add("items[$i].service = 'laver' : verbe brut au lieu du libelle du catalogue 'Lavage'.")
            }
            $category = [string](Get-Value $item 'category')
            if ($category -and $categories -notcontains $category) {
                $problems.Add("items[$i].category = '$category' absente du catalogue.")
            }
            if ($service -and $services -notcontains $service) {
                $problems.Add("items[$i].service = '$service' absent du catalogue.")
            }
        }

        foreach ($field in @('ticket', 'amount', 'payment_method', 'deposit', 'pickup_date', 'item_status')) {
            Assert-Equal $field (Get-Value $payload $field) $null $problems | Out-Null
        }
        # Le modele peut normaliser les accents de la transcription malgre la consigne.
        # En production, LlmIntentExtractor remplace ce champ par le texte recu ; le test unitaire
        # C# couvre ce garde-fou. On consigne l'ecart brut comme limite, sans le confondre avec
        # une non-conformite metier du pipeline.
        $rawTranscript = Get-Value $payload 'transcript'
        if ($null -eq $rawTranscript -or $rawTranscript -cne $Scenario.Text) {
            $note = "$($Scenario.Name) : transcript brut = <$rawTranscript> au lieu de <$($Scenario.Text)> ; texte original impose par LlmIntentExtractor en production."
            $script:modelLimitations.Add($note)
            Write-Host "  LIMITE MODELE : $note" -ForegroundColor Yellow
        }

        if (-not $Scenario.AccepterFaibleConfiance -and $confidence -lt 0.6) {
            $problems.Add("confidence = $confidence (< 0,6 pour une demande claire)")
        }
    }

    if ($problems.Count -eq 0) {
        $script:scenarioOk++
        Write-Host 'RESULTAT : CONFORME' -ForegroundColor Green
    } else {
        Write-Host "RESULTAT : $($problems.Count) ecart(s)" -ForegroundColor Red
        $n = 0
        foreach ($problem in $problems) {
            $n++
            Write-Host "  $n. $problem" -ForegroundColor Yellow
            $script:allProblems.Add("$($Scenario.Name) : $problem")
        }
    }
}

$scenarios = @(
    [pscustomobject]@{
        Name = '1. Demande simple'
        Text = "Bonjour, je voudrais faire laver trois chemises et un pantalon. Je m'appelle Astride."
        Intent = @('create_order'); ExpectedName = 'Astride'; AccepterFaibleConfiance = $false
        Items = @(
            [pscustomobject]@{ Category = 'Chemise'; Service = 'Lavage'; Quantity = 3; Express = $null }
            [pscustomobject]@{ Category = 'Pantalon'; Service = 'Lavage'; Quantity = 1; Express = $null }
        )
    }
    [pscustomobject]@{
        Name = '2. Plusieurs articles et services'
        Text = 'Bonjour, je depose deux chemises a laver, trois pantalons a repasser et une cravate en nettoyage a sec.'
        Intent = @('create_order'); ExpectedName = $null; AccepterFaibleConfiance = $false
        Items = @(
            [pscustomobject]@{ Category = 'Chemise'; Service = 'Lavage'; Quantity = 2; Express = $null }
            [pscustomobject]@{ Category = 'Pantalon'; Service = 'Repassage'; Quantity = 3; Express = $null }
            [pscustomobject]@{ Category = 'Cravate'; Service = $serviceNettoyageSec; Quantity = 1; Express = $null }
        )
    }
    [pscustomobject]@{
        Name = '3. Express explicite'
        Text = 'Je voudrais faire laver une chemise en express, s il vous plait.'
        Intent = @('create_order'); ExpectedName = $null; AccepterFaibleConfiance = $false
        Items = @(
            [pscustomobject]@{ Category = 'Chemise'; Service = 'Lavage'; Quantity = 1; Express = $true }
        )
    }
    [pscustomobject]@{
        Name = '4. Sans mention express'
        Text = 'Je depose une chemise a laver, s il vous plait.'
        Intent = @('create_order'); ExpectedName = $null; AccepterFaibleConfiance = $false
        Items = @(
            [pscustomobject]@{ Category = 'Chemise'; Service = 'Lavage'; Quantity = 1; Express = $null }
        )
    }
    [pscustomobject]@{
        Name = '5. Informations manquantes'
        Text = 'Bonjour, je voudrais faire laver trois chemises.'
        Intent = @('create_order'); ExpectedName = $null; AccepterFaibleConfiance = $false
        Items = @(
            [pscustomobject]@{ Category = 'Chemise'; Service = 'Lavage'; Quantity = 3; Express = $null }
        )
    }
    [pscustomobject]@{
        Name = '6. Demande ambigue'
        Text = 'Euh, je ne sais pas trop, peut-etre quelque chose a faire avec ce pantalon.'
        Intent = @('unknown'); ExpectedName = $null; AccepterFaibleConfiance = $true
        Items = @()
    }
)

Write-Host ''
Write-Host '=== 3. SIX SCENARIOS REELS ===' -ForegroundColor Cyan
foreach ($scenario in $scenarios) { Test-Scenario -Scenario $scenario }

if ($RepetitionsLatence -gt 0) {
    Write-Host ''
    Write-Host "=== 4. REPETITIONS DE LATENCE ($RepetitionsLatence appels) ===" -ForegroundColor Cyan
    for ($i = 1; $i -le $RepetitionsLatence; $i++) {
        try {
            $r = Invoke-Llm -Text $scenarios[0].Text
            $script:latencies.Add($r.Seconds)
            $parsed = $r.Content | ConvertFrom-Json
            $ok = ($parsed.PSObject.Properties['intent'] -and $parsed.items[0].express -eq $null)
            Write-Host "  repetition $i : HTTP $($r.Status), $($r.Seconds.ToString('F2')) s, JSON $(if ($ok) {'valide'} else {'INVALIDE'})"
            if (-not $ok) { $script:allProblems.Add("Repetition $i : JSON ou express non conforme") }
        } catch {
            Write-Host "  repetition $i : ECHEC $($_.Exception.Message)" -ForegroundColor Red
            $script:allProblems.Add("Repetition $i : $($_.Exception.Message)")
        }
    }
}

if ($InclureGrammaire) {
    Write-Host ''
    Write-Host '=== 5. TEST GRAMMAIRE GBNF (hors production) ===' -ForegroundColor Cyan
    if (-not (Test-Path $grammarPath)) { throw "Grammaire absente : $grammarPath" }
    $grammar = [System.IO.File]::ReadAllText($grammarPath, [System.Text.Encoding]::UTF8)
    $payload = [ordered]@{
        model = $model
        messages = @(
            @{ role = 'system'; content = $systemPrompt },
            @{ role = 'user'; content = $scenarios[0].Text }
        )
        grammar = $grammar
        temperature = 0.1
        max_tokens = 512
        stream = $false
    }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json -Depth 12 -Compress))
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $response = Invoke-WebRequest -Uri "$llmUrl/v1/chat/completions" -Method Post `
        -Body $bytes -ContentType 'application/json; charset=utf-8' -TimeoutSec 120 -UseBasicParsing
    $watch.Stop()
    $raw = [System.Text.Encoding]::UTF8.GetString($response.RawContentStream.ToArray())
    Write-Host "HTTP $([int]$response.StatusCode) en $($watch.Elapsed.TotalSeconds.ToString('F2')) s"
    Write-Host 'Reponse brute (grammaire) :' -ForegroundColor DarkCyan
    Write-Host $raw
    try {
        $envelope = $raw | ConvertFrom-Json
        $content = [string]$envelope.choices[0].message.content
        $parsed = $content | ConvertFrom-Json
        Write-Host 'GBNF : JSON valide.' -ForegroundColor Green
        Write-Host "  intent=$($parsed.intent) ; service=$($parsed.items[0].service) ; express=$($parsed.items[0].express)"
    } catch {
        Write-Host "GBNF : ECHEC $($_.Exception.Message)" -ForegroundColor Red
        $script:allProblems.Add("GBNF : $($_.Exception.Message)")
    }
}

if ($InclureAudioPipeline) {
    Write-Host ''
    Write-Host '=== 6. PIPELINE AUDIO REEL : TTS -> WHISPER -> LLM ===' -ForegroundColor Cyan
    $wav = Join-Path $env:TEMP ("pressing-llm-" + [guid]::NewGuid().ToString('N') + '.wav')
    try {
        Add-Type -AssemblyName System.Speech -ErrorAction Stop
        $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
        $format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(
            16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,
            [System.Speech.AudioFormat.AudioChannel]::Mono)
        $synth.SetOutputToWaveFile($wav, $format)
        $synth.Speak($scenarios[0].Text)
        $synth.Dispose()
        Write-Host "WAV temporaire : $((Get-Item $wav).Length) octets, 16 kHz mono PCM 16 bits"

        $sttRaw = & curl.exe -sS -F "file=@$wav" -F 'language=fr' -F 'response_format=json' "$sttUrl/inference" 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'whisper.cpp a refuse le WAV.' }
        $stt = ($sttRaw -join "`n") | ConvertFrom-Json
        $transcript = [string]$stt.text
        Write-Host "Transcription Whisper : $transcript"
        if ($transcript -notmatch 'trois' -or $transcript -notmatch 'Astride') {
            $note = "Whisper base a degrade la phrase TTS : <$transcript> ; le modele small n'est pas telecharge pour cette machine."
            $script:modelLimitations.Add($note)
            Write-Host "  LIMITE STT : $note" -ForegroundColor Yellow
        }

        $watch = [Diagnostics.Stopwatch]::StartNew()
        $result = Invoke-Llm -Text $transcript
        $watch.Stop()
        Write-Host "LLM en $($result.Seconds.ToString('F2')) s"
        Write-Host 'Reponse brute (pipeline) :' -ForegroundColor DarkCyan
        Write-Host $result.Raw
        try {
            $parsed = $result.Content | ConvertFrom-Json
            $rawExpress = $parsed.items[0].express
            $express = Get-ExpressProduction -RawExpress $rawExpress -Text $transcript
            $service = $parsed.items[0].service
            $rawTranscript = $parsed.transcript
            if ($null -ne $rawExpress -and $rawExpress -ne $express) {
                $note = "Pipeline audio : express brut = <$rawExpress> ; phrase sans refusal explicite, production impose <$express>."
                $script:modelLimitations.Add($note)
                Write-Host "  LIMITE MODELE : $note" -ForegroundColor Yellow
            }
            if ($null -eq $rawTranscript -or $rawTranscript -cne $transcript) {
                $note = "Pipeline audio : transcript brut = <$rawTranscript> au lieu de <$transcript> ; texte original impose par LlmIntentExtractor en production."
                $script:modelLimitations.Add($note)
                Write-Host "  LIMITE MODELE : $note" -ForegroundColor Yellow
            }
            if ($parsed.intent -ne 'create_order' -or $null -ne $express -or $service -ne 'Lavage') {
                throw "intent=$($parsed.intent), service=$service, express production=$express (attendu create_order / Lavage / null)"
            }
            Write-Host 'Pipeline audio : transcription -> intention validee apres recalage production.' -ForegroundColor Green
        } catch {
            Write-Host "Pipeline audio : ECHEC $($_.Exception.Message)" -ForegroundColor Red
            $script:allProblems.Add("Pipeline audio : $($_.Exception.Message)")
        }
    } catch {
        Write-Host "Pipeline audio indisponible : $($_.Exception.Message)" -ForegroundColor Red
        $script:allProblems.Add("Pipeline audio : $($_.Exception.Message)")
    } finally {
        if (Test-Path $wav) { Remove-Item $wav -Force }
        Write-Host 'WAV temporaire supprime.' -ForegroundColor DarkCyan
    }
}

Write-Host ''
Write-Host '=== 7. PERFORMANCE ===' -ForegroundColor Cyan
if ($script:latencies.Count -gt 0) {
    $sorted = @($script:latencies | Sort-Object)
    $count = $sorted.Count
    $sum = 0.0
    foreach ($value in $sorted) { $sum += $value }
    $average = $sum / $count
    $p95Index = [Math]::Min($count - 1, [Math]::Max(0, [Math]::Ceiling(0.95 * $count) - 1))
    Write-Host "Requetes mesurees : $count"
    for ($i = 0; $i -lt $count; $i++) {
        Write-Host ("  #{0,2} : {1,6:F2} s" -f ($i + 1), $sorted[$i])
    }
    Write-Host ("Moyenne : {0:F2} s | minimum : {1:F2} s | maximum : {2:F2} s" -f $average, $sorted[0], $sorted[$count - 1])
    Write-Host ("p95 (rang {0}/{1}) : {2:F2} s" -f ($p95Index + 1), $count, $sorted[$p95Index])
    if ($sorted[$p95Index] -lt 5.0) {
        Write-Host 'Objectif p95 < 5 s : ATTEINT sur cet echantillon.' -ForegroundColor Green
    } else {
        Write-Host 'Objectif p95 < 5 s : NON ATTEINT sur cet echantillon.' -ForegroundColor Red
    }
}

Write-Host ''
Write-Host '=== 8. VERDICT ===' -ForegroundColor Cyan
Write-Host "Scenarios conformes : $script:scenarioOk / $($scenarios.Count)"
Write-Host "Ecarts enregistres : $($script:allProblems.Count)"
Write-Host "Limites observees (sorties brutes / STT) : $($script:modelLimitations.Count)"
if ($script:allProblems.Count -eq 0 -and $script:scenarioOk -eq $scenarios.Count) {
    if ($script:modelLimitations.Count -eq 0) {
        Write-Host 'TEST REEL : CONFORME' -ForegroundColor Green
    } else {
        Write-Host 'TEST REEL METIER : CONFORME apres couche de securite C#' -ForegroundColor Green
        foreach ($limitation in $script:modelLimitations) { Write-Host "  - $limitation" -ForegroundColor Yellow }
    }
} else {
    Write-Host 'TEST REEL : NON CONFORME' -ForegroundColor Red
    foreach ($problem in $script:allProblems) { Write-Host "  - $problem" -ForegroundColor Yellow }
}
