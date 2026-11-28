using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Tests.Support;

namespace Pressing.Tests.Integration;

/// <summary>Journal des rappels et relance manuelle (responsable / administrateur).</summary>
[Collection("Api")]
public class RemindersApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private static int _counter = 900;

    public RemindersApiTests(ApiFactory factory) => _factory = factory;

    private async Task<(ClientDto Client, int OrderId)> ClientWithOrderAsync(
        ClientNotificationPreference preference, OrderStatus status = OrderStatus.Ready)
    {
        int n = Interlocked.Increment(ref _counter);
        PressingApiClient staff = await _factory.CashierAsync();
        ClientDto client = (await staff.CreateClientAsync(new CreateClientRequest(
            "Fatou", "Relance" + n, "07220" + n.ToString("00000"), "07220" + n.ToString("00000"),
            $"fatou.relance{n}@example.com", null, null, null, false, preference)))!;

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PressingDbContext>();
        var order = new Order
        {
            TicketNumber = $"PRESS-2026-8{n:0000}",
            ClientId = client.Id,
            Status = status,
            DueDate = DateTime.Today.AddDays(2),
            ReadyNotifiedAt = DateTime.Now.AddDays(-4),
            Total = 8000,
            PaidAmount = 3000
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (client, order.Id);
    }

    [Fact]
    public async Task Caissier_n_a_acces_ni_au_journal_ni_a_la_relance()
    {
        PressingApiClient cashier = await _factory.CashierAsync();

        var list = await Assert.ThrowsAsync<ApiException>(() => cashier.GetRemindersAsync());
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        var send = await Assert.ThrowsAsync<ApiException>(() => cashier.SendReminderAsync(1));
        Assert.Equal(HttpStatusCode.Forbidden, send.StatusCode);
    }

    [Fact]
    public async Task Sans_jeton_le_journal_est_refuse()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => _factory.NewApiClient().GetRemindersAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task Relance_manuelle_d_un_client_consentant_est_journalisee_et_auditee()
    {
        var (client, orderId) = await ClientWithOrderAsync(ClientNotificationPreference.WhatsApp);
        PressingApiClient manager = await _factory.ManagerAsync();

        ReminderLogDto sent = (await manager.SendReminderAsync(orderId))!;

        Assert.True(sent.Success);
        Assert.Equal("manual", sent.ReminderType);
        Assert.Equal("Relance manuelle", sent.TypeLabel);
        Assert.Equal("whatsapp-simulated", sent.Channel);
        Assert.Equal(client.Id, sent.ClientId);

        PagedResult<ReminderLogDto> page = (await manager.GetRemindersAsync(type: "manual", channel: "whatsapp"))!;
        Assert.Contains(page.Items, r => r.Id == sent.Id && r.TicketNumber.StartsWith("PRESS-2026-8"));

        PressingApiClient admin = await _factory.AdminAsync();
        PagedResult<AuditLogDto> audit = (await admin.GetAuditAsync(search: "reminder-send"))!;
        Assert.Contains(audit.Items, a => a.EntityId == orderId.ToString());
    }

    [Fact]
    public async Task Relance_par_e_mail_met_le_message_en_file()
    {
        var (_, orderId) = await ClientWithOrderAsync(ClientNotificationPreference.Email);
        PressingApiClient admin = await _factory.AdminAsync();

        ReminderLogDto sent = (await admin.SendReminderAsync(orderId))!;

        Assert.Equal("email-queued", sent.Channel);
        Assert.Equal("E-mail", sent.ChannelLabel);
    }

    [Fact]
    public async Task Relance_sans_consentement_est_refusee_avec_un_message_clair()
    {
        var (_, orderId) = await ClientWithOrderAsync(ClientNotificationPreference.None);
        PressingApiClient manager = await _factory.ManagerAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => manager.SendReminderAsync(orderId));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("n'a pas consenti", ex.Message);
    }

    [Fact]
    public async Task Relance_d_une_commande_terminee_ou_inconnue_est_refusee()
    {
        var (_, completed) = await ClientWithOrderAsync(ClientNotificationPreference.WhatsApp, OrderStatus.Completed);
        PressingApiClient manager = await _factory.ManagerAsync();

        var done = await Assert.ThrowsAsync<ApiException>(() => manager.SendReminderAsync(completed));
        Assert.Equal(HttpStatusCode.BadRequest, done.StatusCode);

        var missing = await Assert.ThrowsAsync<ApiException>(() => manager.SendReminderAsync(999999));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Journal_pagine_et_filtre_par_periode()
    {
        var (_, first) = await ClientWithOrderAsync(ClientNotificationPreference.WhatsApp);
        var (_, second) = await ClientWithOrderAsync(ClientNotificationPreference.WhatsApp);
        PressingApiClient manager = await _factory.ManagerAsync();
        await manager.SendReminderAsync(first);
        await manager.SendReminderAsync(second);

        PagedResult<ReminderLogDto> one = (await manager.GetRemindersAsync(page: 1, pageSize: 1))!;
        Assert.Single(one.Items);
        Assert.True(one.Total >= 2);
        Assert.True(one.TotalPages >= 2);

        PagedResult<ReminderLogDto> today = (await manager.GetRemindersAsync(from: DateTime.Today, to: DateTime.Today))!;
        Assert.True(today.Total >= 2);

        PagedResult<ReminderLogDto> past = (await manager.GetRemindersAsync(to: DateTime.Today.AddDays(-30)))!;
        Assert.Equal(0, past.Total);

        PagedResult<ReminderLogDto> none = (await manager.GetRemindersAsync(channel: "email"))!;
        Assert.All(none.Items, r => Assert.StartsWith("email", r.Channel));
    }
}
