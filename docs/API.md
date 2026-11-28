# Contrat de l'API — Pressing Éclat version 2.0.0

Ce document est **la référence** entre l'API (`src/Pressing.Api`), la bibliothèque cliente
(`src/Pressing.Client`), le site web (`src/Pressing.Web`) et l'application Desktop
(`src/Pressing.Desktop`). Toute nouvelle route doit y être ajoutée **dans la même PR**.

## Conventions

- Base publique : `http://localhost:5180` par défaut (variable `PRESSING_PUBLIC_API_URL`, utilisée par le Desktop et le site).
  L'adresse d'écoute du serveur est `PRESSING_API_URL` (`http://0.0.0.0:5180` par défaut) : derrière un reverse proxy,
  l'API peut écouter en HTTP interne alors que les clients appellent une URL HTTPS publique. Les deux variables ne sont
  pas interchangeables.
- JSON en camelCase, **énumérations sérialisées en texte** (`"WashAndIron"`, `"Ready"`…) via `JsonStringEnumConverter`.
- Dates : `DateTime` en heure locale du serveur, format ISO 8601. Elles peuvent comporter un décalage (`+01:00`) :
  les clients doivent les traiter comme de l'heure locale (ne pas convertir en UTC).
- IP des clients : derrière le site web ou un reverse proxy, `X-Forwarded-For` et `X-Forwarded-Proto` ne sont pris en
  compte que si l'appelant est de confiance (boucle locale, ou adresses listées dans `PRESSING_TRUSTED_PROXIES`).
  Un seul hop est accepté (`ForwardLimit = 1`) et `X-Real-IP` n'est pas lu. La limitation de débit (`429`) porte sur
  l'adresse corrigée ; `X-Forwarded-Proto` permet aussi de marquer le cookie `Secure` quand le public est en HTTPS.
