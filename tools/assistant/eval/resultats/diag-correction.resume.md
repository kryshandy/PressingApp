# Banc d'évaluation — diag

- Généré le : 2026-11-27 19:46:09 +01:00
- Jeu : `C:\Users\Ntsimi\Desktop\Keyce\C sharp\Pressing Def\PressingApp\tools\assistant\eval\resultats\avant-1.5b.jsonl`
- Recalcul depuis : `C:\Users\Ntsimi\Desktop\Keyce\C sharp\Pressing Def\PressingApp\tools\assistant\eval\resultats\avant-1.5b.jsonl` (aucun appel réseau)
- Scénarios : 150 (synthétiques : 150)
- Modèle : `pressing-local` · date de référence catalogue : 2026-11-27
- Erreurs d'exécution : 0

| Mesure | Brut | Après garde-fous C# |
|---|---:|---:|
| Intentions exactes | 132/150 (88,0 %) | 134/150 (89,3 %) |
| Articles exacts | 49/85 (57,6 %) | 0/85 (0,0 %) |
| Champs exacts | 1650/1840 (89,7 %) | 1655/1840 (90,0 %) |
| Express correct | 84/85 (98,8 %) | 84/85 (98,8 %) |
| Null corrects | 1159/1185 (97,8 %) | 1183/1185 (99,8 %) |
| Transcription exacte | 70/150 (46,7 %) | 150/150 (100,0 %) |
| Schéma valide | 150/150 (100,0 %) | 150/150 (100,0 %) |
| Informations inventées | 26 | 2 |
| Champs sans preuve | 0 | 0 |
| Hors catalogue | 0 | 0 |
| Faux unknown | 0 | 15 |
| Valeurs normalisées via alias | 0 | 20 |
| Catégorie correcte | 56/85 (65,9 %) | 65/85 (76,5 %) |
| Service correct | 78/85 (91,8 %) | 15/85 (17,6 %) |
| Quantité correcte | 73/85 (85,9 %) | 65/85 (76,5 %) |
| Intentions unknown | 19/20 (95,0 %) | 19/20 (95,0 %) |

## Matrice de confusion — intentions (attendu → obtenu) (intentions)

| Attendu | Obtenu | Nombre |
|---|---|---:|
| create_order | create_order | 70 |
| register_pickup | register_pickup | 15 |
| add_payment | add_payment | 15 |
| update_item_status | unknown | 15 |
| find_order | find_order | 15 |
| unknown | unknown | 19 |
| unknown | find_order | 1 |

## Matrice de confusion — catégories (attendu → obtenu) (catégories)

| Attendu | Obtenu | Nombre |
|---|---|---:|
| Chemise | Chemise | 4 |
| Chemise | null | 2 |
| Pantalon | Pantalon | 4 |
| Pantalon | null | 1 |
| Costume 2 pièces | Costume 2 pièces | 4 |
| Costume 2 pièces | null | 1 |
| Costume 3 pièces | Costume 3 pièces | 4 |
| Robe simple | Robe simple | 4 |
| Robe simple | null | 1 |
| Robe de soirée | Robe de soirée | 4 |
| Jupe | Jupe | 4 |
| Jupe | null | 1 |
| Chemisier | Chemisier | 4 |
| Pull | Pull | 4 |
| Pull | null | 1 |
| Manteau | null | 4 |
| Manteau | Manteau | 1 |
| Doudoune | Doudoune | 3 |
| Doudoune | null | 1 |
| Jean | Jean | 3 |
| Jean | null | 1 |
| Blouson | Blouson | 3 |
| Blouson | null | 1 |
| Cravate | Cravate | 3 |
| Cravate | null | 1 |
| Écharpe | Écharpe | 3 |
| Écharpe | null | 1 |
| Couverture | Couverture | 3 |
| Couverture | null | 1 |
| Drap 2 places | Drap 2 places | 3 |
| Rideau | null | 2 |
| Rideau | Rideau | 1 |
| Nappe | Nappe | 3 |
| Tapis | Tapis | 3 |
| Tapis | null | 1 |

## Phases de latence

| Phase | Résultat |
|---|---:|
| Catalogue (une fois) | 0,000 ms |
| Prompt (une fois) | 0,000 ms |
| Validation JSON (isolée) | 0,039 ms en moyenne |
| HTTP (moyenne / p50 / p95) | 11,263 / 9,160 / 12,857 s |
| Pipeline total (moyenne / p50 / p95) | 11,264 / 9,160 / 12,857 s |

- Recalcul hors ligne : aucun appel au serveur LLM, aucun audio, aucun WER.
- Les latences sont celles mesurées dans le fichier source, pas remesurées.
- Sorties finales = JSON brut enregistré, rejoué par LlmIntentExtractor.ApplyGuardrails.
- La confiance est informative et exclue des comparaisons exactes.
