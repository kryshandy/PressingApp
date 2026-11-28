# Résultats du module IA local — Membre 1

Date des mesures finales : 27–28 novembre 2026. Machine : AMD Ryzen 5 5600U (6 cœurs / 12 threads), 16 Go RAM, **sans GPU**. Périmètre strictement M1 des mesures : aucune route/UI/DTO définitif M2, aucun WhatsApp/e-mail/rappel M3, aucun accès SQLite depuis le moteur IA. L'état Git décrit alors est celui du 27–28/11 ; les ajouts d'outillage ont été livrés ensuite sur une branche parallèle (voir ci-dessous).

État : **lot P1–P4 terminé, chaîne locale testée de bout en bout, démo T1.6 en lecture seule livrée, comparaisons de modèles terminées**. La fiabilisation est obtenue par les garde-fous C#, pas par une précision inventée du modèle brut. Un alias ambigu (`costard`) et un nombre inclus dans un libellé (`Costume 2 pièces`) ont été corrigés le 28/11 ; le recalcul `live-20261127-final-150-v3.*` confirme les mêmes 100 % sur le brut figé du 27/11. Les quatre couples `1,5B/3B × base/small` ont été exécutés sur 150 scénarios ; le défaut reste `1,5B`/`base` (décision motivée en §7).

Ajout livré sur la branche parallèle `feature/ia-m1-t1-complements` : `eval/dataset.jsonl` est un **modèle** `R001`–`R150` ignoré par Git, en attente de 150 WAV et de consentements explicites ; `start-ai.ps1 -Install` fournit l'installation + le lancement en une commande ; `ConcurrenceTests.cs` couvre 10 appels parallèles et la configuration `-np 1`/`-p 1`. **Aucune mesure ci-dessous n'est modifiée** : aucun WER réel ni p95 `< 5 s` n'est revendiqué.

---

## 1. Jeux et nature des mesures

| Catégorie | Fichier / source | Ce que cela mesure | Ce que cela ne mesure pas |
|---|---|---|---|
| Banc live final | `eval/resultats/live-20261127-final-150-v2.*` | 150 appels HTTP réels au LLM local + garde-fous du 27/11 | pas de WER client |
| Recalcul final v3 | `eval/resultats/live-20261127-final-150-v3.*` | mêmes 150 réponses brutes rejouées après correction de l'alias ambigu | pas un nouvel appel LLM |
| Live six scénarios | `eval/resultats/live-20261127-curated.*` | sortie brute et recalage sur 6 phrases | pas un banc 150 |
| Recalcul hors ligne | `eval/resultats/avant-1.5b.jsonl` → `apres-correction-m1-v2.*` | couverture des garde-fous sur 150 sorties brutes figées | pas un nouvel appel LLM |
| Synthétique TTS | `eval/latency/stt-{base,contextual,contextual-v2}.jsonl` | Whisper seul sur 3 WAV générés | WER client |
| Ciblé | `eval/dataset-fiabilite.jsonl` | 53 scénarios `F001`–`F053` | banc officiel |
| Non exécuté | `eval/dataset.jsonl` absent | — | aucun résultat officiel n'est écrit |
| Comparaisons terminées | `eval/resultats/comparaisons/{1.5B,3B}-{base,small}-150.*`, `eval/latency/stt-{base,small}-*.jsonl` | latence/fiabilité relative sur 150 scénarios texte + STT sur 3 phrases TTS | pas un WER client |

Le catalogue utilisé par les prompts est généré depuis `GET /api/public/catalog` : **20 catégories**, **5 services**, **49 synonymes** après curation des alias LLM. Le prompt STT contient **74 termes + un bloc contextuel** placé après les termes.

---

## 2. Banc live final — 150 appels réels

Commande :

```powershell
dotnet tools\assistant\eval\EvaluationHarness\bin\Debug\net10.0\EvaluationHarness.dll `
  tools\assistant\eval\dataset-synthetique.jsonl `
  tools\assistant\eval\resultats\live-20261127-final-150-v2 'live-20261127-final-150-v2'
```

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
| Alias normalisés | 0 | **48** |
| Erreurs d'exécution | — | **0** |

Le run précédent `live-20261127-final-150.*` avait donné 148/150 intentions et **2 erreurs** sur `S071`/`S080` : un `pickup_date` invalide faisait refuser la réponse entière. `LlmIntentExtractor.TryRepairPickupDate` met désormais ce champ à `null` avec avertissement. Le banc a été relancé intégralement après correction : **150/150, 0 erreur**. Les anciens fichiers restent en historique.

