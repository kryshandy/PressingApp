# Contribuer à Pressing Éclat — fonctionnalités avancées (v2)

La **v1** couvre tout le fonctionnement classique d'un pressing (voir [README.md](README.md)).
Ce guide explique **comment l'équipe de 5 développe la suite sans se marcher dessus** : organisation
du code, règles Git, répartition des chantiers avancés et critères de qualité.

> Règle d'or : `main` doit **toujours** compiler, passer les tests et pouvoir être livrée.

---

## 1. Comprendre l'architecture en 5 minutes

```
PressingApp.sln
├── src/Pressing.Core      ← le cœur : entités EF Core, DTO, services métier, PDF, e-mails
├── src/Pressing.Api       ← API REST (minimal API, JWT). SEUL projet qui touche la base SQLite
├── src/Pressing.Client    ← client HTTP typé de l'API, partagé par Desktop et Web
├── src/Pressing.Desktop   ← application Windows Forms du personnel (thème sombre)
├── src/Pressing.Web       ← site public + espace client (ASP.NET Core MVC)
├── tests/Pressing.Tests   ← tests unitaires et d'intégration (xUnit)
├── docs/API.md            ← contrat de l'API (référence unique entre les projets)
└── scripts/               ← build.ps1, start.ps1, stop.ps1
```

Le chemin d'une fonctionnalité est **toujours le même** :

1. **Core** : entité (`Entities/`) → DTO (`Dtos/`) → règle métier dans un service (`Services/`).
2. **Api** : une route dans `Endpoints/XxxEndpoints.cs` avec la bonne politique (`StaffOnly`, `ManagerOrAdmin`, `AdminOnly`, `ClientOnly`).
3. **docs/API.md** : ajout de la ligne de la route (même PR !).
4. **Client** : une méthode dans `PressingApiClient.cs`.
5. **Desktop** et/ou **Web** : l'écran qui l'utilise.
6. **Tests** : au minimum un test d'intégration du scénario nominal et un cas refusé.

Règles d'architecture à ne jamais casser :

- Le Desktop et le Web **ne lisent jamais la base directement** : tout passe par l'API.
- La logique métier vit dans **Core** (pas dans les endpoints, pas dans les formulaires).
- Les services lèvent `InvalidOperationException` / `ArgumentException` / `KeyNotFoundException` avec un **message en français lisible** : le middleware de l'API les transforme en 400/404 et le client les affiche tels quels.
- Toute opération sensible appelle `AuditService.LogAsync`.
- Aucun secret dans le dépôt : tout passe par `.env` (modèle : `.env.example`).

## 2. Mettre en place son poste

```powershell
git clone https://github.com/kryshandy/PressingApp.git
cd PressingApp
copy .env.example .env
.\scripts\build.ps1 -Tests
.\scripts\start.ps1
```

Comptes de démonstration : voir la section « Comptes » du README.

**Base de données de développement** : pour expérimenter sans abîmer vos données, pointez
`PRESSING_DB_PATH` (dans `.env`) vers un fichier jetable, par exemple `data/dev.db`.

## 3. Workflow Git

| Élément | Règle |
| --- | --- |
| Branche principale | `main`, protégée : on n'y pousse **jamais** directement |
| Branche de travail | `prenom/sujet-court` — ex. `mael/paiement-mobile-money` |
| Commits | petits et fréquents, en français, préfixés par la zone : `API : …`, `Core : …`, `Desktop : …`, `Web : …`, `Tests : …`, `Docs : …` |
| Synchronisation | `git pull --rebase origin main` au moins une fois par jour |
| Pull Request | une PR par fonctionnalité, description = quoi / pourquoi / comment tester / captures d'écran |
| Revue | **au moins 1 relecteur** d'un autre lot avant fusion ; l'auteur ne fusionne pas sa propre PR sans relecture |
| Fusion | « Squash and merge » si les commits sont désordonnés, sinon « Rebase and merge » |

