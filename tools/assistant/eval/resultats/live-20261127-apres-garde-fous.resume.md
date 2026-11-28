# Banc d'évaluation — live-20261127-apres-garde-fous

- Généré le : 2026-11-27 21:05:27 +01:00
- Jeu : `C:\Users\Ntsimi\Desktop\Keyce\C sharp\Pressing Def\PressingApp\tools\assistant\eval\resultats\live-20261127.jsonl`
- Recalcul depuis : `C:\Users\Ntsimi\Desktop\Keyce\C sharp\Pressing Def\PressingApp\tools\assistant\eval\resultats\live-20261127.jsonl` (aucun appel réseau)
- Scénarios : 6 (synthétiques : 0)
- Modèle : `pressing-local` · date de référence catalogue : 2026-11-27
- Erreurs d'exécution : 0

| Mesure | Brut | Après garde-fous C# |
|---|---:|---:|
| Intentions exactes | 6/6 (100,0 %) | 6/6 (100,0 %) |
| Articles exacts | 4/8 (50,0 %) | 8/8 (100,0 %) |
| Champs exacts | 82/92 (89,1 %) | 87/92 (94,6 %) |
| Express correct | 8/8 (100,0 %) | 8/8 (100,0 %) |
| Null corrects | 49/49 (100,0 %) | 49/49 (100,0 %) |
| Transcription exacte | 5/6 (83,3 %) | 6/6 (100,0 %) |
| Schéma valide | 6/6 (100,0 %) | 6/6 (100,0 %) |
| Informations inventées | 0 | 0 |
| Champs sans preuve | 0 | 2 |
| Hors catalogue | 0 | 0 |
| Faux unknown | 0 | 0 |
| Valeurs normalisées via alias | 0 | 4 |
| Catégorie correcte | 7/8 (87,5 %) | 8/8 (100,0 %) |
| Service correct | 5/8 (62,5 %) | 8/8 (100,0 %) |
| Quantité correcte | 8/8 (100,0 %) | 8/8 (100,0 %) |
| Intentions unknown | 1/1 (100,0 %) | 1/1 (100,0 %) |

## Matrice de confusion — intentions (attendu → obtenu) (intentions)

| Attendu | Obtenu | Nombre |
|---|---|---:|
| create_order | create_order | 5 |
| unknown | unknown | 1 |

## Matrice de confusion — catégories (attendu → obtenu) (catégories)

| Attendu | Obtenu | Nombre |
|---|---|---:|
| Chemise | Chemise | 5 |
| Pantalon | Pantalon | 2 |
| Cravate | Cravate | 1 |

## Phases de latence

| Phase | Résultat |
|---|---:|
| Catalogue (une fois) | 0,000 ms |
| Prompt (une fois) | 0,000 ms |
| Validation JSON (isolée) | 0,000 ms en moyenne |
| HTTP (moyenne / p50 / p95) | 200,000 / 200,000 / 200,000 s |
| Pipeline total (moyenne / p50 / p95) | 200,000 / 200,000 / 200,000 s |

- Recalcul hors ligne : aucun appel au serveur LLM, aucun audio, aucun WER.
- Les latences sont celles mesurées dans le fichier source, pas remesurées.
- Sorties finales = JSON brut enregistré, rejoué par LlmIntentExtractor.ApplyGuardrails.
- La confiance est informative et exclue des comparaisons exactes.
