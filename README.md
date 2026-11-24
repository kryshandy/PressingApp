# Application de gestion de pressing

Système complet de gestion de pressing composé de **quatre projets** qui partagent **une seule base de données SQLite** :

| Projet | Rôle | Adresse |
| --- | --- | --- |
| `Pressing.Shared` | Entités Entity Framework Core, DTO, règles métier (tarifs, tickets, paiements, soldes, statuts, retraits) | bibliothèque |
| `Pressing.API` | API ASP.NET Core utilisée par l'application desktop | `http://localhost:5180` |
| `Pressing.Desktop` | Application Windows Forms réservée au personnel | lancée en local |
| `Pressing.Web` | Site client ASP.NET Core MVC (Bootstrap, responsive) | `http://localhost:5181` |

Les modifications effectuées depuis le desktop (dépôt, tarif, statut, paiement, retrait) sont immédiatement visibles sur le site client, car les deux applications utilisent la même base et lisent les données à chaque requête.

## 1. Prérequis

- Windows 10/11
- **.NET 10 SDK** (le runtime Windows Desktop est nécessaire pour les Windows Forms)
- PowerShell (toutes les commandes du projet utilisent PowerShell)
- Aucun serveur de base de données à installer : SQLite est embarqué

## 2. Installation et compilation

Ouvrir PowerShell dans le dossier racine du projet (`application de base`) :

```powershell
.\scripts\build.ps1
```

Ce script restaure les paquets NuGet et compile les quatre projets.

## 3. Lancement

```powershell
.\scripts\start.ps1
```

Le script démarre l'API, le site web et l'application desktop, puis affiche les adresses.  
Pour démarrer sans ouvrir le desktop :

```powershell
.\scripts\start.ps1 -SansDesktop
```

Arrêt de toutes les applications :

```powershell
.\scripts\stop.ps1
```

### Lancement manuel (facultatif)

```powershell
dotnet run --project src/Pressing.API
dotnet run --project src/Pressing.Web
dotnet run --project src/Pressing.Desktop
```

L'API et Desktop doivent hériter de la même variable d'environnement `PRESSING_STAFF_API_KEY`. `scripts/start.ps1` génère une clé temporaire si elle n'est pas déjà définie et la transmet aux processus qu'il lance. Pour lancer les exécutables séparément, définissez une clé aléatoire dans l'environnement des deux processus. Ne stockez pas cette clé dans les fichiers du dépôt.

## 4. Base de données

- Fichier : `%LOCALAPPDATA%\Pressing\pressing.db`  
  (par exemple `C:\Users\<votre nom>\AppData\Local\Pressing\pressing.db`)
- Création automatique au premier lancement, avec les 16 catégories d'articles et leurs tarifs de départ.
- Le mode WAL de SQLite est activé : l'API, le site et le desktop peuvent travailler en parallèle.
- Les données (clients, commandes, tickets, paiements, retraits, statuts, historiques) restent disponibles après fermeture et redémarrage.
- Les anciennes commandes et les historiques ne sont jamais supprimés.
- Le bouton **Sauvegarder les données** du Desktop crée une copie SQLite via l'API, puis vérifie son intégrité. Les sauvegardes se trouvent dans `%LOCALAPPDATA%\Pressing\Backups` lorsque la base par défaut est utilisée.
- L'API permet de créer une copie de récupération vérifiée dans `Recovery` à partir d'une sauvegarde. Elle ne remplace jamais la base active.
- Le schéma est actuellement créé avec `EnsureCreated` et aucun historique de migrations EF Core n'existe. Aucune migration n'est appliquée automatiquement. Avant toute évolution du schéma, il faut définir une migration non destructive, la tester sur une copie restaurée et vérifier une sauvegarde ; le remplacement de la base active reste une opération manuelle distincte.

Ne supprimez, ne réinitialisez et ne remplacez jamais `pressing.db`, `pressing.db-wal` ou `pressing.db-shm` pour résoudre un problème. Créez une sauvegarde vérifiée et demandez une analyse avant toute intervention sur les données.

## 5. Comptes clients du site web

Lors de la création d'un client dans le desktop :

