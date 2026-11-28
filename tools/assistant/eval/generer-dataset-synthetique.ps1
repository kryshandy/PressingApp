<#
.SYNOPSIS
  Génère un jeu de 150 scénarios SYNTHÉTIQUES étiquetés, avec vérité terrain figée.
.DESCRIPTION
  Aucun enregistrement audio réel n'est utilisé. Les libellés du catalogue sont lus
  dans %LOCALAPPDATA%\Pressing\ai\catalogue.json afin d'éviter toute faute de copie.
  Le fichier produit est documenté comme synthétique et ne mesure pas le WER.
.EXAMPLE
  .\tools\assistant\eval\generer-dataset-synthetique.ps1
#>
param(
    [string]$Catalogue = (Join-Path $env:LOCALAPPDATA 'Pressing\ai\catalogue.json'),
    [string]$Output = (Join-Path $PSScriptRoot 'dataset-synthetique.jsonl')
)

$ErrorActionPreference = 'Stop'
$e = [char]0x00E9
$E = [char]0x00C9
$a = [char]0x00E0
$o = [char]0x00F4
$c = [char]0x00E7

if (-not (Test-Path $Catalogue)) { throw "Catalogue absent : $Catalogue" }
$cat = [IO.File]::ReadAllText($Catalogue, [Text.Encoding]::UTF8) | ConvertFrom-Json
$categories = @($cat.categories | ForEach-Object { [string]$_.label })
$services = @($cat.services | ForEach-Object { [string]$_.label })
if ($categories.Count -ne 20 -or $services.Count -ne 5) {
    throw "Catalogue inattendu : $($categories.Count) catégories, $($services.Count) services."
}

$lines = New-Object System.Collections.Generic.List[string]
$gender = @($true,$false,$false,$false,$true,$true,$true,$false,$false,$false,$true,$false,$false,$true,$true,$true,$false,$false,$true,$false)
$plurals = @(
    'Chemises','Pantalons',"Costumes 2 pi${e}ces","Costumes 3 pi${e}ces",'Robes simples',"Robes de soir${e}e",
    'Jupes','Chemisiers','Pulls','Manteaux','Doudounes','Jeans','Blousons','Cravates',
    "${E}charpes",'Couvertures','Drap 2 places','Rideaux','Nappes','Tapis'
)
$qtyWords = @('deux','trois','quatre','cinq','six','sept','huit','neuf','dix')
$servicePhrases = @(
    "${a} laver",
    "lavage et repassage",
    "${a} repasser",
    "en nettoyage ${a} sec",
    "en d${e}tachage"
)

function New-ItemValue { param([string]$Category, $Service, $Quantity, $Express)
    return [ordered]@{ category = $Category; service = $Service; quantity = $Quantity; express = $Express }
}

function New-Expected {
    param(
        [string]$Intent, $ClientName, $ClientPhone, [array]$Items,
        $Ticket, $Amount, $PaymentMethod, $Deposit, $PickupDate,
        $ItemStatus, [string]$Transcript
    )
    return [ordered]@{
        intent = $Intent
        client = [ordered]@{ name = $ClientName; phone = $ClientPhone }
        items = @($Items)
        ticket = $Ticket
        amount = $Amount
        payment_method = $PaymentMethod
        deposit = $Deposit
        pickup_date = $PickupDate
        item_status = $ItemStatus
        confidence = 0.9
        transcript = $Transcript
    }
}

function Add-Scenario {
    param([string]$Id, [string]$Reference, $Expected)
    $record = [ordered]@{
        id = $Id
        source = 'synthetique'
        consentement = 'sans objet - phrase creee pour le banc'
        audio = $null
        reference = $Reference
        expected = $Expected
    }
    $lines.Add(($record | ConvertTo-Json -Depth 12 -Compress))
}

