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
- **Caisse** : encaissements de la période par mode de paiement. **Impayés** : liste et total des soldes.
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
- **248 tests automatisés** au total, dont **102 tests IA M1/M2** (calculs, paiements, retraits, droits, portail, suivi, garde-fous, catalogue, alias ambigus, consultation).

---

## 2. Prérequis

- Windows 10 ou 11
- [.NET SDK 10](https://dotnet.microsoft.com/download) ou plus récent (le runtime *Windows Desktop* est inclus dans le SDK)
- PowerShell (fourni avec Windows)

Aucun serveur de base de données : SQLite est embarqué.

## 3. Installation et lancement

```powershell
git clone https://github.com/kryshandy/PressingApp.git
cd PressingApp
copy .env.example .env          # configuration locale (facultatif : les valeurs par défaut fonctionnent)
.\scripts\build.ps1 -Tests      # restaure, compile et lance les tests
.\tools\assistant\install.ps1   # IA locale : moteurs + modèles vérifiés (optionnel)
.\scripts\start.ps1             # démarre l'API, le site, le Desktop et l'IA locale
```

- Sans le Desktop : `.\scripts\start.ps1 -SansDesktop`
- Sans les moteurs IA : `.\scripts\start.ps1 -SansIA` (ou `ASSISTANT_ENABLED=false` dans `.env`)
- Arrêt de tout (applications + IA) : `.\scripts\stop.ps1`
- Journaux : dossier `logs\` (applications) et `%LOCALAPPDATA%\Pressing\ai\logs\` (IA)

Lancement manuel, projet par projet :

```powershell
dotnet run --project src/Pressing.Api        # http://localhost:5180
dotnet run --project src/Pressing.Web        # http://localhost:5181
dotnet run --project src/Pressing.Desktop
```

> Si Windows affiche une alerte du pare-feu au premier lancement de l'API, autorisez l'accès au réseau privé
> uniquement si d'autres postes du pressing doivent s'y connecter.

## 4. Comptes de démonstration

Créés automatiquement au premier démarrage (à changer en production) :

| Rôle | Identifiant | Mot de passe |
| --- | --- | --- |
| Administrateur | `admin` | `Admin@2026!` (variable `PRESSING_ADMIN_PASSWORD`) |
| Manager | `manager` | `Manager@2026!` |
| Caissier | `caisse` | `Caisse@2026!` |
| Clients démo (site web) | leur téléphone ou e-mail (voir l'écran Clients) | `Client@2026!` |

Les données de démonstration (clients, commandes, paiements) se désactivent avec `PRESSING_SEED_DEMO=false`.

## 5. Parcours type au comptoir

1. **Nouveau dépôt** (F2) → rechercher le client ou « Nouveau client ».
2. Ajouter les articles (catégorie, service, quantité, express), saisir l'acompte → **Valider le dépôt**.
3. Le ticket s'affiche en grand → **Reçu PDF** / **Envoyer par e-mail**.
4. En atelier : dans **Commandes**, ouvrir la commande et passer les articles *En traitement* puis *Prêt*
   (le client reçoit l'e-mail « commande prête » si les e-mails sont configurés).
5. Au retour du client : **Retraits** → saisir/scanner le ticket → cocher les articles → encaisser le reste → valider.
6. En fin de journée : **Caisse** pour le total par mode de paiement, **Impayés** pour les relances.

## 6. Configuration (`.env`)

Toutes les variables sont documentées dans [`.env.example`](.env.example). Les principales :

| Variable | Rôle | Défaut |
| --- | --- | --- |
| `PRESSING_DB_PATH` | fichier de la base SQLite | `%LOCALAPPDATA%\Pressing\pressing.db` |
| `PRESSING_BACKUP_PATH` | dossier des sauvegardes | `Backups` à côté de la base |
| `PRESSING_API_URL` | **adresse d'écoute** de l'API (serveur uniquement) | `http://0.0.0.0:5180` |
| `PRESSING_PUBLIC_API_URL` | **adresse publique** de l'API utilisée par le Desktop et le site | `http://localhost:5180` |
| `PRESSING_WEB_URL` | adresse publique du site (liens dans les e-mails) | `http://localhost:5181` |
| `PRESSING_ADMIN_USERNAME` / `PRESSING_ADMIN_PASSWORD` | compte administrateur initial | `admin` / `Admin@2026!` |
| `PRESSING_SEED_DEMO` | données de démonstration | `true` |
| `EMAIL_PROVIDER` | `auto`, `resend`, `smtp` ou `simulated` | `auto` (simulé si rien n'est configuré) |
| `RESEND_API_KEY` ou `SMTP_*` | envoi réel des e-mails | vide |
| `PRESSING_TRUSTED_PROXIES` | IP des proxys de confiance (reverse proxy) | vide |
| `ASSISTANT_ENABLED` | active/désactive les moteurs IA au démarrage | `true` |
| `ASSISTANT_LLM_URL` / `ASSISTANT_STT_URL` | adresses **boucle locale** des moteurs | `http://127.0.0.1:8081` / `:8082` |
| `ASSISTANT_CONFIDENCE_THRESHOLD` | seuil en dessous duquel l'assistant demande de reformuler | `0.6` |

`PRESSING_API_URL` et `PRESSING_PUBLIC_API_URL` sont **différents** : le premier dit à l'API sur quelle adresse
elle écoute (`0.0.0.0:5180` derrière un proxy) ; le second dit aux clients quelle adresse appeler
(`https://api.exemple.local` si un reverse proxy termine TLS). En local, les deux valeurs par défaut pointent vers
`localhost:5180`. Les en-têtes `X-Forwarded-For` / `X-Forwarded-Proto` ne sont acceptés que depuis la boucle locale
ou les IP listées dans `PRESSING_TRUSTED_PROXIES` ; `X-Real-IP` n'est pas utilisé.

Le fichier `.env` n'est **jamais** versionné. La clé JWT générée (`jwt.key`, à côté de la base) est également
ignorée par Git ; ne la copiez pas sur un autre poste (chaque serveur doit avoir la sienne).

**Installation sur plusieurs postes** : l'API tourne sur un poste « serveur » du pressing ; sur les autres postes,
réglez `PRESSING_PUBLIC_API_URL=http://<ip-du-serveur>:5180` dans le `.env` du Desktop. Ne partagez jamais le fichier
`.db` par le réseau : tous les postes passent par l'API.

## 7. Données et sauvegardes

- Base : `%LOCALAPPDATA%\Pressing\pressing.db` (créée automatiquement, mode WAL, catalogue de 20 catégories et tarifs de départ).
- **Paramètres → Sauvegardes → Sauvegarder maintenant** crée une copie vérifiée dans `Backups`.
- Ne supprimez jamais `pressing.db`, `pressing.db-wal` ou `pressing.db-shm` : faites d'abord une sauvegarde.
- Le schéma est créé avec `EnsureCreated` ; le passage aux migrations EF Core est prévu dans les évolutions futures (voir CONTRIBUTING).

## 8. Architecture

```
src/Pressing.Core      Entités EF Core, DTO, services métier, reçus PDF (QuestPDF), e-mails
src/Pressing.Api       API REST minimal API + JWT ; seul projet qui accède à la base
src/Pressing.Client    Client HTTP typé de l'API (partagé par le Desktop et le site)
src/Pressing.Desktop   Application Windows Forms du personnel
src/Pressing.Web       Site public + espace client
tests/Pressing.Tests   Tests unitaires et d'intégration (xUnit, WebApplicationFactory)
tools/assistant/       Moteur IA local (scripts, prompt, grammaire, banc d'essai, résultats)
docs/API.md            Contrat de toutes les routes de l'API
scripts/               build.ps1, start.ps1, stop.ps1
```

`src/Pressing.Core/Assistant/` contient les contrats et clients IA (aucun accès SQLite, aucune route).

Documentation de l'API générée : http://localhost:5180/openapi/v1.json — exemples de requêtes dans `src/Pressing.Api/Pressing.Api.http`.

Guides internes :
- [tools/assistant/README.md](tools/assistant/README.md) — installation, démarrage et reprise par le Membre 2
- [tools/assistant/RESULTATS.md](tools/assistant/RESULTATS.md) — banc d'essai (état réellement mesuré)
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

- Tests du moteur IA (sans moteur ni modèle nécessaire) :
  `dotnet test tests/Pressing.Tests/Pressing.Tests.csproj --filter FullyQualifiedName~Assistant`

## Assistant vocal IA local — Membre 1

Le moteur est **entièrement local** : whisper.cpp transcrit un WAV 16 kHz mono en français sur `127.0.0.1:8082`,
puis llama.cpp (Qwen2.5-1.5B-Instruct Q4_K_M) extrait une intention JSON sur `127.0.0.1:8081`. Aucune donnée ne sort
de la machine, aucun modèle n'est versionné et les fichiers audio ne sont jamais écrits sur le disque.

```powershell
.\tools\assistant\install.ps1          # téléchargements vérifiés par SHA-256 officiel
.\tools\assistant\update-vocab.ps1     # catalogue réel -> vocabulaire (API publique en lecture seule)
.\tools\assistant\start-ai.ps1         # 8081 + 8082, journaux dans %LOCALAPPDATA%\Pressing\ai\logs
.\tools\assistant\stop-ai.ps1
```

Le contrat JSON M1 est défini dans `src/Pressing.Core/Assistant/AssistantContracts.cs`
(`IntentPayload`, `IntentClient`, `IntentItem`). Le DTO officiel attendu par le contrat T2.1 du Membre 2 est
`Pressing.Core.Dtos.AssistantIntentDto`, avec un mapping explicite sans duplication de noms de champs. Le Membre 2
doit consommer ce DTO via `IM2IntentExtractor` / `IM2SpeechToText`, sans créer de second DTO concurrent, et
transformer le brouillon en commande via les routes existantes. Les points d'intégration (`ISpeechToText`,
`IIntentExtractor`, `IM2*`, `LocalAssistantPipeline`, `AssistantClientFactory`) sont détaillés dans
[tools/assistant/README.md](tools/assistant/README.md) et [docs/API.md](docs/API.md).

Règles garanties par le code : champs inconnus à `null`, confiance < 0,6 ou intention `unknown` ⇒
« je n'ai pas compris » + transcription, silences détectés avant inférence, enregistrements limités à 20 s,
une seule inférence à la fois, lecture seule du catalogue, aucune opération métier déclenchée par l'IA.

> **Divergence de documentation à trancher par l'équipe** : `CONTRIBUTING.md` attribue actuellement les
> notifications WhatsApp/e-mail au « membre 1 », alors que la feuille de route v3 les attribue au **Membre 3**.
> Ce module suit la feuille de route v3 et n'a pas modifié `CONTRIBUTING.md`.

Aucun WER réel n'est publié : le banc officiel nécessite 150 enregistrements avec consentement, qui n'ont pas été fournis. Un banc live synthétique de 150 appels et une chaîne TTS ont été mesurés ; le p95 reste supérieur à 5 s. Voir [tools/assistant/RESULTATS.md](tools/assistant/RESULTATS.md).

## 10. Suite du projet

Les évolutions à venir (notifications WhatsApp/SMS, paiement Mobile Money en ligne, collecte et livraison,
étiquettes code-barres, clôture de caisse, migrations, CI, déploiement…) sont réparties entre les 5 membres dans
**[CONTRIBUTING.md](CONTRIBUTING.md)**.

## Équipe

Projet réalisé par l'équipe B2A Keyce — 5 membres. Le thème sombre du Desktop s'appuie sur le travail initial de Mael.
