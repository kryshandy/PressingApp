# Contrat de l'API — Pressing Éclat v1

Ce document est **la référence** entre l'API (`src/Pressing.Api`), la bibliothèque cliente
(`src/Pressing.Client`), le site web (`src/Pressing.Web`) et l'application Desktop
(`src/Pressing.Desktop`). Toute nouvelle route doit y être ajoutée **dans la même PR**.

## Conventions

- Base : `http://localhost:5180` (variable `PRESSING_PUBLIC_API_URL`).
- JSON en camelCase, **énumérations sérialisées en texte** (`"WashAndIron"`, `"Ready"`…) via `JsonStringEnumConverter`.
- Dates : `DateTime` local, format ISO 8601.
- Authentification : `Authorization: Bearer <jwt>`. Le jeton contient la revendication `token_type` = `staff` ou `client`.
- Erreurs : `application/problem+json` (`ProblemDetails`) — le message lisible est dans `detail`
  (ou `message` pour les routes d'authentification / publiques).
  - `400` règle métier violée (`InvalidOperationException`, `ArgumentException`)
  - `401` non connecté · `403` rôle insuffisant · `404` introuvable · `409` conflit · `429` trop de requêtes.
- Politiques : `StaffOnly` (tout le personnel), `ManagerOrAdmin`, `AdminOnly`, `ClientOnly`.
- Tous les DTO sont dans `src/Pressing.Core/Dtos`.

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
| PUT | `/api/orders/{id}/items/{itemId}/status` | StaffOnly | `UpdateItemStatusRequest` | `OrderDto` |
| PUT | `/api/orders/{id}/status` | StaffOnly | `UpdateItemStatusRequest` (tous les articles) | `OrderDto` |
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
- Toutes les opérations sensibles sont journalisées dans `AuditLogs`.
