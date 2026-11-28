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

## Authentification

| Méthode | Route | Politique | Corps | Réponse |
| --- | --- | --- | --- | --- |
| POST | `/api/auth/staff/login` | anonyme | `StaffLoginRequest` | `AuthResponse` · 401 `{message}` |
| POST | `/api/auth/client/login` | anonyme | `ClientLoginRequest` (identifiant = téléphone ou e-mail) | `AuthResponse` · 401 |
| POST | `/api/auth/client/forgot` | anonyme | `ForgotPasswordRequest` | `MessageResponse` |
| POST | `/api/auth/client/reset` | anonyme | `ResetPasswordRequest` | `MessageResponse` |
| GET | `/api/auth/me` | connecté | | `{ type, staff? , client? }` |
| POST | `/api/auth/staff/change-password` | StaffOnly | `ChangeStaffPasswordRequest` | `MessageResponse` |
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
| PUT | `/api/portal/profile` | ClientOnly | `UpdateProfileRequest` | `ClientDto` |
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

## Administration

| Méthode | Route | Politique | Corps / query | Réponse |
| --- | --- | --- | --- | --- |
| GET | `/api/staff` | AdminOnly | | `StaffUserDto[]` |
| POST | `/api/staff` | AdminOnly | `CreateStaffRequest` | `201 StaffUserDto` |
| PUT | `/api/staff/{id}` | AdminOnly | `UpdateStaffRequest` | `StaffUserDto` |
| POST | `/api/staff/{id}/password` | AdminOnly | `StaffPasswordRequest` | `MessageResponse` |
| GET | `/api/settings` | ManagerOrAdmin | | `SettingsDto` |
| PUT | `/api/settings` | AdminOnly | `UpdateSettingsRequest` | `SettingsDto` |
| POST | `/api/settings/backup` | AdminOnly | | `BackupResultDto` |
| GET | `/api/settings/backups` | AdminOnly | | `BackupResultDto[]` |
| GET | `/api/emails` | ManagerOrAdmin | `?page=1&pageSize=30&status` | `PagedResult<EmailMessageDto>` |
| POST | `/api/emails/test` | ManagerOrAdmin | `TestEmailRequest` | `EmailMessageDto` |
| POST | `/api/emails/{id}/retry` | ManagerOrAdmin | | `EmailMessageDto` |
| GET | `/api/audit` | AdminOnly | `?page=1&pageSize=50&search` | `PagedResult<AuditLogDto>` |

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

## Intégration de l'assistant vocal (Membre 1 → Membre 2)

Aucune route d'assistant n'est définie ici : l'API et l'UI définitives appartiennent au **Membre 2**. Cette section
décrit uniquement les points d'entrée déjà disponibles dans `Pressing.Core/Assistant/`, afin d'éviter un second
contrat JSON concurrent.

- `ISpeechToText.TranscribeAsync(Stream wav, CancellationToken)` — WAV 16 kHz mono PCM 16 bits en mémoire ; renvoie
  `SpeechTranscription` (`Text`, `DurationSeconds`, `Rms`, `SilenceDetected`). Les silences et les formats invalides
  sont rejetés avant tout appel réseau ; 20 secondes maximum. `IsReadyAsync(CancellationToken)` interroge `/health`.
- `IIntentExtractor.ExtractAsync(string texte, CatalogHints catalog, CancellationToken)` — renvoie
  `IntentExtraction` (`RawJson`, `Payload`, `Warnings`). Le catalogue est fourni par l'appelant (lecture seule,
  par exemple `GET /api/public/catalog`) : le moteur n'accède jamais à SQLite. `IsReadyAsync(CancellationToken)`
  interroge également `/health`.
- Façade M2 (aucune route ajoutée ici) : `IM2SpeechToText.TranscribeAsync(Stream wav, CancellationToken)` renvoie
  `Task<string>`, et `IM2IntentExtractor.ExtractAsync(string text, AssistantCatalogHints hints, CancellationToken)`
  renvoie `Task<AssistantIntentDto>`. Le DTO officiel est dans `Pressing.Core.Dtos` et porte exactement les mêmes
  noms JSON snake_case. `AssistantClientFactory.CreateM2SpeechToText()` et `CreateM2IntentExtractor()` exposent ces
  façades. `M2SpeechToTextAdapter` et `M2IntentExtractorAdapter` permettent de consommer ces façades dans le pipeline M1
  sans dupliquer les types internes. `AssistantUnavailableException` dérive de `AssistantException` et est traitée
  comme `EngineUnavailable` par `LocalAssistantPipeline`.
- `AssistantCatalogHints(Categories, Services)` est la forme réduite demandée par M2. Pour les dates relatives et les
  synonymes, le pipeline M1 complet doit recevoir `CatalogHints(Categories, Services, ReferenceDate)` avec des
  `CatalogTerm` : la façade M2 ne doit pas être utilisée pour contourner ces garde-fous.
- `LocalAssistantPipeline.ProcessAsync(Stream wav, CatalogHints catalog, CancellationToken)` — chaîne les deux étapes et
  renvoie `PipelineResult` (`Ok`, `NotUnderstood`, `Silence`, `EngineUnavailable`, `InvalidAudio`).
- `AssistantClientFactory.CreatePipeline()` construit les clients HTTP avec les variables `ASSISTANT_*` documentées
  dans `.env.example`. Les URL sont forcées en boucle locale.
- `tools/assistant/consultation.ps1 -Texte "..."` (ou `-Wav`) illustre T1.6 : intention `find_order` lue à l'écran, sans accès SQLite ni création de commande ; le résultat reste un brouillon à confirmer.
- `AssistantMessages.EngineNotStarted` contient exactement « L'assistant vocal n'est pas démarré ».

Contrat de sortie figé : `IntentPayload` dans `AssistantContracts.cs` et son miroir officiel
`AssistantIntentDto` dans `Pressing.Core.Dtos`, reliés par un mapping explicite (`ToDto` / `FromDto`).

```json
{
  "intent": "create_order | register_pickup | add_payment | update_item_status | find_order | unknown",
  "client": { "name": "Ngo Marie" | null, "phone": "699112233" | null },
  "items": [{ "category": "Chemise", "service": "Lavage + repassage", "quantity": 3, "express": false }],
  "ticket": "PRESS-2026-00142" | null,
  "amount": 5000 | null,
  "payment_method": "cash | mobile_money | card | transfer" | null,
  "deposit": 5000 | null,
  "pickup_date": "2026-12-02" | null,
  "item_status": "in_progress | ready" | null,
  "confidence": 0.0,
  "transcript": "texte reconnu"
}
```

Règles que le Membre 2 doit conserver : champ inconnu = `null` (aucune valeur métier par défaut), confiance < `0,6`
ou `unknown` ⇒ « je n'ai pas compris » + transcription, catégories/services validés contre le catalogue, et surtout
**aucune opération métier déclenchée directement** : le JSON est un brouillon à confirmer, à enregistrer via les
routes existantes (`POST /api/orders`, `/api/orders/{id}/payments`, `/api/orders/{id}/status`, ...).

`tools/assistant/RESULTATS.md` distingue les mesures réellement exécutées : banc live synthétique de 150 appels
(150/150 intentions, 85/85 articles, 0 erreur, p95 11,886 s), recalcul hors ligne et trois phrases TTS. Aucun WER
réel n'est publié : `dataset.jsonl` est absent et `bench.ps1` s'arrête sans écrire de résultat. Les seuils attendus
restent ≥ 90 % d'intentions exactes, ≥ 85 % d'articles exacts et p95 < 5 s.
