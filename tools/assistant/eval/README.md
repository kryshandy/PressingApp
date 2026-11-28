# Jeu d'évaluation — bancs et mesures M1

Trois jeux de données figés et quatre catégories de mesures sont documentés ici. Ils ne doivent pas être confondus.

## Jeux de données

1. `dataset.jsonl` : **jeu réel attendu**, avec audio enregistré avec consentement. Il est absent de ce dépôt. Aucun résultat n'est produit en son nom.
2. `dataset-synthetique.jsonl` : **150 scénarios synthétiques** (`S001`–`S150`), texte sans audio. Il mesure réellement le moteur local sur du texte, **ni le WER ni une transcription vocale réelle**.
3. `dataset-fiabilite.jsonl` : **53 scénarios ciblés** (`F001`–`F053`) du lot P1–P4 : inventions, catalogue/alias/ambiguités, confusions d'intentions, quantités et catégories proches.

Les mesures se répartissent ainsi :

- **banc live final** : `resultats/live-20261127-final-150-v2.*` (150 appels HTTP réels, 150/150 intentions, 85/85 articles, 0 erreur, p95 11,886 s) ;
- **recalcul final v3** : `resultats/live-20261127-final-150-v3.*` (mêmes 150 réponses brutes rejouées le 28/11 après correction de l'alias ambigu `costard` : 150/150, 85/85, 0 invention, 0 champ sans preuve) ;
- **live six scénarios** : `resultats/live-20261127-curated.jsonl` et son rejeu `live-20261127-curated-apres-garde-fous.*` ;
- **recalcul hors ligne** : `resultats/apres-correction-m1-v2.*` (150 sorties historiques `avant-1.5b.jsonl` rejouées dans les garde-fous corrigés) ;
- **synthétique TTS** : `latency/stt-base.jsonl`, `stt-contextual.jsonl`, `stt-contextual-v2.jsonl` ;
- **comparaisons terminées** : `resultats/comparaisons/{1.5B,3B}-{base,small}-150.*` (4 × 150 scénarios texte) et `latency/stt-{base,small}-{1.5B,3B}-4-150.jsonl` (3 phrases TTS par couple) ; défaut `1,5B`/`base` conservé ;
- **historique modèle** : `avant-1.5b.*`, `apres-0.5b.*`, `comparaison.md`.

Aucun enregistrement client avec consentement n'a été fourni. Tout chiffre issu du jeu synthétique ou du TTS doit être qualifié comme tel.

## Jeu réel attendu

L'équipe doit enregistrer 150 phrases au comptoir, avec des voix et accents variés, du bruit de fond réaliste, le consentement explicite de chaque locuteur, une durée ≤ 20 s en WAV 16 kHz mono PCM 16 bits, un stockage hors Git (`eval/audio/`, ignoré) et une transcription de référence plus le JSON attendu dans `dataset.jsonl` (ignoré).

Format attendu d'une ligne de `dataset.jsonl` (le chemin `audio` est relatif à `-AudioRoot`) :

```json
{"id":"R001","source":"reel","consentement":"consentement explicite obtenu","audio":"audio/R001.wav","reference":"Où en est le ticket 142 ?","expected":{"intent":"find_order","client":{"name":null,"phone":null},"items":[],"ticket":"142","amount":null,"payment_method":null,"deposit":null,"pickup_date":null,"item_status":null,"confidence":0.9,"transcript":"Où en est le ticket 142 ?"}}
```

Validation avant exécution du banc :

```powershell
.\tools\assistant\eval\valider-dataset.ps1 -Source reel -Prefix R -RequireAudio -AudioRoot .\tools\assistant\eval
.\tools\assistant\bench.ps1 -Dataset .\tools\assistant\eval\dataset.jsonl -AudioRoot .\tools\assistant\eval
```

Le validateur vérifie maintenant les WAV réels : en-tête RIFF/WAVE, PCM, mono, 16 kHz, 16 bits et durée ≤ 20 s. Aucun résultat n'est écrit tant que les 150 lignes et les 150 fichiers audio ne sont pas présents.

## Jeu synthétique utilisé pour la mesure M1

`dataset-synthetique.jsonl` contient exactement 150 lignes JSONL :

| Champ | Rôle |
|---|---|
| `id` | `S001` à `S150` |
| `source` | `synthetique` |
| `consentement` | `sans objet - phrase creee pour le banc` |
| `audio` | `null` : aucun fichier audio n'est inventé |
| `reference` | phrase source envoyée comme texte au LLM |
| `expected` | vérité terrain figée avant exécution |

Répartition : 70 `create_order`, 15 `register_pickup`, 15 `add_payment`, 15 `update_item_status`, 15 `find_order`, 20 `unknown`. `confidence` est consigné mais exclu des comparaisons exactes.

### Régénérer et valider

```powershell
.\tools\assistant\update-vocab.ps1 -ApiUrl http://localhost:5180
.\tools\assistant\eval\generer-dataset-synthetique.ps1
.\tools\assistant\eval\valider-dataset.ps1
.\tools\assistant\eval\valider-dataset.ps1 `
  -Dataset .\tools\assistant\eval\dataset-fiabilite.jsonl -Count 53 -Prefix F
```

Validations réellement exécutées le 27 novembre 2026 : **150 lignes valides** et **53 lignes valides**. Le catalogue local utilisé comporte 20 catégories, 5 services et 49 synonymes après curation des alias LLM.

### Comparaisons de modèles (autorisées, résultats publiés)

Les modèles `3B` et `small` sont installés et vérifiés (SHA-256 et taille conformes). Le script `tools\assistant\comparer-modeles.ps1` relance chaque couple `1,5B/3B` × `base/small`, vérifie le modèle réellement lancé via le PID, exécute le banc texte puis le banc STT TTS, et restaure `1,5B`/`base` à la fin. Il attend désormais une complétion minimale réussie avant chaque banc : le port `8081` s'ouvre avant la fin du chargement du GGUF et répondait sinon `503 Loading model` sur les premiers scénarios. `-Only '3B-base'` permet de relancer un seul couple. Un passage rapide sur 53 scénarios a validé l'orchestration ; le banc complet sur 150 scénarios a été exécuté le 28/11. Aucune empreinte n'est contournée et le modèle par défaut n'est pas remplacé. Les mesures réelles sont écrites dans `eval/resultats/comparaisons/`.

Résultat des quatre couples : **150/150 intentions et 85/85 articles après garde-fous** partout, 0 erreur. Champs finaux : 1840/1840 pour `1,5B`, 1833/1840 pour `3B`. p95 texte : 11,381 s (`1,5B/base`), 11,335 s (`1,5B/small`), 14,174 s (`3B/base` et `3B/small`). STT sur trois phrases TTS : 1,35 s avec `base`, 4,63 s avec `small`, WER normalisé 0,0 % dans tous les cas. Le défaut reste `1,5B`/`base` ; le détail et la décision sont dans `RESULTATS.md` §7.

## Banc texte réel (sans audio)

`EvaluationHarness` appelle le code C# de production (`LlmIntentExtractor`) et le serveur LLM local. Il distingue la sortie **brute** du modèle et le résultat **après garde-fous**. Il mesure séparément catalogue, construction du prompt, validation JSON, HTTP et total.

```powershell
dotnet build tools\assistant\eval\EvaluationHarness\EvaluationHarness.csproj
dotnet tools\assistant\eval\EvaluationHarness\bin\Debug\net10.0\EvaluationHarness.dll `
  tools\assistant\eval\dataset-synthetique.jsonl `
  tools\assistant\eval\resultats\mon-mesure 'mon-mesure'
```

Sorties : `mon-mesure.jsonl`, `mon-mesure.resume.json`, `mon-mesure.resume.md`. Le banc refuse tout scénario qui référence un fichier audio.

### Mesures déjà exécutées

| Fichier | Contenu réel |
|---|---|
| `resultats/avant-1.5b.*` | référence 150 scénarios, 1,5B, avant grammaire compacte |
| `resultats/apres-0.5b.*` | 150 scénarios, 0,5B, 9 erreurs d'exécution |
| `resultats/apres-correction-m1-v2.*` | **recalcul hors ligne** : 150/150 intentions, 85/85 articles, 1840/1840 champs, 0 invention |
| `resultats/live-20261127-final-150-v2.*` | **banc live final** : 150 appels, 150/150 intentions, 85/85 articles, 1840/1840 champs, 0 erreur, p95 11,886 s |
| `resultats/live-20261127-final-150-v3.*` | **recalcul hors ligne** du même brut figé après correction de l'alias ambigu : 150/150, 85/85, 1840/1840, 0 invention, 0 champ sans preuve |
| `resultats/live-20261127-final-150.*` | run live intermédiaire : 148/150 intentions, 2 erreurs `pickup_date`, conservé comme historique |
| `resultats/live-20261127-curated.jsonl` | 6 réponses HTTP live capturées après régénération du vocabulaire |
| `resultats/live-20261127-curated-apres-garde-fous.*` | recalcul live six scénarios : 6/6 intentions, 8/8 articles, 0 invention |
| `resultats/comparaison.md` | comparaison des deux résumés historiques |
| `latency/avant-1.5b-*.jsonl`, `latency/apres-0.5b-*.jsonl` | phases prompt / génération / HTTP sur six phrases |
| `latency/stt-base.jsonl` | Whisper seul, trois WAV TTS, WER brut 30,8 / 38,9 / 33,3 % |
| `latency/stt-contextual.jsonl` | Whisper avec prompt contextuel, WER brut 0 / 22,2 / 25,0 %, normalisé 0 % |
| `latency/stt-contextual-v2.jsonl` | même vocabulaire après curation des alias LLM, WER identique, latence réduite |

Pour rejouer les 150 sorties brutes sans appel réseau :

```powershell
dotnet tools\assistant\eval\EvaluationHarness\bin\Debug\net10.0\EvaluationHarness.dll `
  --recompute-from-jsonl `
  tools\assistant\eval\resultats\avant-1.5b.jsonl `
  tools\assistant\eval\resultats\apres-correction-m1-v2 'apres-correction-m1-v2'
```

Pour rejouer les 150 réponses live du 27/11 avec les garde-fous corrigés le 28/11 (alias ambigu et nombre dans un libellé) :

```powershell
dotnet tools\assistant\eval\EvaluationHarness\bin\Debug\net10.0\EvaluationHarness.dll `
  --recompute-from-jsonl `
  tools\assistant\eval\resultats\live-20261127-final-150-v2.jsonl `
  tools\assistant\eval\resultats\live-20261127-final-150-v3 `
  'live-20261127-final-150-v3'
```

Pour rejouer les six sorties live capturées :

```powershell
dotnet tools\assistant\eval\EvaluationHarness\bin\Debug\net10.0\EvaluationHarness.dll `
  --recompute-from-jsonl `
  tools\assistant\eval\resultats\live-20261127-curated.jsonl `
  tools\assistant\eval\resultats\live-20261127-curated-apres-garde-fous `
  'live-20261127-curated-apres-garde-fous'
```

Les latences de ces résumés sont celles des fichiers sources ; elles ne sont pas remesurées. Un recalcul ne remplace pas un benchmark live.

## Banc audio réel

```powershell
.\tools\assistant\update-vocab.ps1 -ApiUrl http://localhost:5180
.\tools\assistant\bench.ps1
```

Le banc audio s'arrête si `dataset.jsonl` est absent, si un fichier audio manque ou si un moteur ne répond pas. Ses seuils restent : ≥ 90 % d'intentions exactes, ≥ 85 % d'articles exacts, p95 < 5 s et 100 % de sorties conformes au schéma. **Il n'a pas été exécuté** : aucun résultat officiel ni WER réel n'est publié.

Voir `../RESULTATS.md` pour l'interprétation, la décision de conserver le 1,5B par défaut et les limites non masquées.
