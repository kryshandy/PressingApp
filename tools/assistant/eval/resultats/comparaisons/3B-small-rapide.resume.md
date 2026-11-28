# Banc d'évaluation — 3B-small-rapide

- Généré le : 2026-11-28 02:09:23 +01:00
- Jeu : `C:\Users\Ntsimi\Desktop\Keyce\C sharp\Pressing Def\PressingApp\tools\assistant\eval\dataset-fiabilite.jsonl`
- Scénarios : 53 (synthétiques : 53)
- Modèle : `pressing-local` · date de référence catalogue : 2026-11-27
- Erreurs d'exécution : 0

| Mesure | Brut | Après garde-fous C# |
|---|---:|---:|
| Intentions exactes | 50/53 (94,3 %) | 53/53 (100,0 %) |
| Articles exacts | 28/36 (77,8 %) | 34/36 (94,4 %) |
| Champs exacts | 658/674 (97,6 %) | 669/674 (99,3 %) |
| Express correct | 36/36 (100,0 %) | 36/36 (100,0 %) |
| Null corrects | 430/437 (98,4 %) | 436/437 (99,8 %) |
| Transcription exacte | 53/53 (100,0 %) | 53/53 (100,0 %) |
| Schéma valide | 53/53 (100,0 %) | 53/53 (100,0 %) |
| Informations inventées | 7 | 1 |
| Champs sans preuve | 0 | 0 |
| Hors catalogue | 0 | 0 |
| Faux unknown | 0 | 0 |
| Valeurs normalisées via alias | 0 | 4 |
| Catégorie correcte | 32/36 (88,9 %) | 35/36 (97,2 %) |
| Service correct | 32/36 (88,9 %) | 36/36 (100,0 %) |
| Quantité correcte | 36/36 (100,0 %) | 34/36 (94,4 %) |
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
| null | Costume 2 pièces | 1 |
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
| Catalogue (une fois) | 28,845 ms |
| Prompt (une fois) | 2,971 ms |
| Validation JSON (isolée) | 0,026 ms en moyenne |
| HTTP (moyenne / p50 / p95) | 11,623 / 11,293 / 12,593 s |
| Pipeline total (moyenne / p50 / p95) | 11,623 / 11,293 / 12,594 s |

- Jeu texte synthétique : aucun audio réel, aucun consentement, WER non mesuré.
- Sorties brutes = JSON du modèle avant garde-fous C#.
- Sorties finales = IntentPayload après validation et garde-fous de LlmIntentExtractor.
- validationMs mesure la validation supplémentaire du JSON brut par le banc ; la validation de production est déjà incluse dans httpSeconds.
- La confiance est informative et exclue des comparaisons exactes.
