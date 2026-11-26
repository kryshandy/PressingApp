# Charte de mise en page — Pressing Éclat Desktop

Objectif : une interface **équilibrée, symétrique et calme**, au niveau d'un logiciel de caisse premium.
Chaque écran doit pouvoir se lire en 2 secondes : *où suis-je, que dois-je faire, quel est le chiffre clé*.
Cette charte est **obligatoire** pour tout nouvel écran.

## 1. Grille et alignements

- **Une seule gouttière de page : 32 px** à gauche et à droite (via `Theme.S`). L'en-tête global (recherche, cloche, utilisateur), le titre de page, les cartes et les barres d'actions partagent **exactement les mêmes bords gauche et droit**. Aucun élément ne « décroche » de quelques pixels.
- **Grille de 12 colonnes** sur la zone de contenu, gouttière inter-colonnes **24 px**, espacement vertical entre blocs **24 px**, entre un titre de carte et son contenu **16 px**. Toutes les valeurs sont des multiples de 8 (4 toléré pour les micro-ajustements).
- Des cartes placées **sur une même ligne ont la même hauteur** et leurs titres sont alignés sur la même ligne de base.
- Proportions autorisées par ligne : 12 · 6+6 · 4+4+4 · 3+3+3+3 · 8+4 · 4+8. Pas de 7+5 ni de largeurs « au feeling ».
- Padding interne des cartes : **24 px** partout (20 px toléré pour les cartes KPI).

## 2. Défilement

- **Un seul défilement par écran** : celui de la zone de contenu de la page. Jamais de carte qui défile à l'intérieur d'une page qui défile, sauf une grille de données qui remplit sa carte.
- **Aucune barre de défilement native Windows visible** (ni verticale grise, ni horizontale) : barre fine thémée (6 px, `#334155` sur fond transparent, visible au survol) ou défilement à la molette seulement. Une grille ne doit **jamais** afficher de barre horizontale : les colonnes se répartissent la largeur (`Fill` avec `FillWeight`, colonnes numériques à largeur fixe).
- La barre de défilement, quand elle existe, **ne mange pas la gouttière** : la largeur utile reste identique avec ou sans défilement.

## 3. Hiérarchie typographique

| Rôle | Taille | Graisse | Couleur |
| --- | --- | --- | --- |
| Titre de page | 22 pt | Semibold | `#F1F5F9` |
| Sous-titre de page | 10 pt | Regular | `#94A3B8` |
| Titre de carte | 12 pt | Semibold | `#E2E8F0` |
| Libellé de champ / en-tête de colonne | 8.5 pt, MAJUSCULES, interlettrage | Semibold | `#94A3B8` |
| Texte courant | 10 pt | Regular | `#E2E8F0` |
| Chiffre clé (KPI, total) | 22–26 pt | Semibold | `#F8FAFC` ou laiton `#C8A15A` pour le total à payer |