# --- S001-S070 : création de commande, un à trois articles -------------------
for ($i = 1; $i -le 70; $i++) {
    $idx = ($i - 1) % $categories.Count
    $cat = $categories[$idx]
    $catLower = $cat.ToLowerInvariant()
    $svcIdx = switch (($i - 1) % 5) { 0 { 0 } 1 { 1 } 2 { 2 } 3 { 3 } default { 4 } }
    $svc = $services[$svcIdx]
    $q = if ($i % 3 -eq 0) { 3 } elseif ($i % 3 -eq 1) { 2 } else { 1 }
    $catPhrase = if ($q -eq 1) { $catLower } else { $plurals[$idx].ToLowerInvariant() }

    if ($q -eq 1) {
        $article = if ($gender[$idx]) { 'une' } else { 'un' }
        $qtyText = $article
        $qtyPrefix = "${a} l'article"
        $qtyPart = "d'$article $catLower"
    }
    else {
        $qtyText = $qtyWords[$q - 2]
        $qtyPrefix = "${a} laver"
        $qtyPart = "de $qtyText $catPhrase"
    }

    $express = $null
    $expressPart = ''
    if ($i % 7 -eq 0) { $express = $true; $expressPart = ', en express' }
    elseif ($i % 11 -eq 0) { $express = $false; $expressPart = ', pas express' }

    $name = $null
    $namePart = ''
    if ($i % 9 -eq 0) { $name = 'Astride'; $namePart = ", je m'appelle Astride" }
    elseif ($i % 13 -eq 0) { $name = 'Ngo Marie'; $namePart = ', pour Madame Ngo Marie' }

    $phone = $null
    $phonePart = ''
    if ($i % 17 -eq 0) { $phone = '0612345678'; $phonePart = ', telephone 0612345678' }

    $pickup = $null
    $pickupPart = ''
    if ($i % 19 -eq 0) { $pickup = '2026-11-28'; $pickupPart = ", ${a} r${e}cup${e}rer demain" }

    $reference = "D${e}p${o}t $qtyPart $($servicePhrases[$svcIdx])$expressPart$namePart$phonePart$pickupPart."
    $expected = New-Expected -Intent 'create_order' -ClientName $name -ClientPhone $phone `
        -Items @((New-ItemValue $cat $svc $q $express)) -Ticket $null -Amount $null `
        -PaymentMethod $null -Deposit $null -PickupDate $pickup -ItemStatus $null -Transcript $reference
    Add-Scenario -Id ("S{0:D3}" -f $i) -Reference $reference -Expected $expected
}

# --- S071-S085 : retrait de commande ----------------------------------------
$pickups = @(
    @{ p = "Retrait pour Madame Ngo."; n = 'Ngo'; t = $null },
    @{ p = "Je viens r${e}cup${e}rer ma commande, ticket PRESS-2026-00142."; n = $null; t = 'PRESS-2026-00142' },
    @{ p = "Retrait de la commande de Marie, ticket 143."; n = 'Marie'; t = 'PRESS-2026-00143' },
    @{ p = "Retrait, ticket PRESS-2026-00144."; n = $null; t = 'PRESS-2026-00144' },
    @{ p = "Je voudrais r${e}cup${e}rer ma commande, s'il vous pla${i}t."; n = $null; t = $null },
    @{ p = "Retrait pour Monsieur Diallo."; n = 'Diallo'; t = $null },
    @{ p = "Retrait de ma commande, ticket 145."; n = $null; t = 'PRESS-2026-00145' },
    @{ p = "Retrait pour Paul, ticket PRESS-2026-00146."; n = 'Paul'; t = 'PRESS-2026-00146' },
    @{ p = "Je r${e}cup${e}re ma commande aujourd'hui."; n = $null; t = $null },
    @{ p = "Retrait de la commande de Sophie."; n = 'Sophie'; t = $null },
    @{ p = "Retrait, ticket 147, pour Madame Ngo."; n = 'Ngo'; t = 'PRESS-2026-00147' },
    @{ p = "Retrait de ma commande, ticket PRESS-2026-00148."; n = $null; t = 'PRESS-2026-00148' },
    @{ p = "Retrait pour Jean-Pierre."; n = 'Jean-Pierre'; t = $null },
    @{ p = "Retrait, ticket 149."; n = $null; t = 'PRESS-2026-00149' },
    @{ p = "Je viens chercher ma commande, ticket PRESS-2026-00151."; n = $null; t = 'PRESS-2026-00151' }
)
for ($j = 0; $j -lt $pickups.Count; $j++) {
    $row = $pickups[$j]
    $expected = New-Expected -Intent 'register_pickup' -ClientName $row.n -ClientPhone $null `
        -Items @() -Ticket $row.t -Amount $null -PaymentMethod $null -Deposit $null `
        -PickupDate $null -ItemStatus $null -Transcript $row.p
    Add-Scenario -Id ("S{0:D3}" -f (71 + $j)) -Reference $row.p -Expected $expected
}

