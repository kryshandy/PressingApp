# Banc d'évaluation — 3B-base-150

- Généré le : 2026-11-28 04:34:46 +01:00
- Jeu : `C:\Users\Ntsimi\Desktop\Keyce\C sharp\Pressing Def\PressingApp\tools\assistant\eval\dataset-synthetique.jsonl`
- Scénarios : 150 (synthétiques : 150)
- Modèle : `pressing-local` · date de référence catalogue : 2026-11-27
- Erreurs d'exécution : 0

| Mesure | Brut | Après garde-fous C# |
|---|---:|---:|
| Intentions exactes | 144/150 (96,0 %) | 150/150 (100,0 %) |
| Articles exacts | 64/85 (75,3 %) | 85/85 (100,0 %) |
| Champs exacts | 1778/1840 (96,6 %) | 1833/1840 (99,6 %) |
| Express correct | 79/85 (92,9 %) | 85/85 (100,0 %) |
| Null corrects | 1169/1185 (98,7 %) | 1185/1185 (100,0 %) |
| Transcription exacte | 141/150 (94,0 %) | 150/150 (100,0 %) |
| Schéma valide | 148/150 (98,7 %) | 150/150 (100,0 %) |
| Informations inventées | 16 | 0 |
| Champs sans preuve | 0 | 0 |
| Hors catalogue | 0 | 0 |
| Faux unknown | 0 | 0 |
| Valeurs normalisées via alias | 0 | 18 |
| Catégorie correcte | 82/85 (96,5 %) | 85/85 (100,0 %) |
| Service correct | 73/85 (85,9 %) | 85/85 (100,0 %) |
| Quantité correcte | 85/85 (100,0 %) | 85/85 (100,0 %) |
| Intentions unknown | 20/20 (100,0 %) | 20/20 (100,0 %) |

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
| Catalogue (une fois) | 32,535 ms |
| Prompt (une fois) | 3,350 ms |
| Validation JSON (isolée) | 0,027 ms en moyenne |
| HTTP (moyenne / p50 / p95) | 11,456 / 11,573 / 14,174 s |
| Pipeline total (moyenne / p50 / p95) | 11,456 / 11,573 / 14,174 s |

- Jeu texte synthétique : aucun audio réel, aucun consentement, WER non mesuré.
- Sorties brutes = JSON du modèle avant garde-fous C#.
- Sorties finales = IntentPayload après validation et garde-fous de LlmIntentExtractor.
- validationMs mesure la validation supplémentaire du JSON brut par le banc ; la validation de production est déjà incluse dans httpSeconds.
- La confiance est informative et exclue des comparaisons exactes.
