# Assistant vocal IA local — Membre 1

Rapport final A–I : [`RAPPORT_FINAL_M1.md`](RAPPORT_FINAL_M1.md). Mesures détaillées : [`RESULTATS.md`](RESULTATS.md).

Ce dossier contient uniquement le **moteur IA local** : installation des moteurs et des modèles, scripts de démarrage/arrêt, prompt, grammaire, banc d’essai et documentation. Il ne contient **aucune route API, aucun écran Desktop et aucune opération métier** : ces éléments appartiennent au Membre 2. Les rappels WhatsApp/e-mail appartiennent au Membre 3.

## État de validation

Le lot P1–P4 est terminé et la chaîne locale **TTS → Whisper → LLM → garde-fous C# → contrat JSON** a été testée de bout en bout sur des phrases synthétiques. Le vocabulaire STT est réellement régénéré depuis `GET /api/public/catalog` (20 catégories, 5 services, 74 termes + contexte, 49 synonymes LLM après curation).

Le banc live final (`eval/resultats/live-20261127-final-150-v2.*`) a effectué **150 appels réels** au moteur local : après garde-fous, **150/150 intentions exactes**, **85/85 articles exacts**, **1840/1840 champs exacts**, **0 information inventée** et **0 erreur**. La sortie brute du modèle reste à 146/150 intentions, 39/85 articles et 17 inventions ; elle n'est pas présentée comme fiable. Le p95 est de **11,886 s** : l’objectif `< 5 s` n’est pas atteint. Le banc d’essai officiel reste **non mesuré** faute de 150 enregistrements réels avec consentement.

Le 0,5B descend à 8,936 s mais fait chuter la fiabilité (intentions 76,7 % au lieu de 88,0 %, 195 informations inventées au lieu de 25) ; il n’est donc pas le défaut. Les quatre couples `1,5B/3B × base/small` ont été exécutés sur 150 scénarios le 28/11 : **150/150 intentions et 85/85 articles après garde-fous** pour tous, mais le `3B` monte le p95 à 14,174 s et perd 7 champs sur 1840, tandis que `small` triple la latence STT sans améliorer le WER. Le défaut reste donc `1,5B`/`base`, avec `max_tokens=512`. Voir `RESULTATS.md` §7 et `eval/resultats/comparaison.md` pour toutes les mesures et les limites non masquées.

## Périmètre et contrat

Le contrat JSON est celui de la section 4.1 de la feuille de route v3. Les types M1 restent dans `src/Pressing.Core/Assistant/` (`IntentPayload`, `IntentClient`, `IntentItem`). Pour le contrat T2.1 de Maël, le DTO officiel est désormais `Pressing.Core.Dtos.AssistantIntentDto` : c'est le seul DTO M2 à utiliser dans les routes `/api/assistant/*`. Le mapping explicite `ToDto()` / `FromDto()` conserve les mêmes noms JSON et tous les champs ; aucun DTO concurrent supplémentaire n'est à créer.

Règles fortes :

- les deux serveurs écoutent uniquement sur `127.0.0.1` : LLM `8081`, STT `8082` ;
- l’IA ne touche jamais SQLite : elle reçoit un texte et des indices de catalogue, puis renvoie une intention structurée ;
- aucun champ inconnu n’est inventé : la valeur est `null` ;
- `express` vaut `true` seulement sur mention explicite, `false` seulement sur refus explicite, `null` sinon ; jamais `false` par défaut ;
- confiance < `0,6` ou intention `unknown` ⇒ message `je n'ai pas compris` + transcription ;
- catégories et services sont vérifiés contre le catalogue réel via `GET /api/public/catalog` (lecture seule) ;
- l’audio n’est pas conservé : le client C# le transmet en mémoire, sans fichier temporaire ; les enregistrements du jeu d’évaluation sont stockés hors dépôt, avec consentement ;
- les silences sont détectés avant inférence et la durée est limitée à 20 secondes ;
- une seule inférence à la fois (file d’attente portée par le serveur llama.cpp `-np 1` et le serveur whisper.cpp `-p 1`), vérifiée par `ConcurrenceTests.cs` ;
- aucun modèle, audio ni secret n’est commité.

