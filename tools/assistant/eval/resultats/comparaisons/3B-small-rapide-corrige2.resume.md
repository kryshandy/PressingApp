# Banc d'évaluation — 3B-small-rapide-corrige2

- Généré le : 2026-11-28 02:15:10 +01:00
- Jeu : `C:\Users\Ntsimi\Desktop\Keyce\C sharp\Pressing Def\PressingApp\tools\assistant\eval\resultats\comparaisons\3B-small-rapide.jsonl`
- Recalcul depuis : `C:\Users\Ntsimi\Desktop\Keyce\C sharp\Pressing Def\PressingApp\tools\assistant\eval\resultats\comparaisons\3B-small-rapide.jsonl` (aucun appel réseau)
- Scénarios : 53 (synthétiques : 53)
- Modèle : `pressing-local` · date de référence catalogue : 2026-11-27
- Erreurs d'exécution : 0

| Mesure | Brut | Après garde-fous C# |
|---|---:|---:|
| Intentions exactes | 50/53 (94,3 %) | 53/53 (100,0 %) |
| Articles exacts | 28/36 (77,8 %) | 35/36 (97,2 %) |
| Champs exacts | 658/674 (97,6 %) | 671/674 (99,5 %) |
| Express correct | 36/36 (100,0 %) | 36/36 (100,0 %) |
| Null corrects | 430/437 (98,4 %) | 437/437 (100,0 %) |
| Transcription exacte | 53/53 (100,0 %) | 53/53 (100,0 %) |
| Schéma valide | 53/53 (100,0 %) | 53/53 (100,0 %) |
| Informations inventées | 7 | 0 |
| Champs sans preuve | 0 | 1 |
| Hors catalogue | 0 | 0 |
| Faux unknown | 0 | 0 |
| Valeurs normalisées via alias | 0 | 4 |
| Catégorie correcte | 32/36 (88,9 %) | 36/36 (100,0 %) |
| Service correct | 32/36 (88,9 %) | 36/36 (100,0 %) |
| Quantité correcte | 36/36 (100,0 %) | 35/36 (97,2 %) |
| Intentions unknown | 2/2 (100,0 %) | 2/2 (100,0 %) |

## Matrice de confusion — intentions (attendu → obtenu) (intentions)

| Attendu | Obtenu | Nombre |
|---|---|---:|
| unknown | unknown | 2 |
| create_order | create_order | 30 |
| register_pickup | register_pickup | 4 |
| find_order | find_order | 4 |
| add_payment | add_payment | 7 |
| update_item_status | update_item_status | 6 |

## Matrice de confusion — catégories (attendu → obtenu) (catégories)

| Attendu | Obtenu | Nombre |
|---|---|---:|
| Chemise | Chemise | 8 |
| Cravate | Cravate | 2 |
| null | null | 1 |
| Costume 2 pièces | Costume 2 pièces | 3 |
| Manteau | Manteau | 3 |
| Rideau | Rideau | 1 |
| Jupe | Jupe | 3 |
| Tapis | Tapis | 3 |
| Nappe | Nappe | 3 |
| Couverture | Couverture | 2 |
| Drap 2 places | Drap 2 places | 2 |
| Pantalon | Pantalon | 1 |
| Robe simple | Robe simple | 2 |
| Pull | Pull | 1 |
| Chemisier | Chemisier | 1 |

## Phases de latence

| Phase | Résultat |
|---|---:|
| Catalogue (une fois) | 0,000 ms |
| Prompt (une fois) | 0,000 ms |
| Validation JSON (isolée) | 0,026 ms en moyenne |
| HTTP (moyenne / p50 / p95) | 11,623 / 11,293 / 12,593 s |
| Pipeline total (moyenne / p50 / p95) | 11,623 / 11,293 / 12,594 s |

- Recalcul hors ligne : aucun appel au serveur LLM, aucun audio, aucun WER.
- Les latences sont celles mesurées dans le fichier source, pas remesurées.
- Sorties finales = JSON brut enregistré, rejoué par LlmIntentExtractor.ApplyGuardrails.
- La confiance est informative et exclue des comparaisons exactes.
