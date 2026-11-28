# Résultats du banc d'essai — assistant vocal local

## En bref

Banc TTS **non réel** : 150 phrases parlées par une voix Windows, pas des clients. Le modèle seul fait des erreurs ; avec le garde-fou du produit, on obtient **150/150 intentions, 85/85 articles, 0 invention**. La voix reste un **brouillon à confirmer** par un employé. Seul point non conforme : **12,98 s** de réponse au lieu de 5 s. Le temps réel est renvoyé à M2 avec un poste plus puissant. **Aucun WER client n’est publié.**

## Mesure TTS synthétique — non réel du 2026-11-28 14:15

- Jeu : `.\\tools\\assistant\\eval\\dataset-tts.jsonl`
- Source : `tts` (voix de synthèse Windows, **aucune voix humaine**)
- Phrases traitées : 150 / 150 (complet)
- LLM : 1,5B · STT : base
- Serveurs : `http://127.0.0.1:8081` et `http://127.0.0.1:8082` (127.0.0.1 uniquement)
- Trace brute rejouable : `banc-tts-150-brut.jsonl`
- Rejeu sans appel réseau : `banc-tts-150-apres-garde-fous.*`

Ce banc mesure deux choses distinctes, volontairement séparées : la **sortie brute du LLM** et la **sortie réelle du produit après garde-fous C#**. Les latences et le WER proviennent du passage audio réel du 28/11 à 14:15 ; le rejeu des garde-fous ne refait aucun appel réseau.

### 1. Sortie brute du LLM — sans garde-fous

| Mesure | Résultat | Objectif |
|---|---:|---:|
| Intentions exactes | 92,0 % (138/150) | ≥ 90 % |
| Articles exacts | 23,5 % (20/85) | ≥ 85 % |
| Champs exacts | 82,9 % (1526/1840) | informationnel |
| Sorties conformes au contrat | 98,7 % (148/150) | 100 % |
| Informations inventées | 21 | 0 |
| WER moyen (TTS) | 37,9 % | comparatif base/small |
| Latence p50 | 10,55 s | informationnel |
| Latence p95 | 12,98 s | < 5 s |

Le WER ne mesure que la reconnaissance de la voix **synthétique** : il n'est pas un WER client. La voix TTS déforme une partie du vocabulaire métier, ce qui explique les erreurs d'articles de la sortie brute.

### 2. Sortie du produit — après garde-fous C#

Rejeu hors ligne des **mêmes 150 réponses brutes** par `LlmIntentExtractor.ApplyGuardrails` (`EvaluationHarness --recompute-from-jsonl`) :

| Mesure | Résultat | Objectif |
|---|---:|---:|
| Intentions exactes | **100 % (150/150)** | ≥ 90 % |
| Articles exacts | **100 % (85/85)** | ≥ 85 % |
| Champs exacts | **99,4 % (1829/1840)** | informationnel |
| Sorties conformes au contrat | **100 % (150/150)** | 100 % |
| Informations inventées | **0** | 0 |
| Champs sans preuve | **0** | 0 |
| Hors catalogue / faux `unknown` | **0 / 0** | 0 / 0 |
| Erreurs d'exécution | **0** | 0 |

Les garde-fous refusent d'inventer un article quand la transcription ne le prouve pas : c'est précisément le comportement attendu d'un assistant en mode **brouillon à confirmer**.

### Verdict

- **Fiabilité après garde-fous : CONFORME** sur les intentions, les articles, le contrat JSON et l'absence d'invention.
- **Latence : NON CONFORME** : p95 **12,98 s**, largement au-dessus de `< 5 s` sur CPU sans GPU.
- **Nature des données : TTS non réel.** Aucun WER client n'est publié, aucune validation de production n'est revendiquée.

Décision M1 : l'assistant reste une **aide à la saisie** dont le brouillon est confirmé par le Membre 2. Le seuil de latence `< 5 s` est renvoyé à M2, conditionné à un poste avec GPU. Aucune opération métier n'est déclenchée par la voix.

La latence est mesurée entre l'envoi du WAV au STT et la réception du JSON du LLM. Aucune donnée n'est envoyée hors de la machine.

Commandes de reproduction :

```powershell
.\tools\assistant\bench.ps1 -Dataset .\tools\assistant\eval\dataset-tts.jsonl -AudioRoot .\tools\assistant\eval -Source tts -Output .\tools\assistant\eval\resultats\banc-tts-150.md -RawDump .\tools\assistant\eval\resultats\banc-tts-150-brut.jsonl
dotnet tools\assistant\eval\EvaluationHarness\bin\Debug\net10.0\EvaluationHarness.dll --recompute-from-jsonl tools\assistant\eval\resultats\banc-tts-150-brut.jsonl tools\assistant\eval\resultats\banc-tts-150-apres-garde-fous 'banc-tts-150-apres-garde-fous'
```
