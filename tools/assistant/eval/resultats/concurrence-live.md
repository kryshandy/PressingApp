# Test de concurrence live — moteurs IA locaux

## Mesure du 2026-11-28 15:51

- Requêtes envoyées en parallèle : 20 (10 LLM + 10 STT)
- Serveurs : `127.0.0.1:8081` et `127.0.0.1:8082` uniquement
- Configuration lue dans `start-ai.ps1` : `-np 1`, `-p 1`
- Aucune donnée envoyée hors de la machine.

| Famille | Réussites | Latences mesurées |
|---|---:|---|
| LLM | **10/10** | min 17,43 s · p50 41,02 s · p95 116,58 s · max 116,58 s |
| STT | **10/10** | min 2,46 s · p50 5,37 s · p95 17,99 s · max 17,99 s |
| Total | **20/20** | min 2,46 s · p50 17,43 s · p95 116,58 s · max 116,58 s |

Verdict : **file d’attente vérifiée** — toutes les requêtes simultanées sont revenues avec un résultat exploitable.

Le temps très long sous charge est attendu : les servateurs sont configurés pour une seule inférence à la fois (`-np 1` et `-p 1`). Les requêtes s’attendent donc les unes les autres au lieu de se mélanger. Aucun résultat n’a été perdu ou inventé.

Ce test vérifie la tenue sous charge parallèle. Il ne mesure pas la latence d’un usage séquentiel et ne constitue pas une validation client.

Commande de reproduction :

```powershell
.\tools\assistant\bench-concurrence.ps1 -Count 10
```