Les garde-fous appliqués dans `LlmIntentExtractor`, indépendamment du modèle, couvrent : recopie de la transcription d’origine, tri-state `express`, vidage des champs sur `unknown`, preuve textuelle des valeurs, désambiguïsation des intentions, réparation d'un `pickup_date` invalide, affectation du service **par article** selon la position de sa catégorie dans la transcription, et refus des alias ambigus non prouvés. Un nombre intégré au libellé d'une catégorie (`Costume 2 pièces`) n'est jamais confondu avec la quantité prononcée. Le Membre 2 doit consommer `IntentExtraction.Payload`, pas `RawJson`.

## Artefacts épinglés

Vérifiés le 27 novembre 2026 auprès des API GitHub et Hugging Face. Les empreintes sont celles publiées par les sources officielles ; le script `install.ps1` les revérifie et échoue si elles ne correspondent pas.

| Artefact | Version épinglée | SHA-256 officiel |
|---|---|---|
| `llama-b12810-bin-win-cpu-x64.zip` | llama.cpp `b12810` | `845e25154ae4051659cf7363908a663cf15f99c57f8d5157d0f38f468acf660d` |
| `whisper-bin-x64.zip` | whisper.cpp `b5907` | `ecca07b277d943e9b9f4712856a144b857271a6b333104e511e1988e80db582b` |
| `ggml-base.bin` | ggerganov/whisper.cpp | `60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe` |
| `ggml-small.bin` (option, installé et vérifié le 28/11) | ggerganov/whisper.cpp | `1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b` |
| `qwen2.5-0.5b-instruct-q4_k_m.gguf` | Qwen2.5-0.5B-Instruct-GGUF | `74a4da8c9fdbcd15bd1f6d01d621410d31c6fc00986f5eb687824e7b93d7a9db` |
| `qwen2.5-1.5b-instruct-q4_k_m.gguf` | Qwen2.5-1.5B-Instruct-GGUF | `6a1a2eb6d15622bf3c96857206351ba97e1af16c30d7a74ee38970e434e9407e` |
| `qwen2.5-3b-instruct-q4_k_m.gguf` (option, installé et vérifié le 28/11) | Qwen2.5-3B-Instruct-GGUF | `626b4a6678b86442240e33df819e00132d3ba7dddfe1cdc4fbb18e0a9615c62d` |

Modèle par défaut : **Qwen2.5-1.5B-Instruct en Q4_K_M** (≈ 1,1 Go, licence Apache 2.0). Le 0,5B (≈ 491 Mo) a été téléchargé, vérifié puis évalué réellement sur les 150 scénarios : il réduit la latence mais fait chuter la précision. Conformément à la règle « ne jamais sacrifier la fiabilité métier pour atteindre 5 secondes », il n’est **pas** le modèle par défaut ; il reste disponible avec `-LlmModel 0.5B` pour expérimentation. Le repli 3B et Whisper `small` sont disponibles avec `-LlmModel 3B` / `-WhisperModel small` pour comparaison ; ils ne remplacent pas le défaut.

Pour whisper.cpp, la release « latest » `v1.9.6` ne publie aucun asset Windows. Le build par commit `b5907` est donc épinglé : son archive Windows possède une empreinte officielle dans l’API GitHub. Le même raisonnement s’applique à llama.cpp : la release « latest » ne publie qu’un fichier de tag, alors que le build `b12810` publie l’archive CPU Windows avec son digest.

## Commandes

Depuis la racine du dépôt, en PowerShell :

