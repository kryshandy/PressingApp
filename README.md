# Pressing Éclat — logiciel de gestion de pressing (version 2.0.0)

Solution complète pour un pressing : **une application Desktop pour le personnel**, **un site web pour les clients**
et **une API** qui centralise les données. Tout fonctionne en local sur un poste Windows, sans serveur de base de données à installer.

| Application | Pour qui | Adresse |
| --- | --- | --- |
| **Pressing Desktop** (Windows Forms, thème sombre) | caissiers, managers, administrateur | application Windows |
| **Site client** (ASP.NET Core MVC) | clients du pressing | http://localhost:5181 |
| **API REST** (ASP.NET Core, JWT) | utilisée par le Desktop et le site | http://localhost:5180 |

Les trois partagent **la même base SQLite** à travers l'API : un dépôt enregistré au comptoir est immédiatement visible par le client sur le site.

---

## 1. Fonctionnalités de la version 2.0.0

### Au comptoir (Desktop)
- **Connexion du personnel** avec 3 rôles : *Administrateur*, *Manager*, *Caissier* — le menu s'adapte au rôle.
- **Tableau de bord** : chiffre d'affaires du jour et du mois, commandes actives, prêtes, en retard, total des impayés, graphique des 7 derniers jours, répartition par statut, commandes récentes et prêtes à retirer, notifications.
- **Clients** : fiche complète (nom, prénom, téléphone, WhatsApp, e-mail, adresse, notes), recherche, historique des commandes et paiements, solde dû, points de fidélité, activation de l'**accès web** (mot de passe, e-mail de bienvenue).
- **Nouveau dépôt** : choix ou création rapide du client, plusieurs articles (catégorie × service : lavage, lavage + repassage, repassage, nettoyage à sec, détachage), quantités, prix repris de la grille tarifaire, **express** par article ou global, date de retrait prévue, retrait en boutique ou livraison, remise, code promo, points fidélité, **acompte** ; récapitulatif calculé en direct ; **ticket unique** `PRESS-AAAA-00000`.
- **Suivi des articles** : statut individuel *Déposé → En traitement → Prêt → Retiré* (couleurs bleu, orange, vert, gris) ; le statut de la commande se déduit de ses articles.
- **Paiements** en plusieurs fois (espèces, Mobile Money, carte, virement), solde calculé automatiquement.
- **Retraits** totaux ou **partiels** (choix des articles et quantités), avec encaissement du reste ; retrait refusé tant que le solde n'est pas réglé (paramétrable).
- **Reçu PDF** (A5, QR code) à imprimer ou envoyer par e-mail.
- **Caisse** : encaissements de la période par mode de paiement ; **clôture de caisse journalière** (« Z de caisse ») : fond de caisse, espèces comptées, écart calculé en direct (commentaire obligatoire en cas d'écart), validation par un Manager ou un Administrateur (la clôture validée n'est plus modifiable), historique et PDF. **Impayés** : liste et total des soldes.
- **Tarifs** : grille catégorie × service, supplément express, délai.
- **Annulation et remboursement** (Manager/Admin), avec remboursement par mode de paiement d'origine.
- **Administration** : promotions, rapports par période avec export CSV, gestion du personnel, paramètres de la boutique, configuration des e-mails, **sauvegarde vérifiée de la base**, journal d'audit.

### Pour les clients (site web)
- Accueil, **tarifs**, **suivi d'une commande** par ticket + téléphone (frise des étapes), contact.
- **Espace client** : tableau de bord (commandes en cours, articles prêts, solde, points), liste et détail des commandes, **téléchargement du reçu PDF**, profil, changement et réinitialisation du mot de passe, historique de fidélité.
- Site responsive (mobile → grand écran), accessible, sécurisé (cookie HttpOnly, anti-falsification, CSP).