Le 28/11, le banc rapide a révélé que l'alias `costard`, partagé par « Costume 2 pièces » et « Costume 3 pièces », était résolu arbitrairement en faveur du premier libellé. `CatalogMatcher.Resolve` exige désormais un terme ou un alias unique ; la quantité est rattachée au texte prononcé (`costard`, `costume deux pièces`) et un chiffre intégré au libellé n'est plus confondu avec une quantité. Le rejeu hors ligne `live-20261127-final-150-v3.*` du brut figé donne **150/150 intentions, 85/85 articles, 1840/1840 champs, 0 invention, 0 champ sans preuve, 0 erreur**. Aucun appel LLM n'a été refait pour ce rejeu.

---

## 3. Recalcul hors ligne P1–P4 sur 150 sorties historiques

`EvaluationHarness --recompute-from-jsonl avant-1.5b.jsonl apres-correction-m1-v2`.

| Mesure | Brut | Final recalculé |
|---|---:|---:|
| Intentions exactes | 132/150 | **150/150** |
| Articles exacts | 49/85 | **85/85** |
| Champs exacts | 1650/1840 | **1840/1840** |
| `null` corrects | 1159/1185 | **1185/1185** |
| Informations inventées | 26 | **0** |
| Erreurs | 0 | **0** |

Ces 100 % proviennent d'un rejeu hors ligne ; le banc live du §2 est la mesure de référence désormais disponible.

---

## 4. Run live — six scénarios du script

Commande : `test-llm.ps1 -InclureGrammaire -InclureAudioPipeline -RepetitionsLatence 4`. Log : `%TEMP%\opencode\test-llm-curated.log`.

**Sortie brute** : **2/6 conformes**, **6 écarts**, **4 limites**, `TEST REEL : NON CONFORME` :

| Scénario | Écart brut |
|---|---|
| L001 | `items[1].service = null` au lieu de `Lavage` |
| L002 | `client.name = Marie` ; `items[1].category = Pantalons` |
| L003 | `items[0].service = Lavage + repassage` au lieu de `Lavage` |
| L005 | `client.name = N/A` au lieu de `null` |

**Après garde-fous** (`live-20261127-curated-apres-garde-fous.*`) : **6/6 intentions**, **8/8 articles**, **87/92 champs**, **0 invention**, **0 hors catalogue**, **2 avertissements de service normalisé**, **0 erreur**.

Les avertissements restants sont des services explicitement prononcés puis remis au libellé exact du catalogue. Le harnais les comptabilise comme « sans preuve » par conservatisme d'instrumentation ; les valeurs finales sont correctes.

Deux corrections ont été apportées pendant cette campagne : le service est choisi **par article** selon la position de sa catégorie (`ResolveServiceForItem`), et un `pickup_date` invalide est mis à `null` sans refuser la réponse. Le premier rejeu intermédiaire avait donné 7/8 à cause d'un `à` littéral corrompu dans le fichier temporaire PowerShell 5.1 ; après génération par `U+00E0`, le résultat est 8/8. Ce défaut concernait le banc temporaire, pas le produit.

Le contrôle audio du script ne vérifie qu'`items[0]`. La transcription Whisper de la phrase TTS est désormais exacte et le rejeu complet de cette phrase donne 2/2 articles exacts.

---

## 5. Latence

| Source | Moyenne | p50 | p95 | Détail |
|---|---:|---:|---:|---|
| Harnais final, 150 appels | 9,37 s | 9,88 s | **11,886 s** | catalogue 62,9 ms, prompt 7,2 ms, validation 0,032 ms |
| Banc 150 appels historique | 11,26 s | 9,16 s | 12,857 s | référence antérieure |
| Script live, 10 appels | 12,75 s | — | 23,67 s | min 7,75 s, max 23,67 s |
| Harnais, 6 réponses capturées | 12,95 s | 10,29 s | 23,67 s | post-traitement moyen 13,07 ms |

Le p95 final est **11,886 s** : il s'améliore par rapport à 12,857 s mais reste très au-dessus de l'objectif `< 5 s`. Le premier appel après redémarrage coûte encore ~11 s de préparation de prompt ; la génération LLM sur CPU domine ensuite.

Diagnostic `max_tokens=256` : 6 appels live, `finish_reason=stop` et JSON valide 6/6, jetons `186 / 233 / 150 / 145 / 143 / 112` (max 233), aucune troncature. **La production reste à 512** faute de banc 150 complet à 256. `192` avait été écarté pour troncature.

---

## 6. Whisper — mesures TTS

| Phrase | Base | Contextuel v1 | Contextuel v2 final |
|---|---:|---:|---:|
| Laver 3 chemises + 1 pantalon, Astride | 30,8 % / 1,130 s | 0,0 % / 1,23 s | **0,0 % / 0,893 s** |
| 2 chemises à laver, 3 pantalons à repasser, 1 cravate | 38,9 % / 1,138 s | 22,2 % / 1,30 s | **22,2 % / 0,983 s** |
| Chemise en express | 33,3 % / 1,011 s | 25,0 % / 1,19 s | **25,0 % / 0,846 s** |

