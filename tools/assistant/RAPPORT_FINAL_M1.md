# Rapport final — module IA local M1 (PressingApp)

Date : 27 novembre 2026. Mis à jour le 28 novembre 2026 (T1.6, correction d'un alias ambigu, comparaisons de modèles terminées). Auteur : Membre 1. Périmètre : **M1 exclusivement**.

Ce rapport clôt le lot de correction **P1–P4** et consigne la validation de bout en bout de la chaîne locale :

```text
TTS synthétique → Whisper → LLM local → extraction C# → garde-fous → contrat JSON
```

Il distingue explicitement quatre catégories de résultats, sans les mélanger :

- **Live** : appels réels aux moteurs `127.0.0.1:8081` / `8082`. Le banc final est `eval/resultats/live-20261127-final-150-v2.*` (150 appels) ; les six scénarios du script sont dans `live-20261127-curated.*`.
- **Recalcul hors ligne** : rejeu de sorties déjà capturées à travers les garde-fous corrigés, sans nouvel appel réseau (`apres-correction-m1-v2.*`).
- **Synthétique / TTS** : phrases générées par synthèse vocale Windows ; elles mesurent le moteur, **pas** des données clients.
- **Comparaisons terminées** : quatre couples `1,5B/3B × base/small` exécutés sur 150 scénarios texte le 28/11 (`eval/resultats/comparaisons/*-150.*`) ; défaut `1,5B`/`base` conservé. **Non exécuté** : banc officiel audio et WER réel.

Aucun résultat synthétique n'est présenté comme un résultat client. Aucun WER officiel n'est publié. Le module n'est **pas déclaré opérationnel en production** tant que le p95 et le banc officiel ne sont pas satisfaits.

---

## 1. Périmètre et invariants — respectés

| Invariant | État vérifié |
|---|---|
| M1 uniquement ; aucune route/UI métier M2/M3 | respecté ; seules les interfaces et le DTO nécessaires à M2 ont été exposés |
| Aucun WhatsApp, e-mail ni rappel M3 | respecté |
| `Qwen2.5-1.5B-Instruct Q4_K_M` reste le défaut | respecté ; `/props` confirme `qwen2.5-1.5b-instruct-q4_k_m.gguf` |
| Aucun remplacement de modèle par défaut | respecté ; 0,5B déjà évalué puis rejeté. `3B` et `small` sont installés et vérifiés le 28/11 pour comparaison seulement ; le défaut reste `1,5B`/`base` |
| Ports `8081`, `8082`, `5180`, `5181` | inchangés ; les quatre étaient **ouverts** au contrôle final, `llm=ok`, `stt=ok`, site web HTTP 200 |
| Modes `-SansDesktop`, `-SansBuild`, `-SansIA` | conservés et utilisés lors du redémarrage des applications |
| Contrat JSON figé | respecté ; aucune propriété ajoutée ou retirée dans `IntentPayload`. Le DTO M2 `AssistantIntentDto` mappe exactement les mêmes champs JSON |
| `prompt-systeme.txt` | hash court `E42270B32FEF0BFE` |
| `grammar.gbnf` / `intent-schema.json` | non modifiés ; `3E94DBC9B2493515` / `BB722E2093362759` |
| `vocab-default.txt` | aligné sur le prompt généré ; hash court `44B8962ECDBAF2FD` |
| Catalogue dynamique | unique source via `GET /api/public/catalog` ; aucun accès SQLite dans le module IA |
| Fichiers Word protégés | non touchés ; `Rapport_Audit_Technique_Pressing.docx` et `~$pport_Audit_Technique_Pressing.docx` restent non suivis |
| Git | aucun commit, push, pull, merge, rebase, changement de branche ni opération distante |

---

## 2. Corrections P1–P4 et défauts révélés par le live

### P1 — Preuve textuelle et zéro invention — terminé

`TranscriptEvidenceValidator` est appliqué après `IntentValidator.Validate` dans `LlmIntentExtractor.ApplyGuardrails`. Aucune information métier n'est conservée sans transcription explicite, alias connu ou règle déterministe documentée et testée.

Le banc live a révélé un cas non couvert : sur `S071` et `S080`, le modèle a produit un `pickup_date` non conforme au format `AAAA-MM-JJ`, ce qui faisait refuser toute la réponse. `LlmIntentExtractor.TryRepairPickupDate` met désormais ce seul champ à `null` avant validation, avec avertissement ; le JSON d'origine reste dans `RawJson`. Le banc final ne compte plus aucune erreur.

Règles durcies : montant vs acompte, nom seulement si explicitement introduit, dates relatives limitées à `create_order`, quantité implicite sur `update_item_status`, `express` tri-state.

Résultat : **0 information inventée** sur le recalcul 150 scénarios et sur le banc live final de 150 appels.

### P2 — Intentions sans faux `unknown` — terminé

`IntentEvidenceGuard` désambiguïse avant la lecture des champs : `prêt`/`en cours`, montant + paiement, hésitation sans action, action explicite prioritaire, simple mention d'article non promue en commande.

Résultat : **150/150 intentions exactes** sur le recalcul et sur le banc live final ; **0 faux `unknown`**.

### P3 — Catalogue, alias et prestations par article — terminé

`CatalogMatcher` impose les libellés exacts du catalogue : pluriels irréguliers, nombres en toutes lettres, normalisation accents/ponctuation, alias ambigus désambiguïsés par la transcription.

Une correction a été nécessaire après le premier run live : `ResolveService` ne choisissait qu'une prestation globale et l'appliquait à tous les articles de `L002`. Le garde-fou utilise désormais `CatalogMatcher.ResolveServiceForItem` :

- `FindServiceMentions` repère **toutes** les prestations prononcées et leur position ;
- `FindCategoryIndex` repère la position de la catégorie de l'article ;
- la prestation retenue est la plus proche de cette catégorie ;
- si plusieurs prestations sont prononcées sans catégorie prouvée, aucune n'est inventée.

Tests de régression : `Chaque_article_garde_sa_prestation_quand_plusieurs_sont_prononcees` et `Une_date_de_retrait_invalide_est_mise_a_null_sans_refuser_la_reponse`.

Le banc rapide de comparaison a révélé un second défaut sur l'alias partagé `costard` : le garde-fou choisissait « Costume 2 pièces » alors que la transcription ne permettait pas de trancher avec « Costume 3 pièces ». `CatalogMatcher.Resolve` exige désormais un terme ou un alias **unique** pour prouver une catégorie ; la quantité reste rattachée au texte réellement prononcé (`costard`, ou `costume deux pièces` au lieu du libellé chiffré), et un nombre inclus dans le libellé n'est jamais pris pour une quantité. Tests de régression : `Une_categorie_proposee_par_le_modele_n_est_pas_prouvee_par_un_alias_ambigu`, `Un_alias_ambigu_ne_prouve_pas_la_categorie_mais_conserve_la_quantite`, `Une_categorie_chiffree_peut_etre_prononcee_en_toutes_lettres`, `Le_chiffre_du_libelle_nest_pas_une_quantite_sur_changement_detat`. Le recalcul `live-20261127-final-150-v3.*` confirme **150/150 intentions, 85/85 articles, 1840/1840 champs, 0 invention, 0 champ sans preuve** ; le JSON brut figé du 27/11 n'a pas été modifié.

Résultat final : **85/85 articles exacts** sur le recalcul et sur le banc live final.

### P4 — Prompt, vocabulaire et tests — terminé

- `update-vocab.ps1` a été **réellement réexécuté** avec l'API active : `20 catégories`, `5 services`, **74 termes + contexte** pour Whisper et **49 synonymes** dans `catalogue.json`.
- Ordre du prompt STT retenu : termes plats d'abord, bloc contextuel après. L'ordre inverse reproduisait l'erreur « la vétroix ».
- Les synonymes injectés au LLM ont été **curatés** : un service ne reçoit que ses synonymes réellement prononcés. Le prompt Whisper reste riche ; le bruit LLM est réduit sans retirer de règle métier.
- `vocab-default.txt` a été aligné sur le fichier généré, puis `sync-ressources.ps1` exécuté.
- Jeu ciblé `dataset-fiabilite.jsonl` : **53 scénarios** `F001`–`F053`, validés ; banc synthétique figé : **150 scénarios**, validé.
- Tests créés/étendus : `CatalogMatcherTests`, `TranscriptEvidenceValidatorTests`, `IntentEvidenceGuardTests`, `LlmIntentExtractorTests`, `LocalAssistantPipelineTests`, `AssistantRulesTests`.

---

## 3. Compatibilité M1/M2 finalisée

Aucun changement de Maël n'était présent dans le dépôt : ni `AssistantIntentDto`, ni `AssistantUnavailableException`, ni route `/api/assistant`, ni façade `IsReadyAsync`. La proposition communiquée a donc été intégrée côté M1 sans créer de fonctionnalité M2.

| Contrat exposé | Signature exacte | Emplacement |
|---|---|---|
| `ISpeechToText` | `Task<SpeechTranscription> TranscribeAsync(Stream wav, CancellationToken cancellationToken = default)` ; `Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)` | `src/Pressing.Core/Assistant/ISpeechToText.cs` |
| `IIntentExtractor` | `Task<IntentExtraction> ExtractAsync(string texte, CatalogHints catalog, CancellationToken cancellationToken = default)` ; `Task<bool> IsReadyAsync(...)` ; `Task<AssistantIntentDto> ExtractAsync(string texte, AssistantCatalogHints hints, CancellationToken ...)` | `src/Pressing.Core/Assistant/IIntentExtractor.cs` |
| `IM2SpeechToText` | `Task<string> TranscribeAsync(Stream wav, CancellationToken ct = default)` ; `Task<bool> IsReadyAsync(CancellationToken ct = default)` | `src/Pressing.Core/Assistant/AssistantM2Contracts.cs` |
| `IM2IntentExtractor` | `Task<AssistantIntentDto> ExtractAsync(string text, AssistantCatalogHints hints, CancellationToken ct = default)` ; `Task<bool> IsReadyAsync(CancellationToken ct = default)` | `src/Pressing.Core/Assistant/AssistantM2Contracts.cs` |
| `CatalogHints` | `(IReadOnlyList<CatalogTerm>, IReadOnlyList<CatalogTerm>, DateOnly)` ; `(IReadOnlyList<string>, IReadOnlyList<string>)` ; `(IEnumerable<string>, IEnumerable<string>, DateOnly)` | `src/Pressing.Core/Assistant/AssistantContracts.cs` |
| `AssistantCatalogHints` | `(IReadOnlyList<string> Categories, IReadOnlyList<string> Services)` | `src/Pressing.Core/Dtos/AssistantDtos.cs` |
| `AssistantIntentDto` | `Intent`, `Client`, `Items`, `Ticket`, `Amount`, `PaymentMethod`, `Deposit`, `PickupDate`, `ItemStatus`, `Confidence`, `Transcript` ; mêmes noms JSON snake_case | `src/Pressing.Core/Dtos/AssistantDtos.cs` |
| `AssistantUnavailableException` | `sealed class ... : AssistantException` | `src/Pressing.Core/Assistant/AssistantM2Contracts.cs` |
| `M2SpeechToTextAdapter` | `M2SpeechToTextAdapter(string text, bool ready = true)` | `src/Pressing.Core/Assistant/AssistantM2Contracts.cs` |
| `M2IntentExtractorAdapter` | `M2IntentExtractorAdapter(IIntentExtractor inner)` | `src/Pressing.Core/Assistant/AssistantM2Contracts.cs` |

`WhisperSpeechToText`, `LlmIntentExtractor`, `FakeSpeechToText` et `FakeIntentExtractor` implémentent les deux familles d'interfaces. `LlmIntentExtractor` applique **les mêmes garde-fous** avant de mapper vers `AssistantIntentDto`. `LocalAssistantPipeline` traite `AssistantUnavailableException` comme `EngineUnavailable`. `AssistantClientFactory` expose `CreateM2SpeechToText()` et `CreateM2IntentExtractor()`. Les deux adaptateurs permettent d'interopérer sans créer de DTO concurrent ; `M2SpeechToTextAdapter` est un adaptateur de texte configuré, pas un moteur de transcription réel.

La forme M2 `AssistantCatalogHints` est volontairement réduite : elle ne transporte ni synonymes ni date de référence. Les décisions sensibles aux alias et aux dates relatives doivent continuer à utiliser `CatalogHints` complet. Aucune route, aucun contrôleur, aucun audit, aucun rate limiting et aucune opération métier n'ont été ajoutés.

---

## 4. Architecture finale du garde-fou

```text
Transcription                 Catalogue local (lecture seule via API)
      │                                      │
      ▼                                      ▼
LlmIntentExtractor ──► réparation date ──► IntentValidator.Validate
                                               │
                                               ▼
                                  TranscriptEvidenceValidator
                                     preuve, montants, client, date,
                                     service par article
                                               │
                                               ▼
                                      IntentEvidenceGuard
                                               │
                                               ▼
                                  Revalidation du payload assaini
                                               │
                                               ▼
                                        IntentExtraction
```

`LocalAssistantPipeline` fonde le statut sur l'**état final** du payload, plus sur un avertissement historique. Une valeur normalisée depuis la transcription reste exploitable et est journalisée.

---

## 5. Résultats

### 5.1 Banc live final — 150 appels réels

Commande :

```powershell
dotnet tools\assistant\eval\EvaluationHarness\bin\Debug\net10.0\EvaluationHarness.dll `
  tools\assistant\eval\dataset-synthetique.jsonl `
  tools\assistant\eval\resultats\live-20261127-final-150-v2 'live-20261127-final-150-v2'
```

Fichiers : `eval/resultats/live-20261127-final-150-v2.{jsonl,resume.json,resume.md}`. Généré le 27 novembre 2026 à 22:13:39 +01:00.

| Mesure | Sortie brute du modèle | Après garde-fous C# |
|---|---:|---:|
| Intentions exactes | 146/150 (97,33 %) | **150/150 (100 %)** |
| Articles exacts | 39/85 (45,88 %) | **85/85 (100 %)** |
| Catégorie / service / quantité / `express` | 68 / 55 / 85 / 80 sur 85 | **85 / 85 / 85 / 85** |
| Champs exacts | 1687/1840 (91,68 %) | **1840/1840 (100 %)** |
| Valeurs `null` correctes | 1168/1185 (98,57 %) | **1185/1185 (100 %)** |
| Transcription exacte | 85/150 (56,67 %) | **150/150 (100 %)** |
| Schéma valide | 147/150 (98,0 %) | **150/150 (100 %)** |
| Informations inventées | 17 | **0** |
| Champs sans preuve | 0 | **0** |
| Hors catalogue / faux `unknown` | 0 / 0 | **0 / 0** |
| `unknown` exacts | 16/20 (80,0 %) | **20/20 (100 %)** |
| Valeurs normalisées via alias | 0 | **48** |
| Erreurs d'exécution | — | **0** |

Traçabilité du défaut corrigé : le run précédent `live-20261127-final-150.*` avait donné **148/150 intentions** avec **2 erreurs** (`S071`, `S080`) dues à un `pickup_date` invalide. La réparation ciblée décrite en §2 a été ajoutée, puis le banc complet a été relancé : **150/150, 0 erreur**. Les anciens fichiers sont conservés comme historique.

### 5.2 Recalcul hors ligne — 150 sorties historiques

Commande : `EvaluationHarness --recompute-from-jsonl avant-1.5b.jsonl apres-correction-m1-v2`. Fichiers `eval/resultats/apres-correction-m1-v2.*` ; les anciens `apres-correction-m1.*` restent historiques.

| Mesure | Brut 1,5B historique | Après correction (recalcul) |
|---|---:|---:|
| Intentions exactes | 132/150 (88,0 %) | **150/150 (100 %)** |
| Articles exacts | 49/85 (57,6 %) | **85/85 (100 %)** |
| Champs exacts | 1650/1840 (89,7 %) | **1840/1840 (100 %)** |
| Valeurs `null` correctes | 1159/1185 (97,8 %) | **1185/1185 (100 %)** |
| Informations inventées | 26 | **0** |
| Erreurs | 0 | **0** |

Ce recalcul valide la couverture des garde-fous sur les cas historiquement fautifs. Il ne remplace pas le banc live du §5.1.

### 5.3 Run live — six scénarios du script

Commande : `test-llm.ps1 -InclureGrammaire -InclureAudioPipeline -RepetitionsLatence 4`. Log : `%TEMP%\opencode\test-llm-curated.log`.

**Sortie brute** : **2/6 conformes**, **6 écarts**, **4 limites**, `TEST REEL : NON CONFORME`.

| Scénario | Écart brut |
|---|---|
| L001 | `items[1].service = null` au lieu de `Lavage` |
| L002 | `client.name = Marie` ; `items[1].category = Pantalons` |
| L003 | `items[0].service = Lavage + repassage` au lieu de `Lavage` |
| L005 | `client.name = N/A` au lieu de `null` |

**Après garde-fous** (`live-20261127-curated-apres-garde-fous.*`) : **6/6 intentions**, **8/8 articles**, **87/92 champs**, **0 information inventée**, **0 hors catalogue**, **2 avertissements de service normalisé**. Le premier rejeu intermédiaire avait donné 7/8 à cause d'un accent littéral corrompu dans le fichier temporaire PowerShell 5.1 ; après génération par `U+00E0`, le résultat est 8/8. Ce défaut concernait le banc temporaire, pas le produit.

Le contrôle audio du script ne vérifie qu'`items[0]`. La transcription Whisper de la phrase TTS est désormais exacte et le rejeu complet de cette phrase donne 2/2 articles exacts.

### 5.4 Whisper — mesures TTS séparées

| Phrase | Base, WER brut | Contextuel v1 | Contextuel v2 final |
|---|---:|---:|---:|
| Laver trois chemises + un pantalon, Astride | 30,8 % — 1,130 s | 0,0 % — 1,23 s | **0,0 % — 0,893 s** |
| 2 chemises à laver, 3 pantalons à repasser, 1 cravate | 38,9 % — 1,138 s | 22,2 % — 1,30 s | **22,2 % — 0,983 s** |
| Chemise en express | 33,3 % — 1,011 s | 25,0 % — 1,19 s | **25,0 % — 0,846 s** |

WER brut moyen : **34,3 % → 15,7 %** ; WER normalisé consigné par le banc contextuel : **0,0 %** sur les trois phrases. Les erreurs « la vétroix » et « Stride » ont disparu. Ces mesures restent **synthétiques** : aucun WER réel n'est publié.

### 5.5 Latence live

| Source | Moyenne | p50 | p95 | Remarque |
|---|---:|---:|---:|---|
| Harnais final, 150 appels | 9,37 s | 9,88 s | **11,886 s** | catalogue 62,9 ms, prompt 7,2 ms, validation 0,032 ms |
| Banc 150 appels historique | 11,26 s | 9,16 s | 12,857 s | référence antérieure |
| Script live, 10 appels | 12,75 s | — | 23,67 s | min 7,75 s, max 23,67 s |
| Harnais, 6 réponses capturées | 12,95 s | 10,29 s | 23,67 s | post-traitement moyen 13,07 ms |

Le p95 final sur 150 appels est **11,886 s**, en baisse par rapport aux 12,857 s historiques mais toujours très au-dessus de l'objectif `< 5 s`. La génération LLM sur CPU reste dominante ; le premier appel après redémarrage coûte encore ~11 s de préparation de prompt.

**Diagnostic `max_tokens`** : six appels live avec `max_tokens=256` ont donné `finish_reason=stop` et un JSON valide 6/6 ; jetons `186, 233, 150, 145, 143, 112` (max 233), sans troncature sur cet échantillon. La production reste à **512** faute de banc 150 complet à 256. `192` avait été écarté pour troncature.

### 5.6 Comparaisons `1,5B/3B × base/small` — 150 scénarios texte

Commande : `tools\assistant\comparer-modeles.ps1 -Threads 4`. Le script vérifie le PID et le fichier de modèle réellement lancé, puis restaure `1,5B`/`base` à la fin (confirmé : `qwen2.5-1.5b-instruct-q4_k_m.gguf` + `ggml-base.bin`, `/health=ok`). Le premier run `3B + base` avait produit 9 erreurs `503 Loading model` ; le script attend désormais une complétion minimale réussie et le couple a été relancé intégralement sans erreur. Détail complet : `RESULTATS.md` §7.

| Couple | Final : intentions / articles / champs | Brut : intentions / articles | p95 texte | STT moyen (3 TTS) | Erreurs |
|---|---:|---:|---:|---:|---:|
| 1,5B + base | **150/150 · 85/85 · 1840/1840** | 146/150 · 43/85 | **11,381 s** | 1,350 s | 0 |
| 1,5B + small | **150/150 · 85/85 · 1840/1840** | 146/150 · 46/85 | 11,335 s | 4,629 s | 0 |
| 3B + base | 150/150 · 85/85 · 1833/1840 | 144/150 · 64/85 | 14,174 s | 1,327 s | 0 |
| 3B + small | 150/150 · 85/85 · 1833/1840 | 142/150 · 64/85 | 14,174 s | 4,620 s | 0 |

Le WER normalisé est **0,0 %** et le WER brut **15,7 %** pour les quatre couples : les trois phrases TTS sont identiques et aucun enregistrement client n'est disponible. Le p95 est mesuré sur la chaîne texte, sans STT.

**Décision : défaut inchangé.** Le `3B` est plus lent (≈ +2,8 s de p95) et perd 7 champs sur 1840 (noms de clients), sans gain après garde-fous. `small` triple la latence STT sans améliorer le WER. Le `0,5B` reste écarté pour perte de fiabilité. Aucune mesure synthétique ne justifie de remplacer `1,5B`/`base` ni de réduire `max_tokens` à 256.

---

## 6. Validation technique

| Contrôle | Commande | Résultat réel |
|---|---|---|
| Build solution | `dotnet build PressingApp.sln -v minimal` | **0 avertissement, 0 erreur** |
| Build harnais | `dotnet build tools\assistant\eval\EvaluationHarness\EvaluationHarness.csproj` | **0 avertissement, 0 erreur** |
| Tests IA | `dotnet test tests/Pressing.Tests/Pressing.Tests.csproj --filter FullyQualifiedName~Assistant` | **102/102 réussis** |
| Tous les tests | `dotnet test PressingApp.sln --no-build -v minimal` | **248/248 réussis** |
| Syntaxe PowerShell 5.1 | `Parser::ParseFile` sur tous les `.ps1` hors `bin`/`obj`/`.git` | **17 scripts, 0 invalide** |
| Banc synthétique | `valider-dataset.ps1` | **150 lignes valides**, `S001`–`S150` |
| Banc fiabilité | `valider-dataset.ps1 -Dataset dataset-fiabilite.jsonl -Count 53 -Prefix F` | **53 lignes valides** |
| Recalcul historique | `EvaluationHarness --recompute-from-jsonl ... apres-correction-m1-v2` | **150/150 intentions, 85/85 articles** |
| Banc live final | `EvaluationHarness ... dataset-synthetique.jsonl live-20261127-final-150-v2` | **150/150 intentions, 85/85 articles, 0 erreur** |
| Comparaisons 4 modèles | `comparer-modeles.ps1 -Threads 4` (+ `-Only '3B-base'` après correction du chargement) | **4 × 150 scénarios, 0 erreur, défaut restauré** |
| Synchronisation | `sync-ressources.ps1` | prompt/grammaire/schéma/vocabulaire identiques octet pour octet |
| Ports et santé | `/health`, `/props`, TCP, HTTP 5181 | `8081`, `8082`, `5180`, `5181` ouverts ; `llm=ok`, `stt=ok` ; prompt contextuel présent dans la ligne de commande Whisper |
| Hashs | `Get-FileHash` | prompt `E42270B32FEF0BFE`, grammaire `3E94DBC9B2493515`, schéma `BB722E2093362759`, vocabulaire `44B8962ECDBAF2FD` |

Les tests unitaires utilisent des fakes HTTP/STT : aucun moteur n'est appelé. Les seuls appels réels passent par `127.0.0.1:8081` et `127.0.0.1:8082`.

---

## 7. Statut par objectif

| Objectif | Statut | Précision |
|---|---|---|
| P1 preuve textuelle / zéro invention | **TERMINÉ** | 0 invention sur recalcul et banc live 150 ; date invalide réparée |
| P2 intentions exactes | **TERMINÉ** | 150/150 recalcul et 150/150 live final |
| P3 catalogue / alias / services par article | **TERMINÉ** | 85/85 recalcul et 85/85 live final |
| P4 prompt / vocabulaire / tests | **TERMINÉ** | vocabulaire réellement régénéré ; 102 tests IA |
| T1.6 consultation en lecture seule | **DÉMONTRÉ** | `consultation.ps1` affiche la réponse et le brouillon JSON ; aucune base lue, aucune commande créée |
| Seuil ≥ 90 % intentions | **ATTEINT** | 150/150 live final après garde-fous |
| Seuil ≥ 85 % articles | **ATTEINT** | 85/85 live final après garde-fous |
| Aucune information inventée | **ATTEINT** | 0 sur recalcul et banc live final |
| Contrat JSON | **ATTEINT** | 150/150 sur le banc live final et le recalcul |
| Chaîne TTS → Whisper → LLM → garde-fous | **VÉRIFIÉE sur synthétique** | transcription exacte sur la phrase testée ; non extrapolée au client réel |
| p95 < 5 s | **NON ATTEINT** | 11,886 s sur 150 appels live |
| WER réel | **NON MESURÉ** | aucun enregistrement client avec consentement |
| Banc officiel `dataset.jsonl` | **NON MESURÉ** | fichier absent ; aucun résultat simulé. Le validateur accepte désormais `-Source reel -RequireAudio` et vérifie les 150 WAV |
| Comparaison base/small, 1,5B/3B | **TERMINÉ** | 4 couples × 150 scénarios ; défaut `1,5B`/`base` conservé (§5.6) |
| `max_tokens=256` en production | **NON ADOPTÉ** | diagnostic 6/6 sans troncature, mais pas de banc 150 à 256 |

---

## 8. Limites conservées

1. La sortie **brute** du LLM reste imparfaite : sur le banc live final, 146/150 intentions, 39/85 articles et 17 informations inventées avant garde-fous. La fiabilité métier est obtenue par les garde-fous C#, pas en attribuant au modèle une précision qu'il n'a pas.
2. Le script de six scénarios conserve 2 avertissements de service normalisé ; les valeurs finales sont correctes et dans le catalogue.
3. La latence est dominée par la génération CPU du 1,5B. Le 0,5B n'est pas réintroduit comme défaut : il sacrifie la fiabilité métier. Le 3B est plus lent (p95 14,174 s contre 11,381 s) et perd 7 champs sur 1840 ; `small` triple la latence STT sans gain de WER.
4. Le prompt Whisper contextuel corrige les erreurs de décodage sur trois phrases TTS ; cela ne constitue pas un WER officiel.
5. Le script audio ne contrôle qu'un seul article : son message de validation ne prouve pas les six scénarios bruts.
6. `dataset.jsonl` est absent ; `bench.ps1` s'arrête sans écrire de résultat simulé.
7. Aucun commit ni opération distante ; les changements restent locaux.

---

## 9. Fichiers du lot M1

**Noyau (`src/Pressing.Core/Assistant/`)** : `AssistantContracts.cs`, `AssistantM2Contracts.cs`, `ConsultationReply.cs`, `CatalogMatcher.cs`, `TranscriptNumbers.cs`, `TranscriptEvidenceValidator.cs`, `IntentEvidenceGuard.cs`, `LlmIntentExtractor.cs`, `LocalAssistantPipeline.cs`, `AssistantOptions.cs`, `AssistantClientFactory.cs`, `ISpeechToText.cs`, `IIntentExtractor.cs`, `FakeSpeechToText.cs`, `FakeIntentExtractor.cs`.

**DTO officiel M2 (`src/Pressing.Core/Dtos/`)** : `AssistantDtos.cs`.

**Tests (`tests/Pressing.Tests/Unit/Assistant/`)** : `CatalogMatcherTests.cs`, `TranscriptEvidenceValidatorTests.cs`, `IntentEvidenceGuardTests.cs`, `LlmIntentExtractorTests.cs`, `LocalAssistantPipelineTests.cs`, `AssistantRulesTests.cs`, `FakeAssistantTests.cs`, `M2CompatibilityTests.cs`, `ConsultationTests.cs`.

**Outils et bancs (`tools/assistant/`)** : `prompt-systeme.txt`, `grammar.gbnf`, `intent-schema.json`, `update-vocab.ps1`, `vocab-default.txt`, `sync-ressources.ps1`, `consultation.ps1`, `comparer-modeles.ps1`, `bench-stt.ps1`, `test-llm.ps1`, `eval/EvaluationHarness/Program.cs`, `eval/dataset-fiabilite.jsonl`, `eval/resultats/live-20261127-final-150-v2.*` (brut figé), `eval/resultats/live-20261127-final-150-v3.*` (recalcul après correction de l'alias ambigu), `eval/resultats/live-20261127-curated*.{jsonl,resume.json,resume.md}`, `eval/resultats/apres-correction-m1-v2.*`, `eval/resultats/comparaisons/*`, `eval/latency/stt-*.jsonl`, `RESULTATS.md`, `eval/README.md`, `RAPPORT_FINAL_M1.md`.

**Non modifiés** : `dataset-synthetique.jsonl`, `avant-1.5b.*`, `apres-0.5b.*`, `grammar.gbnf`, `intent-schema.json`, et les deux fichiers Word protégés.

---

## 10. Confirmation Git finale

```text
Branche : Astride
HEAD    : baa2fd0d6159b687bdcdd61c08d99843930a6fa0 (inchangé)
git status --short :
 M .env.example
 M README.md
 M docs/API.md
 M scripts/start.ps1
 M scripts/stop.ps1
 M src/Pressing.Core/Pressing.Core.csproj
?? Rapport_Audit_Technique_Pressing.docx
?? src/Pressing.Core/Assistant/
?? src/Pressing.Core/Dtos/AssistantDtos.cs
?? tests/Pressing.Tests/Unit/Assistant/
?? tools/
?? ~$pport_Audit_Technique_Pressing.docx
```

Aucun commit, push, pull, merge, rebase, changement de branche ni accès au distant. Aucun fichier existant supprimé.

---

## 11. Conclusion

**Bilan : lot P1–P4 terminé et chaîne M1 testée de bout en bout sur la plateforme locale.** Le banc live de 150 appels donne, après garde-fous, **150/150 intentions exactes**, **85/85 articles exacts**, **1840/1840 champs exacts**, **0 information inventée** et **0 erreur**, avec un p95 de **11,886 s**. Le recalcul historique et le recalcul `v3` après correction de l'alias ambigu confirment les mêmes 100 %. Le build, les **102 tests IA**, les **248 tests de la solution**, la syntaxe PowerShell et les deux validateurs sont propres. Les six tests M2 couvrent les signatures, le mapping, les garde-fous, la disponibilité et les adaptateurs ; trois tests couvrent la consultation T1.6 en lecture seule ; quatre tests couvrent les alias ambigus et les nombres inclus dans les libellés.

Le module reste **non déclaré opérationnel en production** : le p95 de 11,886 s dépasse largement le seuil `< 5 s`, le WER réel et le banc officiel ne sont pas mesurés. Le défaut demeure `Qwen2.5-1.5B-Instruct Q4_K_M` et `max_tokens=512`. Les quatre comparaisons de modèles sont terminées (§5.6) : le `3B` est plus lent et moins complet après garde-fous, `small` triple la latence STT sans gain de WER ; aucun changement de défaut n'est adopté.

Fichiers de référence : `RESULTATS.md`, `eval/README.md`, `eval/resultats/live-20261127-final-150-v2.resume.{json,md}`, `eval/resultats/live-20261127-final-150-v3.resume.{json,md}`, `eval/resultats/apres-correction-m1-v2.resume.{json,md}`.