Avant d'ouvrir une PR, **obligatoire** :

```powershell
.\scripts\build.ps1 -Tests
```

…puis tester à la main le scénario dans le Desktop et/ou le site.

### Zones sensibles (prévenir l'équipe avant d'y toucher)

- `Pressing.Core/Data/PressingDbContext.cs` et les entités : changement de schéma (voir §5).
- `Pressing.Api/Program.cs` : authentification, politiques, services enregistrés.
- `Pressing.Desktop/Ui/Theme.cs` et `Pressing.Web/wwwroot/css/site.css` : identité visuelle commune.
- `docs/API.md` : contrat partagé.

## 4. Répartition des chantiers avancés (5 lots)

Chaque lot est **indépendant** : un propriétaire principal, des fichiers majoritairement à lui,
et des points de contact clairement identifiés. Les priorités sont notées **P1** (à faire en premier) à **P3**.

### Lot A — Notifications & relation client
*Propriétaire : membre 1*

- **P1** Notifications WhatsApp / SMS (commande prête, rappel de retrait, relance d'impayé) via un fournisseur (Twilio, WhatsApp Cloud API, Orange SMS…). Créer une interface `IMessageTransport` sur le modèle de `IEmailTransport` (`Core/Email/EmailTransport.cs`).
- **P1** Tâche planifiée de rappels (`BackgroundService` sur le modèle de `Api/Infrastructure/EmailBackgroundService.cs`) : articles prêts non retirés depuis N jours, soldes impayés.
- **P2** Préférences de contact du client (e-mail / WhatsApp / aucun) dans la fiche client et le profil web.
- **P3** Enquête de satisfaction après retrait.

Points de contact : `EmailService`, `SettingsKeys`, écran Paramètres (Desktop), profil (Web).

### Lot B — Paiement en ligne & livraison
*Propriétaire : membre 2*

- **P1** Paiement en ligne du solde depuis l'espace client (Mobile Money : Orange Money, MTN, Wave, via un agrégateur type CinetPay / PayDunya ; ou Stripe). Webhook de confirmation → `PaymentService.AddPaymentCoreAsync` avec `PaymentMethod.MobileMoney`. **Jamais** de clé de production dans le dépôt ; utiliser le mode test du fournisseur.
- **P1** Demande de collecte / livraison à domicile depuis le site (créneaux), file des demandes côté Desktop. L'entité `Order` possède déjà `DeliveryType`, `DeliveryAddress`, `DeliveryFee`.
- **P2** Planning des livreurs et statut de tournée.
- **P3** Abonnements / forfaits mensuels (ex. 20 chemises par mois).

Points de contact : `OrderService`, `PaymentService`, `PortalEndpoints`, contrôleurs `/compte` du Web.

### Lot C — Atelier & production
*Propriétaire : membre 3*

- **P1** Étiquettes par article (code-barres / QR, `QRCoder` est déjà référencé) imprimables au dépôt ; scan en atelier pour changer le statut d'un article.
- **P1** Impression du ticket sur imprimante thermique 80 mm (en plus du PDF A5 actuel de `Core/Reports/ReceiptService.cs`).
- **P2** Photos des articles au dépôt (taches, défauts) stockées côté API et visibles dans le détail de commande.
- **P2** Postes de travail (lavage, séchage, repassage, contrôle qualité) et temps passé.
- **P3** Gestion du stock de consommables (lessive, cintres, housses) avec alertes.

Points de contact : `OrderItem`, `OrderService.UpdateItemStatusAsync`, écran détail de commande (Desktop).

### Lot D — Gestion, finance & analytique
*Propriétaire : membre 4*

- **P1** Clôture de caisse journalière (« Z de caisse ») : fond de caisse, encaissements par mode, écart constaté, validation par un manager, historique.
- **P1** Dépenses / charges (loyer, électricité, salaires, produits) et marge réelle.
- **P2** Rapports avancés : exports Excel et PDF, comparaison de périodes, meilleurs clients, articles les plus rentables (base : `ReportService`).
- **P3** Multi-boutiques (une `ShopId` sur les entités principales, sélection de la boutique à la connexion).

Points de contact : `ReportService`, `DashboardService`, écrans Caisse et Rapports (Desktop).

### Lot E — Qualité, sécurité & déploiement
*Propriétaire : membre 5 (ou l'intégrateur de l'équipe)*

- **P1** Migrations EF Core : remplacer `EnsureCreated` (`Core/Data/DatabaseInitializer.cs`) par des migrations versionnées, **sans perte des bases existantes** (migration initiale « baseline »). À faire **en premier**, car les autres lots modifient le schéma.
- **P1** Intégration continue GitHub Actions : `dotnet build` + `dotnet test` sur chaque PR (runner `windows-latest` pour le Desktop).
- **P2** Sauvegardes automatiques planifiées et rotation (la sauvegarde manuelle existe déjà : `SettingsService.CreateBackupAsync`).
- **P2** Sécurité : double authentification pour les comptes Admin, verrouillage après échecs répétés, politique de mots de passe, écran de consultation du journal d'audit.
- **P3** Déploiement : publication `dotnet publish`, installation de l'API comme service Windows ou conteneur Docker, installeur du Desktop (MSIX / Inno Setup), application mobile / PWA pour le client.

Points de contact : tous les projets — le lot E relit en priorité les PR qui touchent au schéma.

### Ordre conseillé

1. Semaine 1 : **E-P1 (migrations + CI)** pendant que les autres lots écrivent leurs maquettes et leurs services Core sans toucher au schéma.
2. Ensuite, chaque lot avance en parallèle sur ses P1, puis P2.
3. Une **démo d'équipe** à chaque fin de semaine sur `main`.

## 5. Changer le schéma de la base

La base SQLite contient les vraies données du pressing : **on ne supprime jamais `pressing.db`**.

1. Tant que les migrations (lot E) ne sont pas en place : coordonnez-vous avec le propriétaire du lot E avant tout ajout de colonne/table.
2. Une fois en place : `dotnet ef migrations add NomExplicite --project src/Pressing.Core --startup-project src/Pressing.Api`.
3. Une migration doit être **non destructive** (ajout de colonnes avec valeur par défaut, pas de suppression de données).
4. Testez-la sur une **copie** de la base (sauvegarde créée depuis Paramètres → Sauvegarder).

## 6. Conventions de code

- C# moderne (`Nullable` activé, `async/await` partout, jamais de `.Result` / `.Wait()`).
- Noms de types et membres en anglais (comme l'existant), **textes visibles en français** avec accents.
- Montants en `decimal`, affichage via `Fmt.Money` ; dates via `Fmt.Date` / `Fmt.DateTime`.
- Desktop : un écran = un `UserControl` enregistré dans la navigation (voir `src/Pressing.Desktop/README-DESKTOP.md`) ; réutiliser les contrôles de `Ui/` (boutons, cartes, grilles) — pas de contrôle Windows gris non stylé.
- Web : réutiliser les variables et composants de `wwwroot/css/site.css` ; pas de nouvelle bibliothèque front sans accord de l'équipe ; accessibilité (labels, contrastes, focus visibles).
- Pas de code mort ni de `Console.WriteLine` de débogage dans les PR.

## 7. Définition de « terminé »

Une fonctionnalité est terminée quand :

- [ ] elle compile sans nouvel avertissement et `.\scripts\build.ps1 -Tests` passe ;
- [ ] la route est dans `docs/API.md` et dans `PressingApiClient` ;
- [ ] les droits sont vérifiés (un rôle non autorisé reçoit 403) ;
- [ ] les messages d'erreur sont clairs en français ;
- [ ] l'écran respecte le thème (Desktop sombre / Web ivoire et laiton) ;
- [ ] le README est mis à jour si l'utilisateur final voit la différence ;
- [ ] un membre d'un autre lot a relu et testé la PR.