# --- S086-S100 : paiement ou acompte -----------------------------------------
$i = [char]0x0069
$payments = @(
    @{ p = "Acompte de cinq mille en esp${e}ces."; a = 5000; m = 'cash'; d = 5000; t = $null },
    @{ p = "Solde de 3000 par carte."; a = 3000; m = 'card'; d = $null; t = $null },
    @{ p = "Paiement de 1500 par mobile money."; a = 1500; m = 'mobile_money'; d = $null; t = $null },
    @{ p = "Virement de 2500."; a = 2500; m = 'transfer'; d = $null; t = $null },
    @{ p = "Acompte de 2000, ticket PRESS-2026-00152."; a = 2000; m = $null; d = 2000; t = 'PRESS-2026-00152' },
    @{ p = "Je paie 4000 en esp${e}ces."; a = 4000; m = 'cash'; d = $null; t = $null },
    @{ p = "Solde de cinq mille par virement."; a = 5000; m = 'transfer'; d = $null; t = $null },
    @{ p = "Acompte de 1000 par carte, ticket 153."; a = 1000; m = 'card'; d = 1000; t = 'PRESS-2026-00153' },
    @{ p = "Paiement de 7500 par mobile money."; a = 7500; m = 'mobile_money'; d = $null; t = $null },
    @{ p = "Acompte de 3500 en esp${e}ces."; a = 3500; m = 'cash'; d = 3500; t = $null },
    @{ p = "Solde de 900 par carte."; a = 900; m = 'card'; d = $null; t = $null },
    @{ p = "Je verse 1250 par virement."; a = 1250; m = 'transfer'; d = $null; t = $null },
    @{ p = "Acompte de 6000, ticket PRESS-2026-00154."; a = 6000; m = $null; d = 6000; t = 'PRESS-2026-00154' },
    @{ p = "Paiement de 800 en esp${e}ces."; a = 800; m = 'cash'; d = $null; t = $null },
    @{ p = "Solde de 2750 par mobile money, ticket 155."; a = 2750; m = 'mobile_money'; d = $null; t = 'PRESS-2026-00155' }
)
for ($j = 0; $j -lt $payments.Count; $j++) {
    $row = $payments[$j]
    $expected = New-Expected -Intent 'add_payment' -ClientName $null -ClientPhone $null `
        -Items @() -Ticket $row.t -Amount $row.a -PaymentMethod $row.m -Deposit $row.d `
        -PickupDate $null -ItemStatus $null -Transcript $row.p
    Add-Scenario -Id ("S{0:D3}" -f (86 + $j)) -Reference $row.p -Expected $expected
}

# --- S101-S115 : changement d'état d'article ---------------------------------
$statuses = @(
    @{ p = "La chemise est pr${e}te."; cat = $categories[0]; s = 'ready'; t = $null },
    @{ p = "Le pantalon est pr${e}t."; cat = $categories[1]; s = 'ready'; t = $null },
    @{ p = "Le costume deux pi${e}ces est pr${e}t."; cat = $categories[2]; s = 'ready'; t = $null },
    @{ p = "La robe simple est en cours."; cat = $categories[4]; s = 'in_progress'; t = $null },
    @{ p = "La jupe est pr${e}te."; cat = $categories[6]; s = 'ready'; t = $null },
    @{ p = "Le pulls est pr${e}t."; cat = $categories[8]; s = 'ready'; t = $null },
    @{ p = "Le manteau est en cours."; cat = $categories[9]; s = 'in_progress'; t = $null },
    @{ p = "La cravate est pr${e}te."; cat = $categories[13]; s = 'ready'; t = $null },
    @{ p = "L'${e}charpe est pr${e}te."; cat = $categories[14]; s = 'ready'; t = $null },
    @{ p = "La couverture est pr${e}te."; cat = $categories[15]; s = 'ready'; t = $null },
    @{ p = "Le tapis est en cours."; cat = $categories[19]; s = 'in_progress'; t = $null },
    @{ p = "La chemise est pr${e}te, ticket PRESS-2026-00156."; cat = $categories[0]; s = 'ready'; t = 'PRESS-2026-00156' },
    @{ p = "Le jean est pr${e}t, ticket 157."; cat = $categories[11]; s = 'ready'; t = 'PRESS-2026-00157' },
    @{ p = "La doudoune est en cours."; cat = $categories[10]; s = 'in_progress'; t = $null },
    @{ p = "Le blouson est pr${e}t."; cat = $categories[12]; s = 'ready'; t = $null }
)
for ($j = 0; $j -lt $statuses.Count; $j++) {
    $row = $statuses[$j]
    $expected = New-Expected -Intent 'update_item_status' -ClientName $null -ClientPhone $null `
        -Items @((New-ItemValue $row.cat $null 1 $null)) -Ticket $row.t -Amount $null `
        -PaymentMethod $null -Deposit $null -PickupDate $null -ItemStatus $row.s -Transcript $row.p
    Add-Scenario -Id ("S{0:D3}" -f (101 + $j)) -Reference $row.p -Expected $expected
}

