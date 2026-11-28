using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pressing.Core;
using Pressing.Core.Email;
using Pressing.Core.Entities;
using Pressing.Core.Messaging;
using Pressing.Core.Options;
using Pressing.Core.Security;
using Pressing.Core.Services;
using Pressing.Tests.Support;
using ClientEntity = Pressing.Core.Entities.Client;

namespace Pressing.Tests.Unit;

/// <summary>
/// Rappels automatiques (lot 3) : consentement obligatoire, un seul palier de relance à la fois, solde rappelé
/// seulement quand la commande attend son retrait, pas de doublon avec l'e-mail « commande prête ».
/// </summary>
public sealed class ReminderServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly TestDb _t = new();
    private readonly FakeWhatsApp _whatsApp = new();
    private int _ticket;

    [Fact]
    public async Task Sans_consentement_aucun_rappel()
    {
        var client = await ClientAsync(ClientNotificationPreference.None);
        await OrderAsync(client, OrderStatus.Ready, readyDaysAgo: 5, balance: 2000);

        Assert.Equal(0, await RunAsync());
        Assert.Empty(_whatsApp.Sent);
    }

    [Fact]
    public async Task Consentement_revoque_aucun_rappel()
    {
        var client = await ClientAsync(ClientNotificationPreference.WhatsApp);
        client.ConsentRevokedAt = Now.DateTime.AddDays(-1);
        await _t.Db.SaveChangesAsync();
        await OrderAsync(client, OrderStatus.Ready, readyDaysAgo: 0);

        Assert.Equal(0, await RunAsync());
    }

    [Fact]
    public async Task Commande_prete_whatsapp_une_seule_fois()
    {
        var client = await ClientAsync(ClientNotificationPreference.WhatsApp);
        await OrderAsync(client, OrderStatus.Ready, readyDaysAgo: 0);

        Assert.Equal(1, await RunAsync());
        Assert.Equal(0, await RunAsync());
        Assert.Contains("est prête", Assert.Single(_whatsApp.Sent).Body);
    }

    [Fact]
    public async Task Commande_prete_par_e_mail_ne_double_pas_l_e_mail_existant()
    {
        var client = await ClientAsync(ClientNotificationPreference.Email);
        await OrderAsync(client, OrderStatus.Ready, readyDaysAgo: 0);

        Assert.Equal(0, await RunAsync());
        Assert.Empty(await _t.Db.EmailMessages.Where(m => m.Template == "reminder").ToListAsync());
    }

    [Fact]
    public async Task Commande_oubliee_depuis_14_jours_recoit_un_seul_palier_et_le_solde()
    {
        var client = await ClientAsync(ClientNotificationPreference.WhatsApp);
        await OrderAsync(client, OrderStatus.Ready, readyDaysAgo: 14, balance: 3500);

        Assert.Equal(2, await RunAsync());
        string[] types = await _t.Db.ReminderLogs.Select(r => r.ReminderType).OrderBy(t => t).ToArrayAsync();
        Assert.Equal(["uncollected-j14", "unpaid-balance"], types);
    }

    [Theory]
    [InlineData(3, "uncollected-j3")]
    [InlineData(8, "uncollected-j7")]
    public async Task Palier_de_relance_selon_l_anciennete(int days, string expected)
    {
        var client = await ClientAsync(ClientNotificationPreference.WhatsApp);
        await OrderAsync(client, OrderStatus.Ready, readyDaysAgo: days);

        Assert.Equal(1, await RunAsync());
        Assert.Equal(expected, (await _t.Db.ReminderLogs.SingleAsync()).ReminderType);
    }

    [Fact]
    public async Task Solde_non_rappele_juste_apres_le_depot()
    {
        var client = await ClientAsync(ClientNotificationPreference.WhatsApp);
        await OrderAsync(client, OrderStatus.Deposited, balance: 5000, dueInDays: 4);

        Assert.Equal(0, await RunAsync());
    }

    [Fact]
    public async Task Veille_de_l_echeance_rappelee_si_la_commande_n_est_pas_prete()
    {
        var client = await ClientAsync(ClientNotificationPreference.WhatsApp);
        await OrderAsync(client, OrderStatus.InProgress, dueInDays: 1);

        Assert.Equal(1, await RunAsync());
        Assert.Equal("due-tomorrow", (await _t.Db.ReminderLogs.SingleAsync()).ReminderType);
    }

    [Fact]
    public async Task Rien_n_est_envoye_la_nuit()
    {
        var client = await ClientAsync(ClientNotificationPreference.WhatsApp);
        await OrderAsync(client, OrderStatus.Ready, readyDaysAgo: 0);

        Assert.Equal(0, await RunAsync(new DateTimeOffset(2026, 12, 1, 21, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task E_mail_de_rappel_pointe_vers_la_page_de_desabonnement_du_site()
    {
        var client = await ClientAsync(ClientNotificationPreference.Email);
        await OrderAsync(client, OrderStatus.Ready, readyDaysAgo: 3);

        Assert.Equal(1, await RunAsync());
        EmailMessage mail = await _t.Db.EmailMessages.SingleAsync(m => m.Template == "reminder");
        Assert.Contains("https://pressing.example/desabonnement?token=", mail.HtmlBody);
        Assert.DoesNotContain("/api/public/", mail.HtmlBody);
    }

    // ------------------------------------------------------------------ outils

    private async Task<int> RunAsync(DateTimeOffset? at = null)
    {
        var service = new ReminderService(
            _t.Db, _whatsApp, _t.Get<EmailService>(),
            new EmailOptions { Provider = "simulated", AppBaseUrl = "https://pressing.example" },
            new TokenService(new TokenOptions { Key = "tests-only-jwt-key-0123456789-abcdefghijklmnopqrstuvwxyz" }),
            new FixedTime(at ?? Now), NullLogger<ReminderService>.Instance, new AuditService(_t.Db));
        return await service.ProcessDueRemindersAsync();
    }

    private async Task<ClientEntity> ClientAsync(ClientNotificationPreference preference)
    {
        ClientEntity client = await _t.AddClientAsync(phone: "+225 07 00 11 22 " + (10 + _ticket));
        client.NotificationPreference = preference;
        client.ConsentGivenAt = preference == ClientNotificationPreference.None ? null : Now.DateTime.AddMonths(-1);
        await _t.Db.SaveChangesAsync();
        return client;
    }

    private async Task OrderAsync(ClientEntity client, OrderStatus status, int? readyDaysAgo = null,
        decimal balance = 0, int dueInDays = 10)
    {
        _t.Db.Orders.Add(new Order
        {
            TicketNumber = $"PRESS-2026-9{++_ticket:0000}",
            ClientId = client.Id,
            Status = status,
            DueDate = Now.DateTime.Date.AddDays(dueInDays),
            ReadyNotifiedAt = readyDaysAgo is int d ? Now.DateTime.AddDays(-d) : null,
            Total = 10000,
            PaidAmount = 10000 - balance
        });
        await _t.Db.SaveChangesAsync();
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
