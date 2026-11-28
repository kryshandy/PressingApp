using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Core.Email;
using Pressing.Core.Entities;
using Pressing.Core.Messaging;
using Pressing.Core.Options;
using Pressing.Core.Security;
using Pressing.Core.Services;
using Pressing.Tests.Support;
using ClientEntity = Pressing.Core.Entities.Client;

namespace Pressing.Tests.Unit;

/// <summary>Enquête de satisfaction : envoi (consentement, délai de 2 h, une seule fois, horaires) et réponse (jeton, note, une réponse).</summary>
public sealed class FeedbackTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 12, 1, 14, 0, 0, TimeSpan.Zero);

    private readonly TestDb _t = new();
    private readonly FakeWhatsApp _whatsApp = new();
    private readonly TokenService _tokens = new(new TokenOptions { Key = "tests-only-jwt-key-0123456789-abcdefghijklmnopqrstuvwxyz" });
    private int _n;

    private ReminderService Reminders(DateTimeOffset at) => new(
        _t.Db, _whatsApp, _t.Get<EmailService>(),
        new EmailOptions { Provider = "simulated", AppBaseUrl = "https://pressing.example" },
        _tokens, new FixedTime(at), NullLogger<ReminderService>.Instance, new AuditService(_t.Db));

    private FeedbackService Feedback(DateTimeOffset? at = null) =>
        new(_t.Db, _tokens, _t.Settings, new FixedTime(at ?? Now));

    private async Task<(ClientEntity Client, Order Order)> CompletedAsync(
        ClientNotificationPreference preference, double completedHoursAgo = 3, OrderStatus status = OrderStatus.Completed)
    {
        int n = ++_n;
        ClientEntity client = await _t.AddClientAsync("Awa", "Avis" + n, "+225 07 00 22 33 " + (10 + n));
        client.NotificationPreference = preference;
        client.ConsentGivenAt = preference == ClientNotificationPreference.None ? null : Now.DateTime.AddMonths(-1);
        var order = new Order
        {
            TicketNumber = $"PRESS-2026-7{n:0000}",
            ClientId = client.Id,
            Status = status,
            DueDate = Now.DateTime.Date,
            Total = 5000,
            PaidAmount = 5000,
            CompletedAt = status == OrderStatus.Completed ? Now.DateTime.AddHours(-completedHoursAgo) : null
        };
        _t.Db.Orders.Add(order);
        await _t.Db.SaveChangesAsync();
        return (client, order);
    }

    private static string TokenIn(string text)
    {
        const string marker = "/avis?token=";
        int start = text.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        string raw = new(text[start..].TakeWhile(c => !char.IsWhiteSpace(c) && c != '\'' && c != '"').ToArray());
        return Uri.UnescapeDataString(raw);
    }

    // ---------------------------------------------------------------- envoi

    [Fact]
    public async Task Sans_consentement_ou_apres_revocation_aucune_demande()
    {
        await CompletedAsync(ClientNotificationPreference.None);
        var (revoked, _) = await CompletedAsync(ClientNotificationPreference.WhatsApp);
        revoked.ConsentRevokedAt = Now.DateTime.AddDays(-1);
        await _t.Db.SaveChangesAsync();

        Assert.Equal(0, await Reminders(Now).ProcessFeedbackRequestsAsync());
        Assert.Empty(_whatsApp.Sent);
    }

    [Fact]
    public async Task Demande_whatsapp_envoyee_une_seule_fois_avec_lien_et_journal()
    {
        var (client, order) = await CompletedAsync(ClientNotificationPreference.WhatsApp);

        Assert.Equal(1, await Reminders(Now).ProcessFeedbackRequestsAsync());
        Assert.Equal(0, await Reminders(Now.AddMinutes(15)).ProcessFeedbackRequestsAsync());

        WhatsAppMessage sent = Assert.Single(_whatsApp.Sent);
        Assert.Equal("feedback", sent.ReminderType);
        Assert.Contains("https://pressing.example/avis?token=", sent.Body);
        Assert.Equal(client.FirstName, sent.Variables![0]);

        ReminderLog log = await _t.Db.ReminderLogs.SingleAsync(r => r.OrderId == order.Id);
        Assert.Equal("feedback", log.ReminderType);
        Assert.Equal("whatsapp-simulated", log.Channel);
        Assert.True(log.Success);

        // Le lien porte un jeton « feedback » de 14 jours qui désigne cette commande.
        var principal = _tokens.Validate(TokenIn(sent.Body))!;
        Assert.Equal("feedback", principal.FindFirst("token_type")!.Value);
        Assert.Equal(order.Id.ToString(), principal.FindFirst("order_id")!.Value);
        Assert.Equal(client.Id.ToString(), principal.FindFirst("client_id")!.Value);
    }

    [Fact]
    public async Task Demande_par_e_mail_met_le_lien_cliquable_en_file()
    {
        await CompletedAsync(ClientNotificationPreference.Email);

        Assert.Equal(1, await Reminders(Now).ProcessFeedbackRequestsAsync());

        EmailMessage mail = await _t.Db.EmailMessages.SingleAsync(m => m.Template == "reminder");
        Assert.Contains("<a href='https://pressing.example/avis?token=", mail.HtmlBody);
        Assert.Contains("Donner mon avis", mail.HtmlBody);
        Assert.Contains("Me désabonner", mail.HtmlBody);
        Assert.Equal("email-queued", (await _t.Db.ReminderLogs.SingleAsync()).Channel);
    }

    [Theory]
    [InlineData(1.0, 0)]
    [InlineData(1.99, 0)]
    [InlineData(2.0, 1)]
    [InlineData(30.0, 1)]
    [InlineData(49.0, 0)]
    public async Task Delai_de_deux_heures_apres_le_retrait_complet(double completedHoursAgo, int expected)
    {
        await CompletedAsync(ClientNotificationPreference.WhatsApp, completedHoursAgo);
        Assert.Equal(expected, await Reminders(Now).ProcessFeedbackRequestsAsync());
    }

    [Theory]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.PartiallyPickedUp)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task Seule_une_commande_completement_retiree_est_concernee(OrderStatus status)
    {
        await CompletedAsync(ClientNotificationPreference.WhatsApp, status: status);
        Assert.Equal(0, await Reminders(Now).ProcessFeedbackRequestsAsync());
    }

    [Theory]
    [InlineData(7)]
    [InlineData(20)]
    public async Task Pas_d_envoi_en_dehors_de_8h_20h(int hour)
    {
        await CompletedAsync(ClientNotificationPreference.WhatsApp, completedHoursAgo: 5);
        DateTimeOffset at = new(2026, 12, 1, hour, 0, 0, TimeSpan.Zero);
        Assert.Equal(0, await Reminders(at).ProcessFeedbackRequestsAsync());

        // Le lendemain matin, il part (dans la fenêtre de 48 h).
        Assert.Equal(1, await Reminders(new DateTimeOffset(2026, 12, 2, 8, 0, 0, TimeSpan.Zero)).ProcessFeedbackRequestsAsync());
    }

    [Fact]
    public async Task Pas_de_demande_si_le_client_a_deja_repondu()
    {
        var (client, order) = await CompletedAsync(ClientNotificationPreference.WhatsApp);
        _t.Db.Feedbacks.Add(new Feedback { OrderId = order.Id, ClientId = client.Id, Note = 4 });
        await _t.Db.SaveChangesAsync();

        Assert.Equal(0, await Reminders(Now).ProcessFeedbackRequestsAsync());
    }

    // ---------------------------------------------------------------- réponse

    [Fact]
    public async Task Note_hors_de_1_a_5_refusee_et_commentaire_limite_a_1000_caracteres()
    {
        var (client, order) = await CompletedAsync(ClientNotificationPreference.WhatsApp);
        string token = _tokens.CreateFeedbackToken(order.Id, client.Id);

        foreach (int bad in new[] { 0, 6, -1, 100 })
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Feedback().SubmitAsync(token, bad, null));
            Assert.Contains("entre 1 et 5", ex.Message);
        }

        var tooLong = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Feedback().SubmitAsync(token, 4, new string('x', 1001)));
        Assert.Contains("1000", tooLong.Message);
        Assert.Empty(await _t.Db.Feedbacks.ToListAsync());

        await Feedback().SubmitAsync(token, 5, new string('é', 1000));
        Assert.Equal(1000, (await _t.Db.Feedbacks.SingleAsync()).Comment!.Length);
    }

    [Fact]
    public async Task Une_seule_reponse_par_commande()
    {
        var (client, order) = await CompletedAsync(ClientNotificationPreference.WhatsApp);
        string token = _tokens.CreateFeedbackToken(order.Id, client.Id);

        FeedbackFormDto before = await Feedback().GetFormAsync(token);
        Assert.False(before.AlreadyAnswered);
        Assert.Equal(order.TicketNumber, before.TicketNumber);

        await Feedback().SubmitAsync(token, 4, "  Très bon travail  ");
        Assert.Single(await _t.Db.Feedbacks.ToListAsync());
        Assert.Equal("Très bon travail", (await _t.Db.Feedbacks.SingleAsync()).Comment);

        await Assert.ThrowsAsync<FeedbackAlreadyAnsweredException>(() => Feedback().SubmitAsync(token, 1, "Autre avis"));
        Assert.Equal(4, (await _t.Db.Feedbacks.SingleAsync()).Note);

        FeedbackFormDto after = await Feedback().GetFormAsync(token);
        Assert.True(after.AlreadyAnswered);
        Assert.Equal(4, after.Note);
    }

    [Fact]
    public async Task Seul_un_jeton_feedback_valide_pour_cette_commande_est_accepte()
    {
        var (client, order) = await CompletedAsync(ClientNotificationPreference.WhatsApp);
        var (other, _) = await CompletedAsync(ClientNotificationPreference.WhatsApp);

        string[] refused =
        [
            _tokens.CreateUnsubscribeToken(client.Id),
            _tokens.CreateStaffToken(new StaffUser { Id = 1, FullName = "A", Username = "a", Role = UserRole.Admin }).Token,
            _tokens.CreateClientToken(client).Token,
            _tokens.CreateFeedbackToken(order.Id, other.Id), // client qui n'est pas celui de la commande
            _tokens.CreateFeedbackToken(99999, client.Id),   // commande inconnue
            "n'importe-quoi",
            ""
        ];
        foreach (string token in refused)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Feedback().SubmitAsync(token, 5, null));
            Assert.Equal(FeedbackService.InvalidLink, ex.Message);
        }

        Assert.Empty(await _t.Db.Feedbacks.ToListAsync());
    }

    [Fact]
    public async Task Le_jeton_est_valable_14_jours()
    {
        var (client, order) = await CompletedAsync(ClientNotificationPreference.WhatsApp);
        string fresh = _tokens.CreateFeedbackToken(order.Id, client.Id);
        var exp = _tokens.Validate(fresh)!.FindFirst("exp")!.Value;
        double days = (DateTimeOffset.FromUnixTimeSeconds(long.Parse(exp)) - DateTimeOffset.UtcNow).TotalDays;
        Assert.InRange(days, 13.9, 14.1);
    }

    [Fact]
    public async Task Liste_des_avis_moyenne_et_filtre_sur_30_jours()
    {
        var (c1, o1) = await CompletedAsync(ClientNotificationPreference.WhatsApp);
        var (c2, o2) = await CompletedAsync(ClientNotificationPreference.WhatsApp);
        var (c3, o3) = await CompletedAsync(ClientNotificationPreference.WhatsApp);
        _t.Db.Feedbacks.AddRange(
            new Feedback { OrderId = o1.Id, ClientId = c1.Id, Note = 5, CreatedAt = Now.DateTime.AddDays(-1) },
            new Feedback { OrderId = o2.Id, ClientId = c2.Id, Note = 4, Comment = "Bien", CreatedAt = Now.DateTime.AddDays(-10) },
            new Feedback { OrderId = o3.Id, ClientId = c3.Id, Note = 1, CreatedAt = Now.DateTime.AddDays(-60) });
        await _t.Db.SaveChangesAsync();

        FeedbackPageDto all = await Feedback().GetPageAsync();
        Assert.Equal(3, all.Total);
        Assert.Equal(3.33, all.Average);
        Assert.Equal(5, all.Items[0].Note); // le plus récent d'abord

        FeedbackPageDto month = await Feedback().GetPageAsync(days: 30);
        Assert.Equal(2, month.Count);
        Assert.Equal(4.5, month.Average);

        Assert.Null((await Feedback(Now.AddDays(100)).GetPageAsync(days: 30)).Average);
    }

    [Fact]
    public async Task L_index_unique_empeche_deux_avis_pour_la_meme_commande()
    {
        var (client, order) = await CompletedAsync(ClientNotificationPreference.WhatsApp);
        _t.Db.Feedbacks.Add(new Feedback { OrderId = order.Id, ClientId = client.Id, Note = 3 });
        await _t.Db.SaveChangesAsync();
        _t.Db.Feedbacks.Add(new Feedback { OrderId = order.Id, ClientId = client.Id, Note = 5 });
        await Assert.ThrowsAsync<DbUpdateException>(() => _t.Db.SaveChangesAsync());
    }

    public void Dispose() => _t.Dispose();

    private sealed class FakeWhatsApp : IWhatsAppTransport
    {
        public List<WhatsAppMessage> Sent { get; } = [];
        public string Provider => "fake";

        public Task<WhatsAppSendResult> SendAsync(WhatsAppMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.FromResult(new WhatsAppSendResult(true, true, "fake", null));
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