- les champs **Nom**, **Prénom**, **Téléphone** et **WhatsApp** sont obligatoires ;
- le champ **Mot de passe** est facultatif :
  - s'il est renseigné, le compte Web est activé (l'identifiant saisi est utilisé, ou le téléphone s'il est laissé vide) ;
  - s'il est laissé vide, la fiche client est créée sans accès Web. Le compte peut être activé plus tard depuis la fiche client.

Ces informations sont affichées sur la fiche client du desktop pour être communiquées au client.  
Un client connecté ne peut consulter que ses propres commandes, soldes, statuts et reçus.

## 6. Fonctionnalités couvertes

### Desktop (personnel)
- Tableau de bord : grille tarifaire, caisse du jour et totale, commandes en cours, vêtements prêts, impayés, recherche rapide et accès rapides.
- Fiches clients : ajout, modification, consultation, recherche (nom, prénom, téléphone, WhatsApp, ticket), historique, solde, paiements et retraits.
- Dépôts : ticket unique `PRESS-AAAA-00000`, plusieurs articles, quantités, catégories, prix repris de la grille tarifaire, acompte, dates par article.
- Lavage express : supplément configurable par catégorie, délai réduit, signalétique visible.
- Statuts indépendants par article : Déposé (bleu), En traitement (orange), Prêt (vert), Retiré (gris), avec historique des changements.
- Retraits totaux ou partiels : articles choisis, montant versé, date/heure, mise à jour des statuts.
- Paiements en plusieurs fois, soldes calculés automatiquement, liste des impayés.
- Caisse calculée uniquement à partir des paiements réellement enregistrés (acompte, paiement direct, paiement lors du retrait).

### Site client
- Connexion/déconnexion sécurisée (cookie d'authentification, jetons anti-forgerie).
- Historique complet des commandes, montants à payer, suivi des vêtements (dont « prêt à récupérer »).
- Génération et téléchargement d'un reçu PDF par commande (anciennes commandes comprises).

Aucun module hors cahier des charges (stock, dépenses, fidélité, réclamation, livraison) n'a été ajouté.

## 7. Tests

Pour enchaîner automatiquement compilation, scénario fonctionnel, redémarrage, vérification de persistance et arrêt :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
```

Le script crée une base temporaire et des dossiers de journaux/résultats isolés sous `%TEMP%`. Les scénarios vérifient notamment les retraits successifs, la quantité restante, les paiements invalides, l'authentification API, l'atomicité client-compte, les vues Web, le reçu PDF, la sauvegarde vérifiée et une copie de restauration non destructive. `smoke.ps1` et `persistence.ps1` refusent désormais de s'exécuter hors de ce contexte isolé.

Les journaux d'exécution sont dans le dossier `logs`.

## 8. Dépannage

- **« Le port est déjà utilisé »** : lancer `.\scripts\stop.ps1` avant de redémarrer.
- **Page blanche ou erreur de connexion à l'API** : vérifier que `Pressing.API` est démarré sur le port 5180 et consulter `logs\api.err.log`.
- **Mot de passe oublié** : le personnel peut définir un nouveau mot de passe depuis la fiche client du desktop.
- **Erreur de compilation** : relancer `.\scripts\build.ps1` ; vérifier que le SDK .NET 10 est installé (`dotnet --list-sdks`).
## Contributions : Prénom Nom (branche `prenom/desktop-theme-sombre`)

Refonte de l'interface de l'application **Pressing.Desktop** (Windows Forms).

- **Correction des textes coupés** : mise à l'échelle DPI (`AutoScaleDimensions`) et cartes qui remplissent leur cellule.
- **Thème sombre** : palette centralisée dans `Ui/Theme.cs`, barre de titre sombre.
- **Menu latéral** : icônes Segoe MDL2, état survol/actif, nouveau logo.
- **Tableau de bord** : cartes de statistiques redessinées, tableaux plus lisibles, statuts recolorés.
- **Boutons** : coins arrondis, styles plein, contour et secondaire.
- **Fenêtres secondaires** : dépôt, retrait, paiement, tarifs, historique, impayés et fiche client harmonisés.

Fichiers principaux : `Ui/Theme.cs`, `Ui/UiFactory.cs`, `Ui/Grids.cs`, `Forms/FrmPrincipal.cs`.