WER brut moyen : **34,3 % → 15,7 %**. WER normalisé consigné par le banc contextuel : **0,0 %** sur les trois phrases. Les erreurs « la vétroix » et « Stride » ont disparu. Ces mesures restent **synthétiques** ; aucun WER réel n'est publié.

---

## 7. Comparaisons de modèles — banc complet terminé

`tools\assistant\comparer-modeles.ps1` a enchaîné les quatre couples `1,5B/3B` × `base/small` sur 150 scénarios texte, vérifié le modèle réellement lancé via le PID, puis restauré `1,5B`/`base` à la fin (vérifié en fin de run : `qwen2.5-1.5b-instruct-q4_k_m.gguf` + `ggml-base.bin`, `/health=ok`). Le premier passage `3B + base` avait donné 9 erreurs `503 Loading model` : le port `8081` s'ouvrait avant la fin du chargement du GGUF. Le script attend désormais une complétion minimale réussie (`Wait-LlmReady`) et le couple a été relancé intégralement : **0 erreur**. Les sorties sont dans `eval/resultats/comparaisons/*-150.*` et `eval/latency/stt-*.jsonl`.

Le p95 ci-dessous mesure la chaîne **texte** (fin de phrase → brouillon), sans STT. La latence STT et le WER sont mesurés séparément sur **trois phrases TTS synthétiques** ; ils ne sont pas additionnés au p95 et ne constituent pas un WER client.

| Couple | Final : intentions | Final : articles | Final : champs | Brut : intentions / articles | Schéma brut | Inventées (brut) | p95 texte | Moyenne | STT moyen (3 TTS) | WER brut / normalisé | Erreurs |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 1,5B + base | **150/150** | **85/85** | **1840/1840** | 146/150 · 43/85 | 146/150 | 17 | **11,381 s** | 9,036 s | 1,350 s | 15,7 % / 0,0 % | 0 |
| 1,5B + small | **150/150** | **85/85** | **1840/1840** | 146/150 · 46/85 | 147/150 | 17 | 11,335 s | **8,937 s** | 4,629 s | 15,7 % / 0,0 % | 0 |
| 3B + base | **150/150** | **85/85** | 1833/1840 | 144/150 · 64/85 | 148/150 | 16 | 14,174 s | 11,456 s | 1,327 s | 15,7 % / 0,0 % | 0 |
| 3B + small | **150/150** | **85/85** | 1833/1840 | 142/150 · 64/85 | 147/150 | 16 | 14,174 s | 11,438 s | 4,620 s | 15,7 % / 0,0 % | 0 |

**Décision : le défaut reste `Qwen2.5-1.5B-Instruct Q4_K_M` + Whisper `base`, avec `max_tokens=512`.**

- Après garde-fous, les quatre couples donnent 150/150 intentions et 85/85 articles ; aucun n'invente d'information ni ne laisse de champ sans preuve.
- Le `3B` est **plus lent** : p95 texte `14,174 s` contre `11,381 s` pour le défaut, soit environ +2,8 s (≈ 25 %). Il n'apporte aucun gain final : il perd **7 champs sur 1840** (noms de clients en intention `register_pickup`) là où le `1,5B` est à 1840/1840. Son intérêt est uniquement brut (64/85 articles contre 43/85), ce que les garde-fous absorbent déjà.
- Whisper `small` **multiplie la latence STT par ~3,4** (4,63 s contre 1,35 s sur les trois phrases TTS) sans améliorer le WER normalisé (0,0 % dans les deux cas) ni le WER brut (15,7 % dans les deux cas). Il n'existe aucun enregistrement client qui justifierait ce coût.
- Le `0,5B`, déjà évalué, reste écarté pour perte de fiabilité métier. Aucun changement de défaut n'est donc adopté sur la base de ce banc synthétique.

---

## 8. Validation technique finale

| Contrôle | Résultat |
|---|---|
| `dotnet build PressingApp.sln` | **0 avertissement, 0 erreur** |
| `dotnet build EvaluationHarness.csproj` | **0 avertissement, 0 erreur** |
| Tests IA (`FullyQualifiedName~Assistant`) | **102/102 réussis** |
| Tous les tests (`dotnet test PressingApp.sln --no-build`) | **248/248 réussis** |
| Syntaxe PowerShell 5.1 | **17 scripts, 0 invalide** |
| `valider-dataset.ps1` | **150 lignes valides** |
| `valider-dataset.ps1 -Count 53 -Prefix F` | **53 lignes valides** |
| Hash prompt / grammaire / schéma / vocabulaire | `E42270B32FEF0BFE` / `3E94DBC9B2493515` / `BB722E2093362759` / `44B8962ECDBAF2FD` |
| Ports et santé | `8081`, `8082`, `5180`, `5181` ouverts ; `llm=ok`, `stt=ok`, site HTTP 200 |