### Sécurité et fiabilité
- Mots de passe hachés (PBKDF2), jetons JWT revérifiés à chaque requête (compte désactivé ou rôle changé = accès coupé).
- Un client ne voit **que ses propres** commandes et reçus.
- Limitation des tentatives de connexion par adresse IP.
- Journal d'audit de toutes les opérations sensibles.
- **253 tests automatisés** (calculs, paiements, retraits, droits, portail, suivi, reprise après panne de l'API,
  migrations, rappels, désinscription, assistant vocal).

---

## 2. Prérequis

| Outil | Version | Vérifier |
| --- | --- | --- |
| Windows | 10 ou 11 (le Desktop est une application Windows Forms) | — |
| [.NET SDK](https://dotnet.microsoft.com/download) | **10 ou plus récent** (le runtime *Windows Desktop* est inclus) | `dotnet --list-sdks` |
| PowerShell | celui de Windows suffit | — |
| Git | pour cloner et contribuer | `git --version` |

Aucun serveur de base de données à installer : SQLite est embarqué.

Si PowerShell refuse de lancer les scripts (« l'exécution de scripts est désactivée »), autorisez-les pour votre
seul compte, une fois pour toutes :

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

## 3. Lancer les applications

### 3.1 Premier lancement (poste de développement ou de démonstration)

```powershell
git clone https://github.com/kryshandy/PressingApp.git
cd PressingApp
copy .env.example .env          # facultatif : start.ps1 le crée s'il manque
.\scripts\build.ps1 -Tests      # restaure, compile et lance les tests (doit finir par « Compilation terminée »)
.\scripts\start.ps1             # démarre l'API, puis le site, puis le Desktop
```

`start.ps1` attend que l'API réponde sur `http://localhost:5180/api/health` avant de lancer le site et le Desktop.
Les trois applications partagent la même base, créée automatiquement au premier démarrage de l'API
(catalogue, tarifs, comptes et, par défaut, données de démonstration).

| Application | Adresse | Comment l'ouvrir |
| --- | --- | --- |
| API | http://localhost:5180 | utilisée par les deux autres ; santé : `/api/health`, contrat : `/openapi/v1.json` |
| Site client | http://localhost:5181 | dans un navigateur |
| Desktop | — | s'ouvre tout seul sur l'écran de connexion |

### 3.2 Au quotidien

| Besoin | Commande |
| --- | --- |
| Tout démarrer (recompile d'abord) | `.\scripts\start.ps1` |
| Démarrer sans recompiler (plus rapide) | `.\scripts\start.ps1 -SansBuild` |
| API et site seulement (serveur, ou Desktop lancé ailleurs) | `.\scripts\start.ps1 -SansDesktop` |
| Tout arrêter | `.\scripts\stop.ps1` |
| Voir les journaux | dossier `logs\` (`api.log`, `api.err.log`, `web.log`, `web.err.log`) |

**Toujours lancer `stop.ps1` avant de recompiler** : une API ou un Desktop encore ouvert verrouille les fichiers
de `bin\` et la compilation échoue avec « le fichier est verrouillé par Pressing.Api ».

### 3.3 Lancement manuel, projet par projet (débogage)

Dans trois terminaux, **dans cet ordre** (l'API d'abord) :

```powershell
dotnet run --project src/Pressing.Api      # http://localhost:5180
dotnet run --project src/Pressing.Web      # http://localhost:5181
dotnet run --project src/Pressing.Desktop
```

Chaque application lit le fichier `.env` en remontant depuis son dossier : lancées depuis le dépôt, elles trouvent
toutes le même `.env` à la racine.

## 4. Comptes de démonstration

Créés automatiquement **au premier démarrage** de l'API (une base déjà créée garde ses comptes) :

| Rôle | Identifiant | Mot de passe |
| --- | --- | --- |
| Administrateur | `admin` | `Admin@2026!` (variables `PRESSING_ADMIN_USERNAME` / `PRESSING_ADMIN_PASSWORD`) |
| Manager | `manager` | `Manager@2026!` |
| Caissier | `caisse` | `Caisse@2026!` |
| Clients démo (site web) | leur téléphone ou e-mail (voir l'écran Clients) | `Client@2026!` |

Les données de démonstration (clients, commandes, paiements, comptes manager et caissier) se désactivent avec
`PRESSING_SEED_DEMO=false`, **avant** le premier démarrage. En production, voir la section 6.

## 5. Parcours type au comptoir

1. **Nouveau dépôt** (F2) → rechercher le client ou « Nouveau client ».
2. Ajouter les articles (catégorie, service, quantité, express), saisir l'acompte → **Valider le dépôt**.
3. Le ticket s'affiche en grand → **Reçu PDF** / **Envoyer par e-mail**.
4. En atelier : dans **Commandes**, ouvrir la commande et passer les articles *En traitement* puis *Prêt*
   (le client reçoit l'e-mail « commande prête » si les e-mails sont configurés).
5. Au retour du client : **Retraits** → saisir/scanner le ticket → cocher les articles → encaisser le reste → valider.
6. En fin de journée : **Caisse** pour le total par mode de paiement, **Impayés** pour les relances.

## 6. Configuration pour un lancement complet

Tout fonctionne **sans rien configurer** sur un seul poste : e-mails simulés (visibles dans Paramètres → E-mails),
données de démonstration, adresses `localhost`. Pour un vrai pressing, faites la liste ci-dessous **dans l'ordre**.

### 6.1 Liste de mise en service

| # | Quoi | Où | Pourquoi |
| --- | --- | --- | --- |
| 1 | Choisir l'emplacement de la base et des sauvegardes | `.env` : `PRESSING_DB_PATH`, `PRESSING_BACKUP_PATH` (sauvegardes sur un autre disque ou une clé) | une panne de disque ne doit pas emporter les deux |
| 2 | Désactiver la démonstration | `.env` : `PRESSING_SEED_DEMO=false` | pas de faux clients ni de comptes `manager` / `caisse` connus de tous |
| 3 | Mot de passe administrateur | `.env` : `PRESSING_ADMIN_PASSWORD` (long et unique) | lu **une seule fois**, à la création de la base |
| 4 | Mode production | `.env` : `ASPNETCORE_ENVIRONMENT=Production` | page d'erreur publique et HSTS sur le site |
| 5 | Premier démarrage | `.\scripts\start.ps1` puis connexion `admin` | la base, la clé `jwt.key` et le compte admin sont créés |
| 6 | Infos de la boutique | Desktop → **Paramètres → Boutique** (nom, adresse, téléphone, WhatsApp, e-mail, horaires) | elles apparaissent sur les reçus, les e-mails et le site |
| 7 | Tarifs et catégories | Desktop → **Tarifs** | le catalogue de départ est un exemple : ajustez prix, express et délais |
| 8 | Comptes du personnel | Desktop → **Personnel** (un compte par personne, rôle Caissier par défaut) | chaque action est tracée au nom de son auteur dans l'audit |
| 9 | E-mails réels | `.env` (voir 6.3), puis Desktop → **Paramètres → E-mails → e-mail de test** | confirmation de dépôt, « commande prête », reçus, mot de passe oublié |
| 10 | Adresse publique du site | `.env` : `PRESSING_WEB_URL` | les liens des e-mails (suivi, réinitialisation) doivent ouvrir le vrai site |
| 11 | Autres postes du comptoir | voir 6.4 | plusieurs caisses sur la même base |
| 12 | Première sauvegarde | Desktop → **Paramètres → Sauvegardes → Sauvegarder maintenant** | vérifier que le dossier de sauvegarde fonctionne |

Après toute modification du `.env` : `.\scripts\stop.ps1` puis `.\scripts\start.ps1 -SansBuild`.

### 6.2 Variables du `.env`

Toutes les variables sont documentées dans [`.env.example`](.env.example). Le fichier `.env` n'est **jamais** versionné.

| Variable | Rôle | Défaut |
| --- | --- | --- |
| `PRESSING_DB_PATH` | fichier de la base SQLite | `%LOCALAPPDATA%\Pressing\pressing.db` |
| `PRESSING_BACKUP_PATH` | dossier des sauvegardes | `Backups` à côté de la base |
| `PRESSING_API_URL` | **adresse d'écoute** de l'API (serveur uniquement) | `http://0.0.0.0:5180` |
| `PRESSING_PUBLIC_API_URL` | **adresse publique** de l'API utilisée par le Desktop et le site | `http://localhost:5180` |
| `PRESSING_WEB_URL` | adresse publique du site (liens dans les e-mails) | `http://localhost:5181` |
| `PRESSING_ADMIN_USERNAME` / `PRESSING_ADMIN_PASSWORD` | compte administrateur initial | `admin` / `Admin@2026!` |
| `PRESSING_SEED_DEMO` | données de démonstration | `true` |
| `PRESSING_JWT_KEY` | clé de signature des jetons | vide : générée dans `jwt.key` à côté de la base |
| `EMAIL_PROVIDER` | `auto`, `resend`, `smtp` ou `simulated` | `auto` |
| `RESEND_*` ou `SMTP_*` | envoi réel des e-mails | vide |
| `EMAIL_BACKGROUND_ENABLED` | traitement automatique de la file d'e-mails | `true` |
| `REMINDERS_ENABLED` | rappels automatiques (WhatsApp / e-mail) aux clients consentants | `true` |
| `BACKUP_SCHEDULE_ENABLED` / `BACKUP_HOUR` / `BACKUP_RETENTION_DAYS` / `BACKUP_COPY_PATH` | sauvegardes automatiques quotidiennes (voir 7.1) | `true` / `22` / `30` / vide |
| `WHATSAPP_PROVIDER` | `simulated` ou `meta` | `simulated` |
| `WHATSAPP_ACCESS_TOKEN` / `WHATSAPP_PHONE_NUMBER_ID` | accès à l'API WhatsApp Business Cloud (Meta) | vide |
| `WHATSAPP_WEBHOOK_VERIFY_TOKEN` / `WHATSAPP_APP_SECRET` | webhook Meta (réponse « STOP ») | vide |
| `PRESSING_TRUSTED_PROXIES` | IP des proxys de confiance (reverse proxy) | vide |

`PRESSING_API_URL` et `PRESSING_PUBLIC_API_URL` sont **différents** : le premier dit à l'API sur quelle adresse
elle écoute (`0.0.0.0:5180` derrière un proxy) ; le second dit aux clients quelle adresse appeler
(`https://api.exemple.local` si un reverse proxy termine TLS). En local, les deux valeurs par défaut pointent vers
`localhost:5180`. Les en-têtes `X-Forwarded-For` / `X-Forwarded-Proto` ne sont acceptés que depuis la boucle locale
ou les IP listées dans `PRESSING_TRUSTED_PROXIES` ; `X-Real-IP` n'est pas utilisé.

La clé JWT générée (`jwt.key`, à côté de la base) est ignorée par Git ; ne la copiez pas sur un autre poste
(chaque serveur doit avoir la sienne).

### 6.3 Activer les vrais e-mails

Avec `EMAIL_PROVIDER=auto`, l'API choisit Resend si `RESEND_API_KEY` est rempli, sinon SMTP si `SMTP_HOST` est rempli,
sinon le mode simulé (rien ne part, tout est journalisé).

**Option A — Resend** (le plus simple) : créez une clé sur resend.com, puis :

```
RESEND_API_KEY=re_xxxxxxxx
RESEND_FROM_EMAIL=contact@votre-domaine.com
RESEND_FROM_NAME=Pressing Éclat
```

L'expéditeur de test `onboarding@resend.dev` n'envoie qu'à l'adresse du compte Resend : pour écrire aux clients,
vérifiez votre domaine dans Resend et utilisez une adresse de ce domaine.

**Option B — SMTP avec un mot de passe d'application** (Gmail, Outlook, OVH…). Exemple Gmail (la validation en
deux étapes doit être active sur le compte pour créer un mot de passe d'application) :

```
SMTP_HOST=smtp.gmail.com
SMTP_PORT=587
SMTP_USE_SSL=true
SMTP_USER=votre.adresse@gmail.com
SMTP_APP_PASSWORD=xxxx xxxx xxxx xxxx
```

Vérifiez ensuite dans Desktop → **Paramètres → E-mails** : envoyez un e-mail de test et cochez les envois
automatiques voulus (confirmation, commande prête, reçu).

### 6.4 Plusieurs postes au comptoir

1. Choisissez un poste « serveur » toujours allumé : il lance l'API et le site (`.\scripts\start.ps1 -SansDesktop`)
   et garde la base sur **son disque local**.
2. Sur ce serveur, ouvrez le port de l'API au réseau privé (PowerShell **administrateur**) ; ajoutez 5181 si des
   clients consultent le site depuis le Wi-Fi de la boutique :

   ```powershell
   New-NetFirewallRule -DisplayName "Pressing API" -Direction Inbound -Protocol TCP -LocalPort 5180 -Action Allow -Profile Private
   ```

3. Sur chaque autre poste : installez le .NET SDK (ou le runtime Windows Desktop 10), récupérez le Desktop et placez
   à côté un `.env` contenant `PRESSING_PUBLIC_API_URL=http://<ip-du-serveur>:5180`.
4. Ne partagez **jamais** le fichier `.db` par le réseau : tous les postes passent par l'API.

### 6.5 Rappels WhatsApp et e-mail

- **Consentement d'abord** : un client ne reçoit des rappels que si sa fiche (Desktop → Clients → modifier) indique un
  **canal de rappel** (WhatsApp ou e-mail). Ce choix est daté ; « Aucun rappel » date la révocation.
- **Quels rappels** (toutes les 15 min, entre 8 h et 20 h) : commande prête (WhatsApp), commande non retirée à J+3,
  J+7 puis J+14 (un seul palier à la fois), solde restant quand la commande attend son retrait, veille de l'échéance.
  Chaque rappel ne part qu'une fois par commande.
- **E-mail** : utilise la configuration de la section 6.3 ; chaque e-mail contient un lien vers la page
  `/desabonnement` du site (`PRESSING_WEB_URL` doit donc être l'adresse publique du site).
- **Choix du client** : depuis son profil du site (**Mon compte → Mon profil**, canal « Aucun rappel / WhatsApp / E-mail »
  avec case de consentement obligatoire) ou depuis sa fiche dans le Desktop.
- **Écran Rappels** (Desktop, Manager/Admin) : journal des envois (ticket, client, type, canal, résultat ; filtres par
  type, canal et période) et bouton **Relancer le client**, qui envoie une relance manuelle. Elle est refusée si le client
  n'a pas consenti, et journalisée et auditée.
- **WhatsApp** : par défaut `WHATSAPP_PROVIDER=simulated` (rien ne part, tout est journalisé). Avec `meta`, renseignez
  `WHATSAPP_ACCESS_TOKEN` et `WHATSAPP_PHONE_NUMBER_ID` (Meta for Developers → WhatsApp). En dehors des 24 h qui suivent
  un message du client, Meta n'accepte que des **modèles de messages approuvés** : configurez-les comme ci-dessous.
  Sans nom de modèle pour un type de rappel, ce rappel part en texte libre (réservé aux tests).

  **Créer les modèles WhatsApp** (WhatsApp Manager → *Modèles de messages* → *Créer un modèle*) : catégorie
  **Utilitaire**, langue **Français** (code `fr`, ou `fr_FR` selon votre choix : le reporter dans
  `WHATSAPP_TEMPLATE_LANGUAGE`), type de contenu *Texte*. Créez un modèle par type de rappel ; les variables du corps
  sont fournies dans cet ordre, `{{1}}` = prénom et `{{2}}` = numéro de ticket :

  | Variable `.env` | Type de rappel | `{{3}}` | Exemple de corps |
  | --- | --- | --- | --- |
  | `WHATSAPP_TEMPLATE_READY` | commande prête | (aucune) | « Bonjour {{1}}, votre commande {{2}} est prête. Vous pouvez venir la récupérer. » |
  | `WHATSAPP_TEMPLATE_UNCOLLECTED` | non retirée (J+3, J+7, J+14) | nombre de jours (3, 7 ou 14) | « Bonjour {{1}}, votre commande {{2}} attend d'être récupérée depuis {{3}} jours. » |
  | `WHATSAPP_TEMPLATE_BALANCE` | solde impayé | montant (ex. « 3 500 FCFA ») | « Bonjour {{1}}, le solde restant de votre commande {{2}} est de {{3}}. » |
  | `WHATSAPP_TEMPLATE_DUE` | veille de l'échéance | date jj/MM/aaaa | « Bonjour {{1}}, votre commande {{2}} a une échéance prévue le {{3}}. » |

  Mettez dans `.env` le **nom** exact du modèle (minuscules, chiffres et `_`, ex. `rappel_solde`), une fois le modèle au
  statut *Approuvé* (quelques minutes à quelques heures chez Meta), puis redémarrez l'API. Un modèle qui n'a pas le
  bon nombre de variables est refusé par Meta : l'envoi est alors journalisé en échec dans l'écran Rappels. La relance
  manuelle part toujours en texte libre : elle n'aboutit que si le client a écrit au pressing dans les 24 dernières heures.
- Pour tout couper : `REMINDERS_ENABLED=false`.

Pas encore disponibles (v3 en cours, voir `docs/Bilan-integration-et-attentes.pdf`) : l'assistant vocal IA, les
étiquettes QR, le ticket thermique, la clôture de caisse et le paiement en ligne.

### 6.6 Dépannage

| Symptôme | Cause probable et solution |
| --- | --- |
| « L'API ne répond pas encore » au démarrage | lire `logs\api.err.log` ; port 5180 déjà pris (`.\scripts\stop.ps1`, ou `Get-NetTCPConnection -LocalPort 5180`) |
| Compilation : fichier verrouillé par `Pressing.Api` ou `Pressing.Desktop` | une instance tourne encore : `.\scripts\stop.ps1` puis recompiler |
| Voyant rouge sous « Vérification du serveur » sur l'écran de connexion du Desktop | l'API est arrêtée, ou `PRESSING_PUBLIC_API_URL` pointe ailleurs (poste distant : pare-feu du serveur, 6.4) |
| Le site affiche « service momentanément indisponible » | l'API est arrêtée : relancer `start.ps1` ; les formulaires saisis sont conservés |
| Les e-mails restent « simulés » | aucune clé Resend ni hôte SMTP dans `.env`, ou API non redémarrée après la modification |
| Le mot de passe admin du `.env` ne marche pas | la base existait déjà : le mot de passe n'est lu qu'à sa création ; changez-le dans Desktop → **Mon compte** |
| `dotnet` introuvable ou « SDK not found » | installer le .NET SDK 10+ puis rouvrir le terminal |

## 7. Données et sauvegardes

- Base : `%LOCALAPPDATA%\Pressing\pressing.db` (créée automatiquement, mode WAL, catalogue de 20 catégories et tarifs de départ).
- **Paramètres → Sauvegardes → Sauvegarder maintenant** crée une copie vérifiée dans `Backups`.
- Ne supprimez jamais `pressing.db`, `pressing.db-wal` ou `pressing.db-shm` : faites d'abord une sauvegarde.
- Le schéma est versionné par des **migrations EF Core** (`src/Pressing.Core/Data/Migrations`), appliquées au démarrage
  de l'API. Une base existante (créée par la v2) est **adoptée sans perte**, après une sauvegarde automatique
  `pressing-avant-adoption-…db` ; toute nouvelle migration est précédée d'une sauvegarde `pressing-avant-migration-…db`
  (dans `PRESSING_BACKUP_PATH`).
- Développeurs : `dotnet tool restore` installe `dotnet-ef` ; ajout d'une migration, voir CONTRIBUTING §5.

### 7.1 Sauvegardes automatiques

L'API crée **chaque jour** une sauvegarde vérifiée (`PRAGMA integrity_check`) dans le dossier de sauvegarde, sous le
nom `pressing-auto-AAAAMMJJ-HHmmss.db`. Réglages dans le `.env` (modèle : `.env.example`) :

| Variable | Rôle | Défaut |
| --- | --- | --- |
| `BACKUP_SCHEDULE_ENABLED` | `false` coupe les sauvegardes planifiées | `true` |
| `BACKUP_HOUR` | heure locale (0 à 23) à partir de laquelle la sauvegarde du jour est faite | `22` |
| `BACKUP_RETENTION_DAYS` | durée de conservation des sauvegardes **automatiques** | `30` |
| `BACKUP_COPY_PATH` | copie secondaire de chaque sauvegarde automatique (autre disque, clé USB, dossier synchronisé) | vide (pas de copie) |

- **Une seule fois par jour** : la présence d'un fichier `pressing-auto-<date du jour>-…` suffit à l'établir, même après
  un redémarrage de l'API. Si l'API est arrêtée à l'heure dite, la sauvegarde est faite à son redémarrage le même jour
  (après l'heure choisie).
- **Rotation** : les sauvegardes automatiques de plus de `BACKUP_RETENTION_DAYS` jours sont supprimées, dans le dossier
  principal **et** dans `BACKUP_COPY_PATH`. Les sauvegardes **manuelles** (`pressing-AAAAMMJJ-…db`) et celles
  d'avant migration ou adoption (`pressing-avant-…db`) ne sont **jamais** supprimées automatiquement.
- **Copie secondaire** : si le dossier est indisponible (clé retirée, disque absent), l'erreur est journalisée et la
  sauvegarde locale est conservée ; la copie n'est pas rejouée ce jour-là. Pensez à vérifier ce dossier de temps en temps.
- Une sauvegarde qui échoue au contrôle d'intégrité est supprimée et réessayée au passage suivant (toutes les 5 minutes).
- Elles apparaissent dans **Paramètres → Sauvegardes** comme les sauvegardes manuelles.

### 7.2 Restaurer une sauvegarde

À faire **API arrêtée** (service Windows ou `scripts\stop.ps1`), sinon SQLite garde des fichiers ouverts :

1. Arrêter l'API (et le site si vous voulez éviter les messages d'erreur).
2. **Garder une copie** de la base actuelle (`pressing.db`) : si la restauration ne convient pas, on revient en arrière.
3. Copier la sauvegarde choisie (ex. `pressing-auto-20261128-220000.db`) à la place de `pressing.db`, sous ce nom.
4. **Supprimer `pressing.db-wal` et `pressing.db-shm`** à côté : ils appartiennent à l'ancienne base et ne doivent pas
   être rejoués sur la sauvegarde.
5. Redémarrer l'API. Si la sauvegarde date d'une version plus ancienne, les migrations s'appliquent seules
   (après une sauvegarde `avant-migration`).

Procédure testée sur une base jetable (28/11/2026) : base de démonstration (8 clients) → sauvegarde via l'API →
ajout d'un client (9) → arrêt de l'API → remplacement de `pressing.db` par la sauvegarde, suppression de `-wal`/`-shm` →
redémarrage : **8 clients**, le client ajouté après la sauvegarde a disparu, comme attendu.

## 8. Architecture

```
src/Pressing.Core      Entités EF Core, DTO, services métier, reçus PDF (QuestPDF), e-mails
src/Pressing.Api       API REST minimal API + JWT ; seul projet qui accède à la base
src/Pressing.Client    Client HTTP typé de l'API (partagé par le Desktop et le site)
src/Pressing.Desktop   Application Windows Forms du personnel
src/Pressing.Web       Site public + espace client
tests/Pressing.Tests   Tests unitaires et d'intégration (xUnit, WebApplicationFactory)
docs/API.md            Contrat de toutes les routes de l'API
scripts/               build.ps1, start.ps1, stop.ps1
```

Documentation de l'API générée : http://localhost:5180/openapi/v1.json — exemples de requêtes dans `src/Pressing.Api/Pressing.Api.http`.

Guides internes :
- [docs/API.md](docs/API.md) — contrat de l'API et règles métier
- [src/Pressing.Desktop/DESIGN.md](src/Pressing.Desktop/DESIGN.md) — charte de mise en page du Desktop
- [src/Pressing.Desktop/README-DESKTOP.md](src/Pressing.Desktop/README-DESKTOP.md) — ajouter un écran au Desktop
- [CONTRIBUTING.md](CONTRIBUTING.md) — **organisation de l'équipe et fonctionnalités avancées à venir**

## 9. Tests et vérifications

```powershell
.\scripts\build.ps1 -Tests
```

- Tests unitaires et d'intégration : base SQLite temporaire, jamais la base réelle.
- Autotest de l'application Desktop (API démarrée) :
  `src\Pressing.Desktop\bin\Debug\net10.0-windows\Pressing.Desktop.exe --selftest` — parcourt tous les écrans et un
  dépôt complet (dépôt → statuts → paiement → retrait → reçu) et affiche `SELFTEST OK`.

## 10. Suite du projet

État d'avancement, travail attendu de chaque membre et règles pour éviter les conflits :
**`docs/Bilan-integration-et-attentes.pdf`** (28/11/2026). Organisation Git et conventions :
**[CONTRIBUTING.md](CONTRIBUTING.md)**. Feuille de route initiale : `docs/Brief-etat-et-feuille-de-route-v3.pdf`.

## 11. Déploiement

```powershell
.\scripts\publish.ps1                    # dist\<version>\ : api\, web\, desktop\, scripts\, .env.example, README, LISEZMOI-INSTALLATION.txt
.\scripts\publish.ps1 -AutonomeComplet   # variante avec le runtime .NET embarqué (plus volumineuse)
.\scripts\publish.ps1 -WhatIf            # simulation
```

- **Choix par défaut : publication dépendante du framework (win-x64).** Le dossier livré est petit et les correctifs de
  sécurité de .NET arrivent par Windows Update. Il faut installer une fois *ASP.NET Core Runtime 10* sur le serveur et
  *.NET Desktop Runtime 10* sur les postes (commandes `winget` dans `LISEZMOI-INSTALLATION.txt`). Avec
  `-AutonomeComplet`, aucun prérequis mais le dossier est bien plus gros. `dist/` est ignoré par Git.
- **Services Windows** (PowerShell en administrateur, depuis `dist\<version>\`) :
  `.\scripts\install-service.ps1` enregistre `PressingEclatApi` et `PressingEclatWeb` (démarrage automatique,
  redémarrage après 5 s, 5 s puis 30 s en cas d'échec), crée le `.env` (base et sauvegardes dans
  `C:\ProgramData\PressingEclat`) puis démarre les services. `.\scripts\install-service.ps1 -Desinstaller` les supprime
  (base, sauvegardes et `.env` conservés). Option `-WhatIf` pour simuler.
- Un service ne tourne pas sous votre profil : renseignez toujours `PRESSING_DB_PATH` / `PRESSING_BACKUP_PATH` dans le
  `.env` (le script le fait à la création) ; l'API et le site retrouvent le `.env` du dossier d'installation.
- Postes du comptoir : copier `desktop\`, y ajouter un `.env` avec `PRESSING_PUBLIC_API_URL=http://<serveur>:5180`.
- Contrôles qualité : `.\scripts\check-encoding.ps1` (accents corrompus, scripts sans BOM) et
  `.\scripts\ci-selftest.ps1` (autotest Desktop sur une API jetable) tournent aussi dans la CI.

## Équipe

Projet réalisé par l'équipe B2A Keyce — 5 membres. Le thème sombre du Desktop s'appuie sur le travail initial de Mael.
