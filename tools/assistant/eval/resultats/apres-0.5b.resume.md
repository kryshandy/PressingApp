# Banc d'évaluation — apres-0.5b

- Généré le : 2026-11-27 18:08:11 +01:00
- Jeu : `C:\Users\Ntsimi\Desktop\Keyce\C sharp\Pressing Def\PressingApp\tools\assistant\eval\dataset-synthetique.jsonl`
- Scénarios : 150 (synthétiques : 150)
- Modèle : `pressing-local` · date de référence catalogue : 2026-11-27
- Erreurs d'exécution : 9

| Mesure | Brut | Après garde-fous C# |
|---|---:|---:|
| Intentions exactes | 115/150 (76,7 %) | 115/150 (76,7 %) |
| Articles exacts | 40/85 (47,1 %) | 51/85 (60,0 %) |
| Champs exacts | 1414/1840 (76,8 %) | 1451/1840 (78,9 %) |
| Express correct | 64/85 (75,3 %) | 83/85 (97,7 %) |
| Null corrects | 961/1185 (81,1 %) | 990/1185 (83,5 %) |
| Transcription exacte | 134/150 (89,3 %) | 141/150 (94,0 %) |
| Schéma valide | 141/150 (94,0 %) | 141/150 (94,0 %) |
| Informations inventées | 224 | 195 |

## Phases de latence

| Phase | Résultat |
|---|---:|
| Catalogue (une fois) | 28,652 ms |
| Prompt (une fois) | 3,408 ms |
| Validation JSON (isolée) | 0,029 ms en moyenne |
| HTTP (moyenne / p50 / p95) | 3,605 / 2,967 / 8,936 s |
| Pipeline total (moyenne / p50 / p95) | 3,605 / 2,967 / 8,965 s |

- Jeu texte synthétique : aucun audio réel, aucun consentement, WER non mesuré.
- Sorties brutes = JSON du modèle avant garde-fous C#.
- Sorties finales = IntentPayload après validation et garde-fous de LlmIntentExtractor.
- validationMs mesure la validation supplémentaire du JSON brut par le banc ; la validation de production est déjà incluse dans httpSeconds.
- La confiance est informative et exclue des comparaisons exactes.
