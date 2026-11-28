# Banc d'évaluation — apres-correction-m1

- Généré le : 2026-11-27 20:05:27 +01:00
- Jeu : `C:\Users\Ntsimi\Desktop\Keyce\C sharp\Pressing Def\PressingApp\tools\assistant\eval\resultats\avant-1.5b.jsonl`
- Recalcul depuis : `C:\Users\Ntsimi\Desktop\Keyce\C sharp\Pressing Def\PressingApp\tools\assistant\eval\resultats\avant-1.5b.jsonl` (aucun appel réseau)
- Scénarios : 150 (synthétiques : 150)
- Modèle : `pressing-local` · date de référence catalogue : 2026-11-27
- Erreurs d'exécution : 0

| Mesure | Brut | Après garde-fous C# |
|---|---:|---:|
| Intentions exactes | 132/150 (88,0 %) | 150/150 (100,0 %) |
| Articles exacts | 49/85 (57,6 %) | 85/85 (100,0 %) |
| Champs exacts | 1650/1840 (89,7 %) | 1840/1840 (100,0 %) |
| Express correct | 84/85 (98,8 %) | 85/85 (100,0 %) |
| Null corrects | 1159/1185 (97,8 %) | 1185/1185 (100,0 %) |
| Transcription exacte | 70/150 (46,7 %) | 150/150 (100,0 %) |
| Schéma valide | 150/150 (100,0 %) | 150/150 (100,0 %) |
| Informations inventées | 26 | 0 |
| Champs sans preuve | 0 | 0 |
| Hors catalogue | 0 | 0 |
| Faux unknown | 0 | 0 |
| Valeurs normalisées via alias | 0 | 40 |
| Catégorie correcte | 56/85 (65,9 %) | 85/85 (100,0 %) |
| Service correct | 78/85 (91,8 %) | 85/85 (100,0 %) |
| Quantité correcte | 73/85 (85,9 %) | 85/85 (100,0 %) |
| Intentions unknown | 19/20 (95,0 %) | 20/20 (100,0 %) |

## Matrice de confusion — intentions (attendu → obtenu) (intentions)

| Attendu | Obtenu | Nombre |
|---|---|---:|
| create_order | create_order | 70 |
| register_pickup | register_pickup | 15 |
| add_payment | add_payment | 15 |
| update_item_status | update_item_status | 15 |
| find_order | find_order | 15 |
| unknown | unknown | 20 |

## Matrice de confusion — catégories (attendu → obtenu) (catégories)

| Attendu | Obtenu | Nombre |
|---|---|---:|
| Chemise | Chemise | 6 |
| Pantalon | Pantalon | 5 |
| Costume 2 pièces | Costume 2 pièces | 5 |
| Costume 3 pièces | Costume 3 pièces | 4 |
| Robe simple | Robe simple | 5 |
| Robe de soirée | Robe de soirée | 4 |
| Jupe | Jupe | 5 |
| Chemisier | Chemisier | 4 |
| Pull | Pull | 5 |
| Manteau | Manteau | 5 |
| Doudoune | Doudoune | 4 |
| Jean | Jean | 4 |
| Blouson | Blouson | 4 |
| Cravate | Cravate | 4 |
| Écharpe | Écharpe | 4 |
| Couverture | Couverture | 4 |
| Drap 2 places | Drap 2 places | 3 |
| Rideau | Rideau | 3 |
| Nappe | Nappe | 3 |
| Tapis | Tapis | 4 |

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