# --- S116-S130 : consultation seulement --------------------------------------
$finds = @(
    @{ p = "O${u} en est le ticket 142 ?"; t = 'PRESS-2026-00142' },
    @{ p = "O${u} en est ma commande ?"; t = $null },
    @{ p = "Statut de la commande PRESS-2026-00158."; t = 'PRESS-2026-00158' },
    @{ p = "O${u} en est le ticket PRESS-2026-00159 ?"; t = 'PRESS-2026-00159' },
    @{ p = "Je voudrais savoir o${u} en est ma commande, ticket 160."; t = 'PRESS-2026-00160' },
    @{ p = "O${u} en est le dossier de Marie ?"; t = $null },
    @{ p = "Quel est le statut du ticket PRESS-2026-00161 ?"; t = 'PRESS-2026-00161' },
    @{ p = "O${u} en est ma commande, ticket 162 ?"; t = 'PRESS-2026-00162' },
    @{ p = "Je cherche le statut de ma commande."; t = $null },
    @{ p = "O${u} en est le ticket PRESS-2026-00163 ?"; t = 'PRESS-2026-00163' },
    @{ p = "Pevez-vous me dire o${u} en est ma commande ?"; t = $null },
    @{ p = "O${u} en est le ticket 164 ?"; t = 'PRESS-2026-00164' },
    @{ p = "Statut de la commande, ticket PRESS-2026-00165."; t = 'PRESS-2026-00165' },
    @{ p = "O${u} en est la commande de Sophie ?"; t = $null },
    @{ p = "O${u} en est le ticket PRESS-2026-00166 ?"; t = 'PRESS-2026-00166' }
)
for ($j = 0; $j -lt $finds.Count; $j++) {
    $row = $finds[$j]
    $expected = New-Expected -Intent 'find_order' -ClientName $null -ClientPhone $null `
        -Items @() -Ticket $row.t -Amount $null -PaymentMethod $null -Deposit $null `
        -PickupDate $null -ItemStatus $null -Transcript $row.p
    Add-Scenario -Id ("S{0:D3}" -f (116 + $j)) -Reference $row.p -Expected $expected
}

# --- S131-S150 : incompréhensible ou hors périmètre --------------------------
$unknowns = @(
    "Euh, je ne sais pas trop, peut-${e}tre quelque chose ${a} faire avec ce pantalon.",
    "Je voudrais juste dire bonjour.",
    "Quel temps fait-il ?",
    "Peut-${e}tre une chemise, je ne sais pas.",
    "Euh... laissez-moi r${e}fl${e}chir.",
    "Je ne sais pas ce que je veux.",
    "C'est compliqu${e}, peut-${e}tre plus tard.",
    "Une chemise, peut-${e}tre, ou alors pas.",
    "J'ai une question sans rapport avec le pressing.",
    "Euh, je voudrais r${e}fl${e}chir ${a} propos de cette cravate.",
    "Quelque chose ${a} faire avec ${c}a, je ne sais pas quoi.",
    "Je passe juste vous saluer.",
    "Peut-${e}tre qu'il faut faire quelque chose, je ne sais pas.",
    "Euh, je ne suis pas s${u}r de moi.",
    "Je reviens plus tard, je ne sais pas encore.",
    "Avez-vous des nouvelles du quartier ?",
    "Je ne comprends pas bien ce que je veux.",
    "Peut-${e}tre une robe, mais je ne suis pas certain.",
    "Euh, ${c}a d${e}pend, je ne sais pas trop.",
    "Je voudrais juste parler un peu."
)
for ($j = 0; $j -lt $unknowns.Count; $j++) {
    $expected = New-Expected -Intent 'unknown' -ClientName $null -ClientPhone $null `
        -Items @() -Ticket $null -Amount $null -PaymentMethod $null -Deposit $null `
        -PickupDate $null -ItemStatus $null -Transcript $unknowns[$j]
    $expected.confidence = 0.4
    Add-Scenario -Id ("S{0:D3}" -f (131 + $j)) -Reference $unknowns[$j] -Expected $expected
}

if ($lines.Count -ne 150) { throw "Le jeu contient $($lines.Count) lignes au lieu de 150." }

# Vérification minimale avant écriture : JSON valide, identifiants uniques, transcript = référence.
$ids = New-Object System.Collections.Generic.HashSet[string]
foreach ($line in $lines) {
    $row = $line | ConvertFrom-Json
    if (-not $ids.Add([string]$row.id)) { throw "Identifiant dupliqué : $($row.id)" }
    if ([string]$row.reference -cne [string]$row.expected.transcript) {
        throw "Transcript de vérité terrain différent de la référence pour $($row.id)."
    }
    if ([string]$row.source -cne 'synthetique') { throw "Scénario non étiqueté synthétique : $($row.id)" }
}

$utf8NoBom = New-Object Text.UTF8Encoding($false)
$full = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path (Get-Location).Path $Output }
[IO.File]::WriteAllLines($full, $lines, $utf8NoBom)
Write-Host "Jeu synthétique écrit : $full" -ForegroundColor Green
Write-Host "Lignes : $($lines.Count) (aucun audio réel, aucun consentement, WER non mesurable)." -ForegroundColor Yellow