- Authentification : `Authorization: Bearer <jwt>`. Le jeton contient la revendication `token_type` = `staff` ou `client`.
- Erreurs : `application/problem+json` (`ProblemDetails`) — le message lisible est dans `detail`
  (ou `message` pour les routes d'authentification / publiques).
  - `400` règle métier violée (`InvalidOperationException`, `ArgumentException`)
  - `401` non connecté · `403` rôle insuffisant · `404` introuvable · `409` conflit · `429` trop de requêtes.
- Politiques : `StaffOnly` (tout le personnel), `ManagerOrAdmin`, `AdminOnly`, `ClientOnly`.
- Tous les DTO sont dans `src/Pressing.Core/Dtos`.

## Découverte de l'API (anonyme)

| Méthode | Route | Paramètres / corps | Réponse | Statut | Authentification |
| --- | --- | --- | --- | --- | --- |
| GET | `/` | aucun / aucun | `{ name, version, documentation, health }` (voir ci-dessous) | `200 OK` | anonyme |
| GET | `/openapi/v1.json` | aucun / aucun | document OpenAPI au format JSON (`application/json`) | `200 OK` | anonyme |

`GET /` renvoie notamment :

```json
{
  "name": "Pressing Éclat API",
  "version": "2.0.0",
  "documentation": "/openapi/v1.json",
  "health": "/api/health"
}
```

Le chemin `/openapi/v1.json` est un chemin de contrat versionné : il ne doit pas être renommé lors d'un changement
de version du logiciel.

## Santé et public (anonyme)

| Méthode | Route | Corps / query | Réponse |
| --- | --- | --- | --- |
| GET | `/api/health` | | `HealthInfoDto` |
| GET | `/api/public/info` | | `{ shop: ShopInfo, categories: CategoryDto[], promotions: PromotionDto[], features: string[] }` |
| GET | `/api/public/catalog` | | `CategoryDto[]` |
| GET | `/api/public/tracking/{ticket}` | `?phone=` (obligatoire) | `TrackingDto` · 404 |
| POST | `/api/public/contact` | `{ name, email, phone?, message }` | `{ message }` |
| GET | `/api/public/unsubscribe` | `?token=` (lien des e-mails de rappel) | `{ active }` · 400 lien invalide ou expiré |
| GET | `/api/public/feedback` | `?token=` (lien d'avis, type `feedback`) | `FeedbackFormDto` (`ticketNumber`, `firstName`, `shopName`, `alreadyAnswered`, `note?`) · 400 lien invalide ou expiré |
| POST | `/api/public/feedback` | `{ token, note (1 à 5), comment? (≤ 1000 caractères) }` | `{ message }` · 400 note hors 1–5, commentaire trop long, lien invalide · 409 déjà répondu (une seule réponse par commande) |
| POST | `/api/public/unsubscribe` | `{ token }` | `{ message }` · 400 — passe le client en « aucun rappel » et date la révocation |
| GET | `/api/public/whatsapp/webhook` | `?hub.mode=subscribe&hub.verify_token&hub.challenge` (vérification Meta) | le `challenge` · 401 |
| POST | `/api/public/whatsapp/webhook` | notification Meta signée (`X-Hub-Signature-256`, `WHATSAPP_APP_SECRET`) | 200 · 401 signature invalide · 503 secret absent — « STOP » révoque le consentement WhatsApp |

Le lien de désinscription des e-mails ouvre la page **`/desabonnement?token=`** du site, qui appelle les deux routes
`unsubscribe`. Le jeton (`token_type = unsubscribe`, 180 jours) ne donne accès à aucune autre route.

Le lien d'enquête de satisfaction, envoyé 2 h après le retrait complet, ouvre la page **`/avis?token=`** du site, qui appelle
les deux routes `feedback`. Le jeton (`token_type = feedback`, 14 jours) désigne une commande et son client ; il est refusé
par toute autre route, y compris comme jeton de désabonnement. Les routes sont limitées en débit (30 requêtes/minute/IP).

## Authentification

| Méthode | Route | Politique | Corps | Réponse |
| --- | --- | --- | --- | --- |
| POST | `/api/auth/staff/login` | anonyme | `StaffLoginRequest` | `AuthResponse` · 401 `{message}` (identifiant inconnu et mot de passe faux : même message) · **423** `{message, lockedUntil}` compte verrouillé (5 échecs consécutifs = 15 min ; le message donne l'heure de déverrouillage) |
| POST | `/api/auth/staff/login/mfa` | anonyme, en-tête `Authorization: Bearer <jeton mfa-pending>` | `{ code }` (6 chiffres ou code de secours) | `AuthResponse` · 401 code faux/déjà utilisé/jeton expiré · 423 verrouillé |
| GET | `/api/auth/staff/2fa` | StaffOnly | | `{ enabled, recoveryCodesLeft, required, available }` |
| POST | `/api/auth/staff/2fa/setup` | StaffOnly (Manager) | | `{ secret, otpAuthUri, issuer, account }` · 400 pour un Admin ou un Caissier |
| POST | `/api/auth/staff/2fa/enable` | StaffOnly (Manager) | `{ code }` | `{ recoveryCodes[8] }` (affichés une seule fois) · 400 code faux |
| POST | `/api/auth/staff/2fa/disable` | StaffOnly (Manager) | `{ password, code }` | `MessageResponse` · 400 si l'exigence est active (seul l'administrateur peut alors la lever) |
| POST | `/api/auth/client/login` | anonyme | `ClientLoginRequest` (identifiant = téléphone ou e-mail) | `AuthResponse` · 401 |
| POST | `/api/auth/client/forgot` | anonyme | `ForgotPasswordRequest` | `MessageResponse` |
| POST | `/api/auth/client/reset` | anonyme | `ResetPasswordRequest` | `MessageResponse` |
| GET | `/api/auth/me` | connecté | | `{ type, staff? , client? }` |
| POST | `/api/auth/staff/change-password` | StaffOnly | `ChangeStaffPasswordRequest` | `MessageResponse` · 400 si le nouveau mot de passe a moins de 8 caractères, sans lettre ou sans chiffre |
| POST | `/api/auth/client/change-password` | ClientOnly | `ClientChangePasswordRequest` | `MessageResponse` |

## Clients (personnel)

| Méthode | Route | Politique | Corps / query | Réponse |
| --- | --- | --- | --- | --- |
| GET | `/api/clients` | StaffOnly | `?search&page=1&pageSize=25&includeInactive=false` | `PagedResult<ClientDto>` |
| GET | `/api/clients/lookup` | StaffOnly | `?search&take=20` | `ClientDto[]` |
| GET | `/api/clients/{id}` | StaffOnly | | `ClientDetailDto` · 404 |
| POST | `/api/clients` | StaffOnly | `CreateClientRequest` | `201 ClientDto` |
| PUT | `/api/clients/{id}` | StaffOnly | `UpdateClientRequest` | `ClientDto` |
| POST | `/api/clients/{id}/password` | StaffOnly | `SetClientPasswordRequest` | `ClientDto` |
| POST | `/api/clients/{id}/loyalty` | ManagerOrAdmin | `AdjustLoyaltyRequest { points, note }` | `ClientDto` |

`CreateClientRequest`, `UpdateClientRequest` et `UpdateProfileRequest` acceptent `notificationPreference`
(`None` · `WhatsApp` · `Email`). Choisir un canal date le consentement (`consentGivenAt`) ; revenir à `None` date la
révocation (`consentRevokedAt`). Un canal exige le numéro WhatsApp ou l'e-mail correspondant (400 sinon).

## Catalogue, tarifs et promotions

| Méthode | Route | Politique | Corps / query | Réponse |
| --- | --- | --- | --- | --- |
| GET | `/api/catalog` | StaffOnly | `?includeInactive=false` | `CategoryDto[]` |
| POST | `/api/catalog` | ManagerOrAdmin | `CreateCategoryRequest` | `201 CategoryDto` |
| PUT | `/api/catalog/{id}` | ManagerOrAdmin | `UpdateCategoryRequest` | `CategoryDto` |
| GET | `/api/promotions` | StaffOnly | `?includeInactive=true` | `PromotionDto[]` |
| POST | `/api/promotions` | ManagerOrAdmin | `CreatePromotionRequest` | `201 PromotionDto` |
| PUT | `/api/promotions/{id}` | ManagerOrAdmin | `UpdatePromotionRequest` | `PromotionDto` |
| POST | `/api/promotions/validate` | StaffOnly | `ValidatePromotionRequest` | `PromotionValidationDto` |

## Commandes (dépôts), statuts, paiements, retraits

| Méthode | Route | Politique | Corps / query | Réponse |
| --- | --- | --- | --- | --- |
| GET | `/api/orders` | StaffOnly | `?status&search&from&to&clientId&onlyReady&onlyOutstanding&includeCancelled=true&page=1&pageSize=30` | `PagedResult<OrderSummaryDto>` |
| GET | `/api/orders/ready` | StaffOnly | `?take=30` | `OrderSummaryDto[]` |
| GET | `/api/orders/outstanding` | StaffOnly | `?take=30` | `OrderSummaryDto[]` (impayés) |
| GET | `/api/orders/{id}` | StaffOnly | | `OrderDto` · 404 |
| GET | `/api/orders/ticket/{ticket}` | StaffOnly | | `OrderDto` · 404 |
| POST | `/api/orders/quote` | StaffOnly | `OrderQuoteRequest` | `OrderQuoteDto` |
| POST | `/api/orders` | StaffOnly | `CreateOrderRequest` | `201 OrderDto` |
| PUT | `/api/orders/{id}/items/{itemId}/status` | StaffOnly | `UpdateItemStatusRequest` — `status` ∈ `Deposited`, `InProgress`, `Ready` uniquement | `OrderDto` |
| PUT | `/api/orders/{id}/status` | StaffOnly | `UpdateItemStatusRequest` (tous les articles modifiables) — mêmes valeurs | `OrderDto` |
| POST | `/api/orders/{id}/payments` | StaffOnly | `AddPaymentRequest` | `PaymentDto` |
| POST | `/api/orders/{id}/refunds` | ManagerOrAdmin | `RefundRequest` | `PaymentDto` |
| POST | `/api/orders/{id}/pickup` | StaffOnly | `PickupRequest` (retrait total ou partiel + encaissement) | `OrderDto` |
| POST | `/api/orders/{id}/cancel` | ManagerOrAdmin | `CancelOrderRequest` | `OrderDto` |
| GET | `/api/orders/{id}/receipt` | StaffOnly | | `application/pdf` |
| POST | `/api/orders/{id}/receipt/email` | StaffOnly | | `MessageResponse` |
| GET | `/api/payments` | StaffOnly | `?from&to&method&search&page=1&pageSize=50` | `PagedResult<PaymentDto>` (caisse) |

## Espace client (portail)

| Méthode | Route | Politique | Corps | Réponse |
| --- | --- | --- | --- | --- |
| GET | `/api/portal/dashboard` | ClientOnly | | `PortalDashboardDto` |
| GET | `/api/portal/orders` | ClientOnly | | `OrderSummaryDto[]` |
| GET | `/api/portal/orders/{id}` | ClientOnly | | `OrderDto` · 404 (jamais la commande d'un autre client) |
| GET | `/api/portal/orders/{id}/receipt` | ClientOnly | | `application/pdf` |
| GET | `/api/portal/profile` | ClientOnly | | `ClientDto` |
| PUT | `/api/portal/profile` | ClientOnly | `UpdateProfileRequest` (`NotificationPreference` : `None`, `WhatsApp` ou `Email` ; le consentement est daté à l'enregistrement, 400 si l'e-mail ou le numéro WhatsApp manque) | `ClientDto` |
| GET | `/api/portal/loyalty` | ClientOnly | | `LoyaltyTransactionDto[]` |

## Tableau de bord, notifications, rapports

| Méthode | Route | Politique | Query | Réponse |
| --- | --- | --- | --- | --- |
| GET | `/api/dashboard` | StaffOnly | | `DashboardDto` |
| GET | `/api/notifications` | StaffOnly | `?take=20` | `NotificationDto[]` |
| GET | `/api/notifications/unread-count` | StaffOnly | | `{ count }` |
| POST | `/api/notifications/{id}/read` | StaffOnly | | `204` |
| POST | `/api/notifications/read-all` | StaffOnly | | `204` |
| GET | `/api/reports` | ManagerOrAdmin | `?from&to` (défaut : 30 derniers jours) | `ReportDto` |
| GET | `/api/reports/export` | ManagerOrAdmin | `?from&to` | `text/csv` |

## Clôture de caisse

« Z de caisse » journalier : les montants attendus sont calculés depuis les paiements et remboursements du jour
(net par mode : espèces, Mobile Money, carte, virement, autre). Espèces attendues en caisse = fond de caisse + espèces
nettes ; **écart = espèces comptées − espèces attendues en caisse**. Un écart non nul impose un commentaire.
Une seule clôture validée par jour (index unique) ; une clôture validée n'est plus modifiable (400) ; création,
modification et validation sont écrites dans le journal d'audit.

| Méthode | Route | Politique | Corps / query | Réponse |
| --- | --- | --- | --- | --- |
| GET | `/api/cash-closings/preview` | StaffOnly | `?date=` (défaut : aujourd'hui) | `CashClosingPreviewDto` (rien n'est enregistré) |
| POST | `/api/cash-closings` | StaffOnly | `CashClosingRequest` (`date`, `openingFloat`, `countedCash`, `comment`) | `201 CashClosingDto` (brouillon) · 400 si la caisse du jour est déjà clôturée |
| PUT | `/api/cash-closings/{id}` | StaffOnly | `CashClosingRequest` | `CashClosingDto` (brouillon uniquement) · 400 si validée |
| POST | `/api/cash-closings/{id}/validate` | ManagerOrAdmin | | `CashClosingDto` (attendus recalculés puis figés) · 403 pour un caissier |
| GET | `/api/cash-closings` | ManagerOrAdmin | `?from&to` (défaut : 90 derniers jours) | `CashClosingDto[]` |
| GET | `/api/cash-closings/{id}` | StaffOnly | | `CashClosingDto` · 404 |
| GET | `/api/cash-closings/{id}/pdf` | StaffOnly | | `application/pdf` |

## Rappels clients (responsable / administrateur)

Les rappels automatiques (WhatsApp / e-mail) ne partent qu'avec un consentement daté et non révoqué. Ces routes
exposent leur journal et permettent une relance manuelle. Méthodes du client : `PressingApiClient.Reminders.cs`.

| Méthode | Route | Politique | Query / corps | Réponse |
| --- | --- | --- | --- | --- |
| GET | `/api/reminders` | ManagerOrAdmin | `?page&pageSize&type&channel&from&to` — `type` : `ready`, `uncollected-j3`, `uncollected-j7`, `uncollected-j14`, `unpaid-balance`, `due-tomorrow`, `manual`, `feedback` ; `channel` : `whatsapp` ou `email` ; `from`/`to` inclusifs | `PagedResult<ReminderLogDto>` (ticket, client, type, canal, date, succès, erreur), du plus récent au plus ancien |
| POST | `/api/reminders/orders/{orderId}/send` | ManagerOrAdmin | | `ReminderLogDto` · 400 sans consentement (« … n'a pas consenti aux rappels … »), commande terminée/annulée, coordonnée du canal manquante ou échec d'envoi · 404 commande inconnue — journalisé (`manual`) et audité (`reminder-send`) |

### Double authentification (TOTP) et verrouillage

- **Rôle concerné : Manager uniquement.** Administrateurs et caissiers n'ont pas de double authentification.
- Réglage `Security.RequireManagerTwoFactor` (Paramètres → Sécurité, AdminOnly, **activé par défaut**, audité `2fa-require` /
  `2fa-unrequire`). Activé : un manager ayant la 2FA reçoit, après un mot de passe correct, un jeton intermédiaire
  (`AuthResponse.mfaRequired = true`, `token_type = mfa-pending`, 5 minutes, sans rôle) à compléter par
  `POST /api/auth/staff/login/mfa`. Le jeton intermédiaire est refusé par `TokenRevalidation` pour toute autre route (401).
  Un manager qui n'a pas encore la 2FA se connecte normalement et reçoit `twoFactorSetupRequired = true` : le Desktop lui impose
  l'activation (QR code, premier code, codes de secours) avant d'ouvrir l'application. Désactivé : connexion par mot de passe seul.
- TOTP RFC 6238 : HMAC-SHA1, 30 s, 6 chiffres, fenêtre ±1 pas, un même code ne sert qu'une fois à la connexion. URI
  `otpauth://totp/Pressing%20%C3%89clat:<identifiant>?secret=…&issuer=Pressing%20%C3%89clat`. Le secret est chiffré (ASP.NET
  Core Data Protection, clés dans `dataprotection-keys/` à côté de la base) ; les 8 codes de secours sont stockés hachés (SHA-256)
  et à usage unique.
- Les échecs de code comptent comme des échecs de connexion (5 = verrouillage de 15 minutes).
- Audit : `lockout`, `unlock`, `2fa-enable`, `2fa-disable`, `2fa-reset`, `2fa-require`, `2fa-unrequire`.

## Satisfaction (responsable / administrateur)

| Méthode | Route | Politique | Corps / query | Réponse |
| --- | --- | --- | --- | --- |
| GET | `/api/feedback` | ManagerOrAdmin | `?page=1&pageSize=25&days` (`days` : moyenne et nombre limités aux N derniers jours) | `FeedbackPageDto` : `items[]` (ticket, client, note, commentaire, date), `total`, `average`, `count` |

Une demande d'avis part une seule fois (journal `ReminderLogs`, type `feedback`), 2 h après le retrait complet d'une commande
(statut `Completed`, au plus 48 h plus tard), entre 8 h et 20 h, uniquement si le client a consenti (WhatsApp ou e-mail).

## Administration

| Méthode | Route | Politique | Corps / query | Réponse |
| --- | --- | --- | --- | --- |
| GET | `/api/staff` | AdminOnly | | `StaffUserDto[]` |
| POST | `/api/staff` | AdminOnly | `CreateStaffRequest` | `201 StaffUserDto` |
| PUT | `/api/staff/{id}` | AdminOnly | `UpdateStaffRequest` | `StaffUserDto` |
| POST | `/api/staff/{id}/password` | AdminOnly | `StaffPasswordRequest` | `MessageResponse` · 400 politique de mot de passe (8 caractères, une lettre, un chiffre) |
| POST | `/api/staff/{id}/unlock` | AdminOnly | | `MessageResponse` — remet les échecs à zéro, audité (`unlock`) |
| POST | `/api/staff/{id}/two-factor/reset` | AdminOnly | | `MessageResponse` — efface secret et codes de secours, audité (`2fa-reset`) |
| GET | `/api/settings` | ManagerOrAdmin | | `SettingsDto` |
| PUT | `/api/settings` | AdminOnly | `UpdateSettingsRequest` | `SettingsDto` |
| POST | `/api/settings/backup` | AdminOnly | | `BackupResultDto` |
| GET | `/api/settings/backups` | AdminOnly | | `BackupResultDto[]` |
| GET | `/api/emails` | ManagerOrAdmin | `?page=1&pageSize=30&status` | `PagedResult<EmailMessageDto>` |
| POST | `/api/emails/test` | ManagerOrAdmin | `TestEmailRequest` | `EmailMessageDto` |
| POST | `/api/emails/{id}/retry` | ManagerOrAdmin | | `EmailMessageDto` |
| GET | `/api/audit` | AdminOnly | `?page=1&pageSize=50&search` | `PagedResult<AuditLogDto>` |

## Assistant vocal (v3 — lot M1/M2)

> **Statut : routes implémentées (T2.3), contrat figé (T2.1).** Les DTO sont dans `src/Pressing.Core/Dtos/AssistantDtos.cs`.
> Principe non négociable : **l'IA ne touche jamais la base**. Ces routes ne créent aucune commande, aucun paiement,
> aucun retrait : elles renvoient un **brouillon** que le caissier vérifie, puis valide par les routes existantes
> (`POST /api/orders`, retraits, paiements, statuts).

| Méthode | Route | Politique | Corps / query | Réponse |
| --- | --- | --- | --- | --- |
| GET | `/api/assistant/status` | StaffOnly | | `AssistantStatusDto` (activé ? moteurs prêts ? seuil de confiance, message) — répond même si l'assistant est désactivé |
| POST | `/api/assistant/transcribe` | StaffOnly | Corps **brut** `audio/wav`, **2 Mo maximum** | `AssistantTranscriptionDto` — l'audio n'est jamais stocké |
| POST | `/api/assistant/interpret` | StaffOnly | `AssistantInterpretRequest` (`text`, 1000 caractères maximum) | `OrderDraftDto` |

Codes de réponse :

| Code | Cas |
| --- | --- |
| `200` | Succès. |
| `400` | Fichier de plus de 2 Mo, vide ou qui n'est pas un WAV (en-tête `RIFF`/`WAVE`) ; texte vide ou de plus de 1000 caractères. |
| `401` / `403` | Pas de jeton / jeton non « personnel » (un client du portail reçoit `403`). |
| `429` | Plus de 30 requêtes par minute et par utilisateur (limitation de débit). |
| `503` | Assistant désactivé (`ASSISTANT_ENABLED=false`), moteurs non installés ou arrêtés : « L'assistant vocal n'est pas démarré ». |

Chaque appel aboutissant est journalisé (`AuditService.LogAsync("assistant", …)`) avec le **texte** uniquement, jamais l'audio.
Réglages : `ASSISTANT_ENABLED` (défaut `true`) et `ASSISTANT_CONFIDENCE_THRESHOLD` (défaut `0.6`), voir `.env.example`.

### Contrat d'intention (sortie du modèle de langage → `AssistantIntentDto`)

JSON en **snake_case**, produit sous contrainte de grammaire par le membre 1 ; un champ inconnu vaut `null` et n'est
jamais inventé.

```json
{
  "intent": "create_order | register_pickup | add_payment | update_item_status | find_order | unknown",
  "client": { "name": "Ngo Marie" | null, "phone": "699112233" | null },
  "items": [ { "category": "Chemise", "service": "Lavage + repassage", "quantity": 3, "express": false } ],
  "ticket": "PRESS-2026-00142" | null,
  "amount": 5000 | null, "payment_method": "cash | mobile_money | card | transfer" | null,
  "deposit": 5000 | null, "pickup_date": "2026-12-02" | null,
  "item_status": "in_progress | ready" | null,
  "confidence": 0.0-1.0, "transcript": "texte reconnu"
}
```

| Champ | Règle |
| --- | --- |
| `intent` | Une des 6 valeurs ci-dessus ; toute autre valeur est traitée comme `unknown`. |
| `items[].category` / `service` | Texte libre entendu (« costard »). **Le backend** le rapproche du catalogue (synonymes, fautes) ; il refuse ce qui est absent du catalogue et le signale en avertissement au lieu de deviner. |
| `payment_method` | `transfer` correspond à `BankTransfer`. Valeur inconnue → champ ignoré et surligné. |
| `item_status` | `in_progress` ou `ready` uniquement (`PickedUp` passe par le retrait, `Cancelled` par l'annulation). |
| `pickup_date` | Jour calendaire `AAAA-MM-JJ`, déjà résolu par le modèle (« mardi » → date). |
| `confidence` | En dessous de **0,6** (réglable), ou si `intent` = `unknown` : `target` = `NotUnderstood`, le Desktop affiche « je n'ai pas compris » et la transcription. |

### Brouillon validé (`OrderDraftDto`, réponse de `/api/assistant/interpret`)

JSON camelCase habituel de l'API, énumérations en texte. Le brouillon n'est **jamais enregistré**.

| Champ | Rôle |
| --- | --- |
| `intent`, `confidence`, `transcript` | Repris de l'intention ; la transcription est affichée à l'écran. |
| `target` | Écran à ouvrir pré-rempli : `NotUnderstood`, `NewOrder`, `Pickup`, `Payment`, `UpdateStatus`, `FindOrder`. |
| `client` | `clientId` si retrouvé (téléphone normalisé, puis nom approché) ; `isNew` = `true` si inconnu (création proposée) ; `candidates` = homonymes à départager. |
| `items[]` | `categoryId`, `serviceType`, prix du catalogue ; `categoryId` / `serviceType` à `null` si introuvables ; `isUncertain` = surlignage orange. |
| `orderId`, `ticket`, `balance` | Commande retrouvée par son ticket (retrait, paiement, statut, recherche). |
| `paymentAmount`, `paymentMethod`, `depositAmount`, `dueDate`, `itemStatus` | Valeurs dictées, après contrôle (montant supérieur au solde, etc.). |
| `quote` | `OrderQuoteDto` calculé par `CatalogService` pour les articles reconnus. |
| `uncertainFields` | Noms stables des champs à surligner en orange : `client`, `items[0].category`, `items[0].service`, `ticket`, `amount`, `paymentMethod`, `deposit`, `dueDate`, `itemStatus`. |
| `warnings` | Messages lisibles en français (« Client inconnu : création proposée »). |

## Atelier (étiquettes QR et scan)

| Méthode | Route | Politique | Corps / query | Réponse |
| --- | --- | --- | --- | --- |
| GET | `/api/orders/{id}/labels` | StaffOnly | | `application/pdf` (planche A4, 3 × 8 étiquettes) · 404 |
| POST | `/api/workshop/scan` | StaffOnly | `WorkshopScanRequest` — `code` | `WorkshopScanResultDto` · 400 (code illisible, article déjà prêt / retiré / annulé, pièce secondaire) · 404 (ticket ou article inconnu) |
| POST | `/api/workshop/undo` | StaffOnly | `WorkshopUndoRequest` — `code`, `fromStatus` (statut obtenu par le scan à annuler) | `WorkshopScanResultDto` · 400 si l'article n'est plus à `fromStatus` ou est déjà `Deposited` |
| GET | `/api/orders/{id}/receipt/escpos` | StaffOnly | | `application/octet-stream` : octets ESC/POS du ticket 80 mm (48 colonnes, CP858, QR natif `GS ( k`, coupe `GS V`, tiroir `ESC p` si réglé) · 404 |
| GET | `/api/printing/ticket-printer` | StaffOnly | | `TicketPrinterSettingsDto` — `printerName`, `enabled`, `openCashDrawer` (lisible par tout le personnel) |

L'impression elle-même est faite par le Desktop (winspool, type RAW) sur l'imprimante nommée dans le réglage
`Printer.TicketName` (vide = désactivée) ; `Printer.OpenCashDrawer` (`true`/`false`) ajoute l'impulsion d'ouverture du
tiroir-caisse. Ces deux réglages se modifient par `PUT /api/settings` (ManagerOrAdmin), sans migration (table `AppSettings`).

**Format du code d'étiquette** (dérivé du ticket, aucune colonne en base) : `PRESS-AAAA-NNNNN-II` = ticket + rang de la
ligne d'article (2 chiffres, lignes triées par identifiant). Une ligne de quantité > 1 produit une étiquette par pièce
avec un suffixe en minuscules : `PRESS-2026-00042-01-a`, `-b`, `-c` (puis `-aa` après `-z`). Décodage :
`ItemCodes.TryParseItemCode` (Core) ; casse, espaces et tirets typographiques tolérés.

Règles du scan : chaque scan fait avancer **la ligne** d'une étape (Déposé → En traitement → Prêt) via
`OrderService.UpdateItemStatusAsync` ; quand le dernier article passe à « Prêt », la notification « Commande prête » et
l'e-mail partent comme pour un changement manuel. Le statut étant porté par la ligne, seule l'étiquette sans suffixe ou
la pièce `-a` fait avancer ; les pièces `-b`, `-c`… sont refusées sans changement (évite d'avancer trois fois une même ligne).
Un article « Prêt » ou « Retiré » est refusé (400). L'annulation recule d'**une seule** étape et n'agit que si l'article est
toujours au statut `fromStatus` ; un retour en arrière ne retire pas l'e-mail « prête » déjà mis en file.
Chaque scan et chaque annulation est inscrit dans le journal d'audit (`scan`, `scan-undo`).

## Règles métier clés (implémentées dans `Pressing.Core/Services`)

- Ticket unique généré à la création d'une commande.
- Prix repris de la grille tarifaire (catégorie × type de service) ; supplément express par ligne.
- Total = sous-total + express + livraison − remise − promotion − fidélité.
- `PaidAmount` = somme des paiements − remboursements ; solde = `Total − PaidAmount`.
- Statut de commande déduit des statuts des articles (Déposé → En traitement → Prêt → Partiellement retiré → Terminé).
- Retrait partiel possible ; retrait avec solde impayé refusé sauf si le paramètre `Orders.AllowPickupWithBalance` vaut `true`.
- Changement de statut manuel : seuls `Deposited`, `InProgress` et `Ready` sont acceptés (`PickedUp` passe par le retrait,
  `Cancelled` par l'annulation → 400 « Utilisez le retrait ou l'annulation pour ce statut. »). Un article retiré (même
  partiellement) ou annulé ne peut plus être modifié (400) ; la route « tous les articles » les ignore. Le retour
  `Ready` → `InProgress` reste possible tant que rien n'a été retiré.
- Retrait : les lignes `PickupRequest.Items` portant le même `orderItemId` sont additionnées ; chaque quantité doit être > 0 et le total ne peut dépasser le restant.
- Annulation : un remboursement est généré par mode de paiement d'origine (net des remboursements déjà faits) ; le code promotionnel utilisé est libéré (`UsedCount` − 1).
- Téléphones : forme canonique ivoirienne (chiffres uniquement, sans `00` ni `225` devant un numéro à 10 chiffres) ;
  « +225 05 55 66 77 88 », « 00225 0555667788 » et « 05 55 66 77 88 » désignent le même client.
- Limites de saisie (400) : quantité 1..1000, prix unitaire, frais de livraison, remise, acompte, paiements et remboursements 0..10 000 000 (paiements/remboursements > 0).
  La remise manuelle ne peut pas dépasser le montant restant après promotion.
- Tickets : comparaison insensible à la casse et aux espaces de bordure.
- Toutes les opérations sensibles sont journalisées dans `AuditLogs`.
