using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Pressing.Core;
using Pressing.Core.Dtos;

namespace Pressing.Client;

/// <summary>
/// Client HTTP typé de l'API Pressing (une méthode par route de <c>docs/API.md</c>).
/// <para>
/// <b>Desktop</b> : <c>var api = new PressingApiClient(url); await api.LoginStaffAsync(user, pwd);</c>
/// (le jeton est mémorisé automatiquement ; abonnez-vous à <see cref="Unauthorized"/> pour revenir à l'écran de connexion).
/// </para>
/// <para>
/// <b>Web (ASP.NET Core MVC)</b> : <c>services.AddHttpClient&lt;PressingApiClient&gt;(c =&gt; c.BaseAddress = new Uri(url));</c>
/// puis injection du client ; renseigner <see cref="Token"/> pour chaque requête (jeton du client connecté) : le
/// jeton est envoyé par requête, jamais sur les en-têtes par défaut du HttpClient.
/// </para>
/// <para><b>Règle : toute nouvelle route de docs/API.md doit avoir sa méthode ici.</b></para>
/// <para>Toutes les erreurs (HTTP ou réseau) sont levées sous forme de <see cref="ApiException"/> au message lisible en français.</para>
/// </summary>
public sealed partial class PressingApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    /// <summary>Crée son propre HttpClient (délai 30 s).</summary>
    public PressingApiClient(string baseUrl)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl + "/"), Timeout = TimeSpan.FromSeconds(30) };
        _ownsHttp = true;
    }

    /// <summary>Utilise un HttpClient fourni (IHttpClientFactory) ; il n'est pas libéré par Dispose.</summary>
    public PressingApiClient(HttpClient httpClient)
    {
        _http = httpClient;
        BaseUrl = httpClient.BaseAddress?.ToString().TrimEnd('/') ?? string.Empty;
    }

    public string BaseUrl { get; }

    /// <summary>Jeton JWT ; s'il est défini, il est envoyé en <c>Authorization: Bearer</c> à chaque requête.</summary>
    public string? Token { get; set; }

    public bool IsAuthenticated => !string.IsNullOrEmpty(Token);

    /// <summary>
    /// Adresse IP du visiteur final, transmise à l'API en <c>X-Forwarded-For</c> à chaque requête lorsqu'elle est définie
    /// (utile derrière le site web : la limitation de débit de l'API porte alors sur le visiteur et non sur le serveur web).
    /// L'API ne tient compte de l'en-tête que si l'appelant est un proxy de confiance (boucle locale ou <c>PRESSING_TRUSTED_PROXIES</c>).
    /// </summary>
    public string? ForwardedFor { get; set; }

    /// <summary>Déclenché lorsqu'une requête authentifiée reçoit un 401.</summary>
    public event EventHandler? Unauthorized;

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    #region Health / Public

    public Task<HealthInfoDto?> GetHealthAsync(CancellationToken ct = default) =>
        GetAsync<HealthInfoDto>("api/health", ct);

    public Task<PublicInfoDto?> GetPublicInfoAsync(CancellationToken ct = default) =>
        GetAsync<PublicInfoDto>("api/public/info", ct);

    public Task<List<CategoryDto>?> GetPublicCatalogAsync(CancellationToken ct = default) =>
        GetAsync<List<CategoryDto>>("api/public/catalog", ct);

    /// <summary>Suivi public d'une commande ; <c>null</c> si le ticket / téléphone ne correspond à rien (404).</summary>
    public async Task<TrackingDto?> TrackOrderAsync(string ticket, string phone, CancellationToken ct = default)
    {
        try
        {
            return await GetAsync<TrackingDto>(
                $"api/public/tracking/{Uri.EscapeDataString(ticket)}" + Query(("phone", phone)), ct);
        }
        catch (ApiException ex) when (ex.IsNotFound) { return null; }
    }

    /// <summary>Envoie un message de contact ; retourne le message de confirmation.</summary>
    public async Task<string> SendContactAsync(string name, string email, string? phone, string message, CancellationToken ct = default)
    {
        var r = await PostAsync<MessageResponse>("api/public/contact", new { name, email, phone, message }, ct, anonymous: true);
        return r?.Message ?? string.Empty;
    }

    /// <summary>Rappels e-mail encore actifs pour ce lien de désinscription (400 si le lien est invalide).</summary>
    public Task<UnsubscribeStatusDto?> GetUnsubscribeStatusAsync(string token, CancellationToken ct = default) =>
        GetAsync<UnsubscribeStatusDto>("api/public/unsubscribe" + Query(("token", token)), ct);

    /// <summary>Désactive les rappels e-mail ; retourne le message de confirmation.</summary>
    public async Task<string> UnsubscribeAsync(string token, CancellationToken ct = default)
    {
        var r = await PostAsync<MessageResponse>("api/public/unsubscribe", new UnsubscribeRequest(token), ct, anonymous: true);
        return r?.Message ?? string.Empty;
    }

    #endregion

    #region Auth

    public async Task<AuthResponse?> LoginStaffAsync(string username, string password, CancellationToken ct = default)
    {
        var r = await PostAsync<AuthResponse>("api/auth/staff/login", new StaffLoginRequest(username, password), ct, anonymous: true);
        if (r is not null) Token = r.Token;
        return r;
    }

    public async Task<AuthResponse?> LoginClientAsync(string identifier, string password, CancellationToken ct = default)
    {
        var r = await PostAsync<AuthResponse>("api/auth/client/login", new ClientLoginRequest(identifier, password), ct, anonymous: true);
        if (r is not null) Token = r.Token;
        return r;
    }

    public Task<MessageResponse?> ForgotPasswordAsync(string identifier, CancellationToken ct = default) =>
        PostAsync<MessageResponse>("api/auth/client/forgot", new ForgotPasswordRequest(identifier), ct, anonymous: true);

    public Task<MessageResponse?> ResetPasswordAsync(string token, string newPassword, CancellationToken ct = default) =>
        PostAsync<MessageResponse>("api/auth/client/reset", new ResetPasswordRequest(token, newPassword), ct, anonymous: true);

    public Task<MeDto?> GetMeAsync(CancellationToken ct = default) =>
        GetAsync<MeDto>("api/auth/me", ct);

    public Task<MessageResponse?> ChangeStaffPasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default) =>
        PostAsync<MessageResponse>("api/auth/staff/change-password", new ChangeStaffPasswordRequest(currentPassword, newPassword), ct);

    public Task<MessageResponse?> ChangeClientPasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default) =>
        PostAsync<MessageResponse>("api/auth/client/change-password", new ClientChangePasswordRequest(currentPassword, newPassword), ct);

    /// <summary>Oublie le jeton local (l'API est sans état).</summary>
    public void Logout() => Token = null;

    #endregion

    #region Clients

    public Task<PagedResult<ClientDto>?> SearchClientsAsync(string? search = null, int page = 1, int pageSize = 25,
        bool includeInactive = false, CancellationToken ct = default) =>
        GetAsync<PagedResult<ClientDto>>("api/clients" + Query(("search", search), ("page", page),
            ("pageSize", pageSize), ("includeInactive", includeInactive)), ct);

    public Task<List<ClientDto>?> LookupClientsAsync(string? search, int take = 20, CancellationToken ct = default) =>
        GetAsync<List<ClientDto>>("api/clients/lookup" + Query(("search", search), ("take", take)), ct);

    public Task<ClientDetailDto?> GetClientAsync(int id, CancellationToken ct = default) =>
        GetAsync<ClientDetailDto>($"api/clients/{id}", ct);

    public Task<ClientDto?> CreateClientAsync(CreateClientRequest request, CancellationToken ct = default) =>
        PostAsync<ClientDto>("api/clients", request, ct);

    public Task<ClientDto?> UpdateClientAsync(int id, UpdateClientRequest request, CancellationToken ct = default) =>
        PutAsync<ClientDto>($"api/clients/{id}", request, ct);

    public Task<ClientDto?> SetClientPasswordAsync(int id, SetClientPasswordRequest request, CancellationToken ct = default) =>
        PostAsync<ClientDto>($"api/clients/{id}/password", request, ct);

    public Task<ClientDto?> AdjustLoyaltyAsync(int id, int points, string note, CancellationToken ct = default) =>
        PostAsync<ClientDto>($"api/clients/{id}/loyalty", new { points, note }, ct);

    #endregion

    #region Catalog / Promotions

    public Task<List<CategoryDto>?> GetCatalogAsync(bool includeInactive = false, CancellationToken ct = default) =>
        GetAsync<List<CategoryDto>>("api/catalog" + Query(("includeInactive", includeInactive)), ct);

    public Task<CategoryDto?> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct = default) =>
        PostAsync<CategoryDto>("api/catalog", request, ct);

    public Task<CategoryDto?> UpdateCategoryAsync(int id, UpdateCategoryRequest request, CancellationToken ct = default) =>
        PutAsync<CategoryDto>($"api/catalog/{id}", request, ct);

    public Task<List<PromotionDto>?> GetPromotionsAsync(bool includeInactive = true, CancellationToken ct = default) =>
        GetAsync<List<PromotionDto>>("api/promotions" + Query(("includeInactive", includeInactive)), ct);

    public Task<PromotionDto?> CreatePromotionAsync(CreatePromotionRequest request, CancellationToken ct = default) =>
        PostAsync<PromotionDto>("api/promotions", request, ct);

    public Task<PromotionDto?> UpdatePromotionAsync(int id, UpdatePromotionRequest request, CancellationToken ct = default) =>
        PutAsync<PromotionDto>($"api/promotions/{id}", request, ct);

    public Task<PromotionValidationDto?> ValidatePromotionAsync(string code, decimal subtotal, CancellationToken ct = default) =>
        PostAsync<PromotionValidationDto>("api/promotions/validate", new ValidatePromotionRequest(code, subtotal), ct);

    #endregion

    #region Orders

    public Task<PagedResult<OrderSummaryDto>?> SearchOrdersAsync(OrderStatus? status = null, string? search = null,
        DateTime? from = null, DateTime? to = null, int? clientId = null, bool onlyReady = false,
        bool onlyOutstanding = false, bool includeCancelled = true, int page = 1, int pageSize = 30,
        CancellationToken ct = default) =>
        GetAsync<PagedResult<OrderSummaryDto>>("api/orders" + Query(("status", status), ("search", search),
            ("from", from), ("to", to), ("clientId", clientId), ("onlyReady", onlyReady),
            ("onlyOutstanding", onlyOutstanding), ("includeCancelled", includeCancelled),
            ("page", page), ("pageSize", pageSize)), ct);

    public Task<List<OrderSummaryDto>?> GetReadyOrdersAsync(int take = 30, CancellationToken ct = default) =>
        GetAsync<List<OrderSummaryDto>>("api/orders/ready" + Query(("take", take)), ct);

    public Task<List<OrderSummaryDto>?> GetOutstandingOrdersAsync(int take = 30, CancellationToken ct = default) =>
        GetAsync<List<OrderSummaryDto>>("api/orders/outstanding" + Query(("take", take)), ct);

    public Task<OrderDto?> GetOrderAsync(int id, CancellationToken ct = default) =>
        GetAsync<OrderDto>($"api/orders/{id}", ct);

    /// <summary>Commande par ticket ; <c>null</c> si introuvable (404).</summary>
    public async Task<OrderDto?> GetOrderByTicketAsync(string ticket, CancellationToken ct = default)
    {
        try { return await GetAsync<OrderDto>($"api/orders/ticket/{Uri.EscapeDataString(ticket)}", ct); }
        catch (ApiException ex) when (ex.IsNotFound) { return null; }
    }

    public Task<OrderQuoteDto?> QuoteOrderAsync(OrderQuoteRequest request, CancellationToken ct = default) =>
        PostAsync<OrderQuoteDto>("api/orders/quote", request, ct);

    public Task<OrderDto?> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default) =>
        PostAsync<OrderDto>("api/orders", request, ct);

    public Task<OrderDto?> UpdateItemStatusAsync(int orderId, int itemId, ItemStatus status, CancellationToken ct = default) =>
        PutAsync<OrderDto>($"api/orders/{orderId}/items/{itemId}/status", new UpdateItemStatusRequest(status), ct);

    /// <summary>Applique le statut à tous les articles de la commande.</summary>
    public Task<OrderDto?> UpdateOrderStatusAsync(int orderId, ItemStatus status, CancellationToken ct = default) =>
        PutAsync<OrderDto>($"api/orders/{orderId}/status", new UpdateItemStatusRequest(status), ct);

    public Task<PaymentDto?> AddPaymentAsync(int orderId, AddPaymentRequest request, CancellationToken ct = default) =>
        PostAsync<PaymentDto>($"api/orders/{orderId}/payments", request, ct);

    public Task<PaymentDto?> RefundAsync(int orderId, RefundRequest request, CancellationToken ct = default) =>
        PostAsync<PaymentDto>($"api/orders/{orderId}/refunds", request, ct);

    public Task<OrderDto?> PickupAsync(int orderId, PickupRequest request, CancellationToken ct = default) =>
        PostAsync<OrderDto>($"api/orders/{orderId}/pickup", request, ct);

    public Task<OrderDto?> CancelOrderAsync(int orderId, string reason, CancellationToken ct = default) =>
        PostAsync<OrderDto>($"api/orders/{orderId}/cancel", new CancelOrderRequest(reason), ct);

    public Task<byte[]> GetReceiptPdfAsync(int orderId, CancellationToken ct = default) =>
        GetBytesAsync($"api/orders/{orderId}/receipt", ct);

    public Task<MessageResponse?> EmailReceiptAsync(int orderId, CancellationToken ct = default) =>
        PostAsync<MessageResponse>($"api/orders/{orderId}/receipt/email", null, ct);

    public Task<PagedResult<PaymentDto>?> GetPaymentsAsync(DateTime? from = null, DateTime? to = null,
        PaymentMethod? method = null, string? search = null, int page = 1, int pageSize = 50,
        CancellationToken ct = default) =>
        GetAsync<PagedResult<PaymentDto>>("api/payments" + Query(("from", from), ("to", to), ("method", method),
            ("search", search), ("page", page), ("pageSize", pageSize)), ct);

    #endregion

    #region Portal

    public Task<PortalDashboardDto?> GetPortalDashboardAsync(CancellationToken ct = default) =>
        GetAsync<PortalDashboardDto>("api/portal/dashboard", ct);

    public Task<List<OrderSummaryDto>?> GetPortalOrdersAsync(CancellationToken ct = default) =>
        GetAsync<List<OrderSummaryDto>>("api/portal/orders", ct);

    /// <summary>Commande du client connecté ; <c>null</c> si introuvable (404).</summary>
    public async Task<OrderDto?> GetPortalOrderAsync(int id, CancellationToken ct = default)
    {
        try { return await GetAsync<OrderDto>($"api/portal/orders/{id}", ct); }
        catch (ApiException ex) when (ex.IsNotFound) { return null; }
    }

    public Task<byte[]> GetPortalReceiptPdfAsync(int orderId, CancellationToken ct = default) =>
        GetBytesAsync($"api/portal/orders/{orderId}/receipt", ct);

    public Task<ClientDto?> GetProfileAsync(CancellationToken ct = default) =>
        GetAsync<ClientDto>("api/portal/profile", ct);

    public Task<ClientDto?> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct = default) =>
        PutAsync<ClientDto>("api/portal/profile", request, ct);

    public Task<List<LoyaltyTransactionDto>?> GetPortalLoyaltyAsync(CancellationToken ct = default) =>
        GetAsync<List<LoyaltyTransactionDto>>("api/portal/loyalty", ct);

    #endregion

    #region Dashboard / Notifications / Reports

    public Task<DashboardDto?> GetDashboardAsync(CancellationToken ct = default) =>
        GetAsync<DashboardDto>("api/dashboard", ct);

    public Task<List<NotificationDto>?> GetNotificationsAsync(int take = 20, CancellationToken ct = default) =>
        GetAsync<List<NotificationDto>>("api/notifications" + Query(("take", take)), ct);

    public async Task<int> GetUnreadNotificationCountAsync(CancellationToken ct = default)
    {
        var r = await GetAsync<CountResponse>("api/notifications/unread-count", ct);
        return r?.Count ?? 0;
    }

    public Task MarkNotificationReadAsync(int id, CancellationToken ct = default) =>
        PostAsync($"api/notifications/{id}/read", null, ct);

    public Task MarkAllNotificationsReadAsync(CancellationToken ct = default) =>
        PostAsync("api/notifications/read-all", null, ct);

    public Task<ReportDto?> GetReportAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default) =>
        GetAsync<ReportDto>("api/reports" + Query(("from", from), ("to", to)), ct);

    public Task<byte[]> ExportReportCsvAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default) =>
        GetBytesAsync("api/reports/export" + Query(("from", from), ("to", to)), ct);

    #endregion

    #region Admin

    public Task<List<StaffUserDto>?> GetStaffAsync(CancellationToken ct = default) =>
        GetAsync<List<StaffUserDto>>("api/staff", ct);

    public Task<StaffUserDto?> CreateStaffAsync(CreateStaffRequest request, CancellationToken ct = default) =>
        PostAsync<StaffUserDto>("api/staff", request, ct);

    public Task<StaffUserDto?> UpdateStaffAsync(int id, UpdateStaffRequest request, CancellationToken ct = default) =>
        PutAsync<StaffUserDto>($"api/staff/{id}", request, ct);

    public Task<MessageResponse?> SetStaffPasswordAsync(int id, string password, CancellationToken ct = default) =>
        PostAsync<MessageResponse>($"api/staff/{id}/password", new StaffPasswordRequest(password), ct);

    public Task<SettingsDto?> GetSettingsAsync(CancellationToken ct = default) =>
        GetAsync<SettingsDto>("api/settings", ct);

    public Task<SettingsDto?> UpdateSettingsAsync(Dictionary<string, string> values, CancellationToken ct = default) =>
        PutAsync<SettingsDto>("api/settings", new UpdateSettingsRequest(values), ct);

    public Task<BackupResultDto?> CreateBackupAsync(CancellationToken ct = default) =>
        PostAsync<BackupResultDto>("api/settings/backup", null, ct);

    public Task<List<BackupResultDto>?> GetBackupsAsync(CancellationToken ct = default) =>
        GetAsync<List<BackupResultDto>>("api/settings/backups", ct);

    public Task<PagedResult<EmailMessageDto>?> GetEmailsAsync(int page = 1, int pageSize = 30,
        EmailStatus? status = null, CancellationToken ct = default) =>
        GetAsync<PagedResult<EmailMessageDto>>("api/emails" + Query(("page", page), ("pageSize", pageSize),
            ("status", status)), ct);

    public Task<EmailMessageDto?> SendTestEmailAsync(string to, CancellationToken ct = default) =>
        PostAsync<EmailMessageDto>("api/emails/test", new TestEmailRequest(to), ct);

    public Task<EmailMessageDto?> RetryEmailAsync(int id, CancellationToken ct = default) =>
        PostAsync<EmailMessageDto>($"api/emails/{id}/retry", null, ct);

    public Task<PagedResult<AuditLogDto>?> GetAuditAsync(int page = 1, int pageSize = 50, string? search = null,
        CancellationToken ct = default) =>
        GetAsync<PagedResult<AuditLogDto>>("api/audit" + Query(("page", page), ("pageSize", pageSize),
            ("search", search)), ct);

    #endregion

    #region Plomberie HTTP

    private sealed record CountResponse(int Count);

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        var bytes = await SendAsync(HttpMethod.Get, path, null, false, ct);
        return Deserialize<T>(bytes);
    }

    private async Task<T?> PostAsync<T>(string path, object? body, CancellationToken ct, bool anonymous = false)
    {
        var bytes = await SendAsync(HttpMethod.Post, path, body, anonymous, ct);
        return Deserialize<T>(bytes);
    }

    private async Task<T?> PutAsync<T>(string path, object? body, CancellationToken ct)
    {
        var bytes = await SendAsync(HttpMethod.Put, path, body, false, ct);
        return Deserialize<T>(bytes);
    }

    private Task PostAsync(string path, object? body, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, path, body, false, ct);

    private Task<byte[]> GetBytesAsync(string path, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, path, null, false, ct);

    private static T? Deserialize<T>(byte[] bytes) =>
        bytes.Length == 0 ? default : JsonSerializer.Deserialize<T>(bytes, ApiJson.Options);

    /// <summary>Point d'entrée unique : jeton, erreurs réseau et HTTP, corps brut de la réponse (vide pour 204).</summary>
    private async Task<byte[]> SendAsync(HttpMethod method, string path, object? body, bool anonymous, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        var token = Token;
        var authenticated = !anonymous && !string.IsNullOrEmpty(token);
        if (authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var forwardedFor = ForwardedFor;
        if (!string.IsNullOrWhiteSpace(forwardedFor)) request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor.Trim());
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: ApiJson.Options);

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return response.StatusCode == HttpStatusCode.NoContent ? Array.Empty<byte>() : bytes;

            var error = ApiException.FromResponse(response.StatusCode,
                bytes.Length == 0 ? null : Encoding.UTF8.GetString(bytes));
            if (authenticated && error.IsUnauthorized) Unauthorized?.Invoke(this, EventArgs.Empty);
            throw error;
        }
        catch (HttpRequestException ex)
        {
            throw ApiException.Unreachable(BaseUrl, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw ApiException.Unreachable(BaseUrl, ex);
        }
    }

    /// <summary>Construit « ?a=1&amp;b=x » en ignorant les valeurs null.</summary>
    private static string Query(params (string Key, object? Value)[] parameters)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in parameters)
        {
            var text = value switch
            {
                null => null,
                string s => string.IsNullOrEmpty(s) ? null : s,
                DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                bool b => b ? "true" : "false",
                Enum e => e.ToString(),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString()
            };
            if (text is null) continue;
            sb.Append(sb.Length == 0 ? '?' : '&')
              .Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(text));
        }
        return sb.ToString();
    }

    #endregion
}
