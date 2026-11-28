# Comparaison avant / apres — banc texte 150 scenarios

* Genere le : 2026-11-27 18:22:44 *

| Mesure | Avant (`avant-1.5b`) | Apres (`apres-0.5b`) | Seuil |
|---|---:|---:|---:|
| Intentions exactes | 132/150 (88,0 %) | 115/150 (76,7 %) | >= 90 % |
| Articles exacts | 49/85 (57,7 %) | 51/85 (60,0 %) | >= 85 % |
| Champs exacts | 1731/1840 (94,1 %) | 1451/1840 (78,9 %) | — |
| Express correct | 85/85 (100,0 %) | 83/85 (97,7 %) | — |
| Categorie correcte | 56/85 (65,9 %) | 68/85 (80,0 %) | — |
| Service correct | 78/85 (91,8 %) | 58/85 (68,2 %) | — |
| Quantite correcte | 73/85 (85,9 %) | 75/85 (88,2 %) | — |
| Valeurs null correctes | 1160/1185 (97,9 %) | 990/1185 (83,5 %) | — |
| Transcription exacte | 150/150 (100,0 %) | 141/150 (94,0 %) | — |
| Schema valide | 150/150 (100,0 %) | 141/150 (94,0 %) | >= 100 % |
| Informations inventees | 25 | 195 | 0 |
| Intentions unknown | 19/20 (95,0 %) | 7/20 (35,0 %) | — |

## Latence (pipeline complet C#, 150 appels)

| Phase | Avant | Apres | Objectif |
|---|---:|---:|---:|
| HTTP p95 | 12,857 s | 8,936 s | < 5 s |
| Total p95 | 12,857 s | 8,965 s | < 5 s |
| HTTP moyen | 11,263 s | 3,605 s | — |
| Total moyen | 11,264 s | 3,605 s | — |
| Validation JSON (isolee) | 0,039 ms | 0,029 ms | — |

## Verdict

- Intentions >= 90 % : **NON ATTEINT**
- Articles >= 85 % : **NON ATTEINT**
- Schema 100 % : **NON ATTEINT**
- p95 < 5 s : **NON ATTEINT**
- Aucune information inventee : **NON ATTEINT**

*Jeu syntetique : aucun audio reel, aucun consentement, WER non mesure. Les seuils d intention et d article sont calcules sur le resultat final apres garde-fous C#. *