```powershell
.\tools\assistant\start-ai.ps1 -Install               # poste neuf : installe puis lance 8081 + 8082 en une commande
.\tools\assistant\install.ps1                         # install vérifié seul (1,5B + base)
.\tools\assistant\install.ps1 -LlmModel 0.5B -WhisperModel base
.\tools\assistant\install.ps1 -LlmModel 3B -WhisperModel small     # comparaisons T1.4
.\tools\assistant\update-vocab.ps1 -ApiUrl http://localhost:5180  # catalogue réel -> STT + LLM
.\tools\assistant\sync-ressources.ps1                 # prompt/grammaire/schéma/vocabulaire du dépôt -> copie locale
.\tools\assistant\start-ai.ps1                        # 127.0.0.1:8081 et 127.0.0.1:8082
.\scripts\start.ps1                                   # applications + IA
.\scripts\start.ps1 -SansIA                           # applications sans IA
.\scripts\start.ps1 -SansDesktop -SansIA              # API + site uniquement
.\scripts\stop.ps1                                    # applications + IA
.\tools\assistant\stop-ai.ps1                         # IA uniquement
.\scripts\build.ps1 -Tests                            # compilation + tests xUnit
```

Les fichiers sont rangés dans `%LOCALAPPDATA%\Pressing\ai\` (`llama`, `whisper`, `models`, `logs`, PID, vocabulaires). `start.ps1` et `stop.ps1` appellent les scripts IA uniquement si `ASSISTANT_ENABLED` n’est pas `false` et si `-SansIA` n’est pas passé. `update-vocab.ps1` construit les alias accentués par codes Unicode afin que PowerShell 5.1 ne corrompe pas `repassé` et `dépôt`. Son `--prompt` Whisper contient les termes du catalogue **puis** un bloc contextuel court : cet ordre est nécessaire pour éviter que Whisper `base` decode « faire laver trois » en « faire la vétroix ». La grammaire livrée impose un JSON compact sans espaces ni retours à la ligne : cela réduit les jetons générés sans retirer aucun champ du contrat.

## Tests disponibles

```powershell
# Unitaires : aucun serveur IA requis, fakes HTTP/STT.
dotnet test tests/Pressing.Tests/Pressing.Tests.csproj --filter 'FullyQualifiedName~Assistant'  # 104 tests M1/M2, dont ConcurrenceTests

# Tous les tests C#.
dotnet test PressingApp.sln --no-build

# Réel : exige llama.cpp 8081 et whisper.cpp 8082 déjà démarrés.
.\tools\assistant\test-llm.ps1
.\tools\assistant\test-llm.ps1 -InclureGrammaire -InclureAudioPipeline -RepetitionsLatence 4

# Comparaisons 1,5B/3B et base/small (apres install.ps1 -LlmModel 3B -WhisperModel small).
.\tools\assistant\comparer-modeles.ps1 -Threads 4 -Quick   # 53 scenarios
.\tools\assistant\comparer-modeles.ps1 -Threads 4          # 150 scenarios

# T1.6 : consultation en lecture seule (aucune base lue, aucune commande créée).
.\tools\assistant\consultation.ps1 -Texte "Où en est le ticket 142 ?"
.\tools\assistant\consultation.ps1 -Wav "C:\temp\question.wav"

# Banc officiel réel : s'arrête sans 150 WAV ni consentements, n'écrit aucun résultat simulé.
.\tools\assistant\bench.ps1

# Banc vocal TTS sans enregistrement humain (solution retenue, NON réel).
.\tools\assistant\eval\generer-dataset-tts.ps1
.\tools\assistant\eval\valider-dataset.ps1 -Dataset .\tools\assistant\eval\dataset-tts.jsonl -Source tts -Prefix T -RequireAudio -AudioRoot .\tools\assistant\eval
.\tools\assistant\bench.ps1 -Dataset .\tools\assistant\eval\dataset-tts.jsonl -AudioRoot .\tools\assistant\eval -Source tts -Output .\tools\assistant\eval\resultats\banc-tts-150.md -RawDump .\tools\assistant\eval\resultats\banc-tts-150-brut.jsonl
dotnet tools\assistant\eval\EvaluationHarness\bin\Debug\net10.0\EvaluationHarness.dll --recompute-from-jsonl tools\assistant\eval\resultats\banc-tts-150-brut.jsonl tools\assistant\eval\resultats\banc-tts-150-apres-garde-fous 'banc-tts-150-apres-garde-fous'

# Latence séparée du LLM (prompt / génération / HTTP / tokens) et de Whisper.
.\tools\assistant\bench-latency.ps1 -Output .\tools\assistant\eval\latency\mesure.jsonl -Count 6
.\tools\assistant\bench-stt.ps1 -Output .\tools\assistant\eval\latency\stt-contextual-v2.jsonl

