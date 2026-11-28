# Banc d'évaluation — avant-1.5b

- Généré le : 2026-11-27 17:56:29 +01:00
- Jeu : `C:\Users\Ntsimi\Desktop\Keyce\C sharp\Pressing Def\PressingApp\tools\assistant\eval\dataset-synthetique.jsonl`
- Scénarios : 150 (synthétiques : 150)
- Modèle : `pressing-local` · date de référence catalogue : 2026-11-27
- Erreurs d'exécution : 0

| Mesure | Brut | Après garde-fous C# |
|---|---:|---:|
| Intentions exactes | 132/150 (88,0 %) | 132/150 (88,0 %) |
| Articles exacts | 49/85 (57,6 %) | 49/85 (57,6 %) |
| Champs exacts | 1650/1840 (89,7 %) | 1731/1840 (94,1 %) |
| Express correct | 84/85 (98,8 %) | 85/85 (100,0 %) |
| Null corrects | 1159/1185 (97,8 %) | 1160/1185 (97,9 %) |
| Transcription exacte | 70/150 (46,7 %) | 150/150 (100,0 %) |
| Schéma valide | 150/150 (100,0 %) | 150/150 (100,0 %) |
| Informations inventées | 26 | 25 |

## Phases de latence

| Phase | Résultat |
|---|---:|
| Catalogue (une fois) | 63,301 ms |
| Prompt (une fois) | 8,413 ms |
| Validation JSON (isolée) | 0,039 ms en moyenne |
| HTTP (moyenne / p50 / p95) | 11,263 / 9,160 / 12,857 s |
| Pipeline total (moyenne / p50 / p95) | 11,264 / 9,160 / 12,857 s |

- Jeu texte synthétique : aucun audio réel, aucun consentement, WER non mesuré.
- Sorties brutes = JSON du modèle avant garde-fous C#.
- Sorties finales = IntentPayload après validation et garde-fous de LlmIntentExtractor.
- validationMs est une mesure isolée du validateur ; elle s'ajoute au total de production une seule fois.
- La confiance est informative et exclue des comparaisons exactes.
