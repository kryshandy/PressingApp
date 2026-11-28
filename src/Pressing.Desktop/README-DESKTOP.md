# Pressing Desktop (Windows Forms, .NET 10)

Application du personnel. API : variable `PRESSING_PUBLIC_API_URL` (défaut `http://localhost:5180`).

## Ajouter un écran
1. Créer `Views/MaVue.cs` : `public sealed class MaVue : BaseView` (constructeur `(IAppHost host)`), `Title`, interface construite dans le constructeur, données dans `LoadAsync()` (appels `Api.…Async`, vérifier `Alive` après chaque `await`).
2. L'enregistrer dans `Services/NavRegistry.cs` : une ligne dans `Items` (clé, titre, icône `Icons.*`, section, `h => new MaVue(h)`, rôles : `Everyone`, `ManagerOrAdmin`, `AdminOnly`). Le menu, le masquage par rôle et la navigation suivent automatiquement.
3. Navigation : `Host.Navigate(clé, paramètre)`, `Host.OpenOrder(id)`, `Host.OpenClient(id)`, `Host.OpenView(clé, () => vue)` + `Host.GoBack()`, `Host.Toast(...)`.

## Écrans
Tableau de bord, Nouveau dépôt, Commandes (+ détail), Clients (+ fiche), Retraits, Caisse, Impayés, Tarifs, Mon compte, puis l'**Atelier** et l'administration :
- **Atelier** (`WorkshopView`) : champ toujours actif pour la douchette USB (elle saisit comme un clavier et finit par Entrée) ou la saisie manuelle d'un code d'étiquette `PRESS-AAAA-NNNNN-II[-a]` ; chaque scan fait avancer l'article (Déposé → En traitement → Prêt, `POST /api/workshop/scan`), grand affichage du dernier article, historique, sons, « Annuler le dernier scan » (une étape en arrière). Étiquettes : bouton « Étiquettes » du détail de commande (planche A4 PDF).
- **Ticket 80 mm** : bouton du détail de commande ; imprime en ESC/POS brut (`Services/RawPrinter.cs`, winspool) sur l'imprimante réglée dans Paramètres › Reçus (« Imprimante ticket 80 mm », vide = désactivé). Jamais d'impression réelle en autotest.
- **Caisse** (`CashView`) : paiements et totaux par mode ; bouton « Clôturer la journée » (`CashClosingDialog` : attendus par mode, fond de caisse, espèces comptées, écart en direct, commentaire obligatoire si écart) ; Manager/Admin : historique des clôtures, validation (clôture figée) et PDF.
- **Promotions** (`PromotionsView`, `PromotionDialog`) : codes promo, Manager/Admin.
- **Rapports** (`ReportsView`) : période, chiffres clés, graphique, tableaux, export CSV, Manager/Admin.
- **Personnel** (`StaffView`, `StaffDialogs`) : comptes, rôles, mots de passe, Admin.
- **Rappels** (`RemindersView`) : journal des rappels WhatsApp / e-mail (type, canal, période) et relance manuelle d'un client consentant, Manager/Admin.
- **Paramètres** (`SettingsView`) : boutique, commandes, fidélité, e-mails (journal, test), reçus ; sauvegardes et journal d'audit (Admin). Lecture seule pour un Manager.

## Mise en page (charte `DESIGN.md`)
Les primitives de `Ui/Layout.cs` rendent la charte automatique ; **ne plus utiliser** `TableLayoutPanel`, `ScrollPanel` ou `TwoColumnPanel` pour une page.
- `SetPage(header, body, scroll, toolbar)` (dans `BaseView`) installe un `PageLayout` : gouttière 32 px (mêmes bords que l'en-tête global), en-tête, barre de filtres (16 px dessous), corps rempli (`scroll: false`, listes) ou défilant (`scroll: true`).
- `ScrollViewport` : l'unique défilement d'une page, sans barre native (fine barre thémée dans la gouttière de droite), molette, focus clavier rendu visible. `ThemedGrid` défile aussi sans barre native (molette + barre fine dessinée).
- `VStack(gap = 24)` : pile verticale ; `Add(c, StackSize.Auto | Fixed | Fill, hauteur)`.
- `GridRow` : ligne de 12 colonnes, gouttière 24 px, cellules de même hauteur : `new GridRow().Add(a, 8).Add(b, 4)` ; `.Sticky(b)` garde une colonne visible pendant le défilement ; `StackBelow = 900` empile sous 900 px.
- `UniformRow` (N éléments égaux), `FormGrid(n)` (champs 6+6, 24 px / 16 px), `FieldBox` (libellé MAJUSCULES + champ), `InlineField` (libellé à gauche), `SectionRule("Acompte")`, `FigureBlock` (chiffre clé), `Padded`.
- `TitledCard(titre, contenu, sous-titre, flush, autoHeight)` : padding 24, titre 12 pt, 16 px avant le contenu, actions 32 px à droite (`AddAction`).
- `ActionBar` : boutons sur UNE ligne ; au-delà de 4 (ou faute de place) les actions secondaires les plus à gauche passent dans « Plus d'actions ». `PageHeader.AddAction`/`Actions`, `SetLeading(bouton Retour)`, `AddTitleAdornment(pastille)`.
- `TextLabel` : libellé sans la marge GDI (alignement exact au pixel) ; `Wrap = true` pour le retour à la ligne.
- Grilles : `AddText(..., fixedWidth: 112)` pour les montants/dates/quantités, `weight` + `minWidth` pour les colonnes texte ; `GridCard.AutoHeightRows`/`MaxRows` pour une grille dans une page défilante.
- Après un changement de contenu (lignes chargées, bloc affiché/masqué), appeler `control.RequestPageLayout()` (déjà fait par `GridCard`, `InfoList` et les conteneurs).
- Champs : `InputBox.PlaceholderText` est dessiné au thème (aussi dans les captures). Cases : `ToggleBox` (interrupteur par défaut, `Style = ToggleStyle.Check` pour une case).
- Dialogues : hériter de `DialogForm` (largeurs 480 / 560 / 760, hauteur ajustée au contenu, blocs ajoutés à `Body` en `Dock = Top`).

## Contrôle visuel
`PRESSING_DPI_UNAWARE=1 Pressing.Desktop.exe --selftest --screens <dossier>` : captures de chaque vue, dialogue et de la coque à 1280×760 et 1600×900 (`PRESSING_TEST_SIZES`), barre de titre DWM comprise. `PRESSING_UI_SCALE=1.5` (avec `PRESSING_DPI_UNAWARE=1`) simule un affichage à 150 %.

## Conventions
- Couleurs/polices/espacements : uniquement `Ui/Theme.cs` (`Theme.S(px)` pour toute taille en pixels : DPI).
- Réseau : toujours `await RunAsync(...)` (curseur d'attente, boutons `FlatButton.Busy`, erreurs `ApiException` en boîte de dialogue). Dialogues : hériter de `DialogForm`, appeler l'API via `UiRun.RunAsync(this, ...)`.
- Composants : `PageHeader`, `TitledCard`, `RoundedPanel`, `StatCard`, `FlatButton`, `InputBox`, `SelectBox` (+ `SetChoices`), `DateBox`, `ToggleBox`, `StepperBox`, `ThemedGrid`/`GridCard` (+ `IPillRow`, `ITintRow`), `PagerBar`, `FormGrid`, `FieldBox`, `InfoList`, `ScrollPanel`, `TwoColumnPanel`, `UiKit.Rows`, `AppDialogs`.
- Auto-test : `Pressing.Desktop.exe --selftest [--screens dossier]` (journal `%TEMP%\pressing-selftest.log`).