# Banc texte sur 150 scénarios SYNTHÉTIQUES (aucun audio, aucun WER).
.\tools\assistant\eval\valider-dataset.ps1
dotnet build tools\assistant\eval\EvaluationHarness\EvaluationHarness.csproj
dotnet tools\assistant\eval\EvaluationHarness\bin\Debug\net10.0\EvaluationHarness.dll `
  tools\assistant\eval\dataset-synthetique.jsonl `
  tools\assistant\eval\resultats\live-20261127-final-150-v2 'live-20261127-final-150-v2'
```

`test-llm.ps1` envoie six phrases avec le prompt du dépôt, le catalogue local et `response_format=json_schema`. Il vérifie HTTP, JSON, schéma, valeurs attendues, catalogue, valeurs `null`, intentions `unknown` et absence d’invention de `express`. Il consigne séparément les écarts bruts du modèle et les limites STT. Son verdict brut n'est pas le verdict métier final : celui-ci s'obtient avec `EvaluationHarness --recompute-from-jsonl` ou le banc live complet. Le pipeline audio utilise une synthèse vocale Windows, supprime le WAV temporaire en fin de traitement et ne contrôle qu'un seul article. Ne pas confondre ces 6 scénarios avec le banc officiel de 150 enregistrements.

## Points d’intégration pour le Membre 2

- `ISpeechToText.TranscribeAsync(Stream wav, CancellationToken)` reçoit un WAV 16 kHz mono PCM 16 bits et renvoie `SpeechTranscription` (`Text`, `DurationSeconds`, `Rms`, `SilenceDetected`) ; `IsReadyAsync(CancellationToken)` est disponible.
- `IIntentExtractor.ExtractAsync(string texte, CatalogHints catalog, CancellationToken)` renvoie `IntentExtraction` (`RawJson`, `Payload`, `Warnings`) ; `IsReadyAsync(CancellationToken)` et la surcharge M2 `ExtractAsync(string text, AssistantCatalogHints hints, CancellationToken)` sont disponibles.
- `IM2SpeechToText.TranscribeAsync(Stream wav, CancellationToken)` renvoie `Task<string>`, et `IM2IntentExtractor.ExtractAsync(string text, AssistantCatalogHints hints, CancellationToken)` renvoie `Task<AssistantIntentDto>`. `M2SpeechToTextAdapter` et `M2IntentExtractorAdapter` permettent de les consommer dans le pipeline M1. `AssistantUnavailableException` est prise en charge par le pipeline comme indisponibilité moteur. Ces façades n'ajoutent aucune route ni aucune opération métier.
- `LocalAssistantPipeline.ProcessAsync(Stream wav, CatalogHints catalog, CancellationToken)` enchaîne les deux et renvoie `PipelineResult` avec un `PipelineStatus` (`Ok`, `NotUnderstood`, `Silence`, `EngineUnavailable`, `InvalidAudio`). Le backend doit transformer le brouillon en commande via les routes existantes, jamais directement.
- `AssistantClientFactory` construit les clients HTTP avec les options lues dans l’environnement ; `CreateM2SpeechToText()` et `CreateM2IntentExtractor()` exposent les façades M2 ; aucun accès SQLite.
- `CatalogHints` est fourni par l’appelant : catégories, services, synonymes et date de référence. La forme M2 `AssistantCatalogHints(Categories, Services)` est réduite : elle ne transporte ni synonymes ni date figée. Pour les décisions sensibles aux dates et aux alias, utiliser le pipeline M1 complet. `update-vocab.ps1` propose un exemple de récupération via `GET /api/public/catalog`.

## Divergence de documentation

`CONTRIBUTING.md` attribue actuellement les notifications WhatsApp/e-mail au « membre 1 ». La feuille de route v3 les attribue au **Membre 3**. Ce dossier suit la feuille de route v3 et n’a pas modifié `CONTRIBUTING.md` (fichier du périmètre du Membre 5). À trancher par l’équipe.