Un écran a **un seul** élément en laiton dominant (l'action principale ou le total) : le laiton ne sert pas de décoration.

## 4. Composants

- **Boutons** : hauteur unique 40 px (32 px dans les barres d'outils de cartes). Une seule action Primary (laiton) par zone ; les autres en Secondary. Les barres d'actions ne passent **jamais** à la ligne : au-delà de 4 actions, les secondaires vont dans un bouton « Plus d'actions ▾ ».
- **Champs** : hauteur 40 px, texte d'aide (placeholder) **visible** quand le champ est vide (« Rechercher un ticket, un client… », « Nom, téléphone ou e-mail »). Libellé au-dessus, 6 px d'écart.
- **Cases à cocher** : jamais la case Windows native — utiliser `ToggleBox` (interrupteur) ou une case dessinée au thème.
- **Pastilles de statut** : même largeur minimale dans une colonne, centrées verticalement, couleurs `Fmt.StatusColor`.
- **Grilles** : lignes 44 px, en-tête 40 px, texte tronqué avec « … » uniquement en dernier recours ; colonnes prioritaires (nom, montant, statut) jamais tronquées à 1280 px de large. Montants alignés à droite, police tabulaire si possible.
- **États vides** : icône + phrase + action (« Aucun article. Ajoutez le premier article ci-dessus. »), centrés dans la carte.

## 5. Gabarits d'écrans de référence

- **Tableau de bord** : ligne KPI 3+3+3+3 (4 cartes égales) puis ligne 3+3+3+3 ou 6 KPI en 2 lignes de 3 **de même hauteur** ; ligne 8+4 (graphique 7 jours + répartition par statut, même hauteur, graphique jamais rogné) ; ligne 6+6 (commandes récentes / prêtes à retirer).
- **Nouveau dépôt** : 8+4. Colonne gauche : carte Client (compacte, une ligne), carte Articles (ligne de saisie sur **une seule rangée** alignée avec les colonnes de la grille, puis grille qui occupe la hauteur restante). Colonne droite : **une seule carte « Récapitulatif » collante** contenant options, livraison, remise, code promo, fidélité, acompte, puis séparateur, totaux et bouton « Valider le dépôt » pleine largeur en bas. Pas de défilement interne.
- **Détail de commande** : en-tête (Retour aligné sur la gouttière, ticket, pastille de statut, client, dates) + barre d'actions sur une ligne ; puis 8+4 : gauche = Articles puis Paiements puis Retraits ; droite = carte « Résumé » (client, téléphone, total, payé, **solde en grand**, échéance, express, livraison).
- **Listes (Commandes, Clients, Caisse, Impayés, Tarifs, Promotions, Personnel)** : en-tête de page, barre de filtres sur une ligne (recherche à gauche en 6 colonnes, filtres à droite), carte grille pleine largeur qui prend toute la hauteur, pied de carte avec pagination à gauche et total à droite, alignés sur les bords de la carte. Pas de recherche en double avec la recherche globale sans libellé explicite.
- **Fiche client** : 4+8 : gauche = carte identité (avatar initiales, coordonnées, accès web, points) ; droite = KPI (commandes, total dépensé, solde) puis historique.
- **Paramètres** : navigation de sections verticale à gauche (3 colonnes) + formulaire à droite (9 colonnes) en grilles de champs 6+6.
- **Connexion** : 50/50 ; colonne formulaire centrée verticalement, largeur 400 px, titre, champs et bouton partageant le même bord gauche.

## 6. Fenêtre

- Barre de titre Windows **en mode sombre** (`DwmSetWindowAttribute`, `DWMWA_USE_IMMERSIVE_DARK_MODE`).
- Taille minimale 1280×760 ; rendu vérifié à 1280×760, 1600×900 et 1920×1080, à 100 % et 150 %.

## 6 bis. Implémentation

Les règles ci-dessus sont appliquées par les primitives de `Ui/Layout.cs` (`PageLayout`, `ScrollViewport`, `VStack`, `GridRow`, `ActionBar`, `TextLabel`…) — voir `README-DESKTOP.md`. Règles complémentaires :
- Les libellés de texte d'une page utilisent `TextLabel` (pas de marge GDI) pour que titre, champs et cartes partagent exactement le même bord gauche.
- Une seule action laiton par écran : les actions secondaires de ligne (Modifier, Mot de passe…) vont dans l'en-tête de la carte grille, en 32 px.
- Dans une barre d'actions alignée à droite, l'ordre va du moins important (à gauche) au plus important (Primary, à droite) : ce sont les actions de gauche qui débordent dans « Plus d'actions ».
- Dialogues : largeur 480 (message), 560 (formulaire) ou 760 (tableau) ; la hauteur suit le contenu.

## 7. Contrôle qualité visuel (avant chaque PR)

`PRESSING_DPI_UNAWARE=1 Pressing.Desktop.exe --selftest --screens <dossier>` génère une capture de chaque écran, de chaque dialogue et de la coque à 1280×760 et 1600×900 (ajouter `PRESSING_UI_SCALE=1.5` pour 150 %). Relire chaque capture avec cette liste :
bords alignés · cartes d'une ligne de même hauteur · aucune barre native · aucune troncature de texte clé · une seule action laiton · états vides soignés · aucun contrôle gris Windows.