Les tests unitaires utilisent des fakes HTTP/STT : aucun moteur n'est appelé. Les seuls appels réels passent par `127.0.0.1:8081` et `127.0.0.1:8082`.

---

## 9. Seuils de la feuille de route

| Mesure | Objectif | État vérifiable |
|---|---:|---|
| Intentions exactes | ≥ 90 % | **ATTEINT** : 150/150 sur le banc live final après garde-fous |
| Articles exacts | ≥ 85 % | **ATTEINT** : 85/85 sur le banc live final après garde-fous |
| p95 fin de phrase → brouillon | < 5 s | **NON ATTEINT** : 11,886 s sur 150 appels live |
| Sorties conformes au contrat | 100 % | **ATTEINT** : 150/150 sur le banc live final |
| Aucune information inventée | 0 | **ATTEINT** : 0 sur le banc live final après garde-fous |
| Comparaison base/small, 1,5B/3B | décision documentée | **TERMINÉ** : 4 couples exécutés sur 150 scénarios ; défaut `1,5B`/`base` conservé (§7) |

---

## 10. Reproduire sans installer de nouveau modèle

```powershell
.\scripts\start.ps1 -SansDesktop -SansIA -SansBuild
.\tools\assistant\update-vocab.ps1 -ApiUrl http://localhost:5180
.\tools\assistant\sync-ressources.ps1
.\tools\assistant\start-ai.ps1
dotnet build PressingApp.sln -v minimal
dotnet test PressingApp.sln --no-build -v minimal
dotnet build tools\assistant\eval\EvaluationHarness\EvaluationHarness.csproj
dotnet tools\assistant\eval\EvaluationHarness\bin\Debug\net10.0\EvaluationHarness.dll `
  tools\assistant\eval\dataset-synthetique.jsonl `
  tools\assistant\eval\resultats\live-20261127-final-150-v2 'live-20261127-final-150-v2'
dotnet tools\assistant\eval\EvaluationHarness\bin\Debug\net10.0\EvaluationHarness.dll `
  --recompute-from-jsonl `
  tools\assistant\eval\resultats\live-20261127-final-150-v2.jsonl `
  tools\assistant\eval\resultats\live-20261127-final-150-v3 'live-20261127-final-150-v3'
.\tools\assistant\test-llm.ps1 -InclureGrammaire -InclureAudioPipeline -RepetitionsLatence 4
.\tools\assistant\bench-stt.ps1 -Output .\tools\assistant\eval\latency\stt-contextual-v2.jsonl
.\tools\assistant\comparer-modeles.ps1 -Threads 4
.\tools\assistant\eval\valider-dataset.ps1
.\tools\assistant\eval\valider-dataset.ps1 -Dataset .\tools\assistant\eval\dataset-fiabilite.jsonl -Count 53 -Prefix F
```

`bench.ps1` s'arrête tant que `eval/dataset.jsonl` est absent : aucun résultat simulé n'est écrit.

---

## 11. Bilan

| Lot / point | Statut |
|---|---|
| P1 — preuve textuelle et zéro invention | **TERMINÉ** |
| P2 — intentions sans faux `unknown` | **TERMINÉ** |
| P3 — catalogue, alias, service par article | **TERMINÉ** |
| P4 — prompt, vocabulaire régénéré, tests | **TERMINÉ** |
| T1.6 — consultation en lecture seule | **DÉMONTRÉ** : `consultation.ps1`, aucune base lue, aucune commande créée |
| Comparaisons 1,5B/3B et base/small | **TERMINÉ** : 4 couples × 150 scénarios, défaut `1,5B`/`base` conservé (§7) |
| Banc live 150 appels | **150/150 intentions, 85/85 articles, 0 erreur** |
| Chaîne TTS → Whisper → LLM → garde-fous | **VÉRIFIÉE sur synthétique** |
| p95 < 5 s | **NON ATTEINT** (11,886 s) |
| WER réel / banc officiel | **NON MESURÉ** |
| `max_tokens=256` en production | **NON ADOPTÉ** (diagnostic favorable, banc 150 non refait à 256) |

Le détail des commandes, fichiers, hashs et de la confirmation Git est dans `RAPPORT_FINAL_M1.md`. Le module n'est pas déclaré opérationnel en production.
