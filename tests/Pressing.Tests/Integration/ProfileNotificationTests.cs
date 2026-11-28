using System.Net;
using Pressing.Client;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Tests.Support;

namespace Pressing.Tests.Integration;

/// <summary>Préférence de rappel choisie par le client depuis son profil (espace client / site).</summary>
[Collection("Api")]
public class ProfileNotificationTests : IClassFixture<ApiFactory>
{
    private const string Password = "Profil@2026!";
    private readonly ApiFactory _factory;

    public ProfileNotificationTests(ApiFactory factory) => _factory = factory;

    private async Task<(PressingApiClient Portal, ClientDto Client)> NewPortalClientAsync(string phone, string? email)
    {
        PressingApiClient staff = await _factory.CashierAsync();
        ClientDto client = (await staff.CreateClientAsync(new CreateClientRequest(
            "Mariam", "Profil", phone, phone, email, null, null, Password, false)))!;
        return (await _factory.PortalClientAsync(phone, Password), client);
    }

    private static UpdateProfileRequest Profile(ClientDto c, ClientNotificationPreference? preference) =>
        new(c.FirstName, c.LastName, c.Phone, c.WhatsApp, c.Email, c.Address, preference);

    [Fact]
    public async Task Choisir_WhatsApp_enregistre_le_canal_et_date_le_consentement()
    {
        var (portal, client) = await NewPortalClientAsync("0711000001", null);
        Assert.Equal(ClientNotificationPreference.None, client.NotificationPreference);
        Assert.Null(client.ConsentGivenAt);

        ClientDto updated = (await portal.UpdateProfileAsync(Profile(client, ClientNotificationPreference.WhatsApp)))!;

        Assert.Equal(ClientNotificationPreference.WhatsApp, updated.NotificationPreference);
        Assert.NotNull(updated.ConsentGivenAt);
        Assert.Null(updated.ConsentRevokedAt);
    }

    [Fact]
    public async Task Choisir_Aucun_rappel_revoque_le_consentement()
    {
        var (portal, client) = await NewPortalClientAsync("0711000002", "mariam.profil2@example.com");
        await portal.UpdateProfileAsync(Profile(client, ClientNotificationPreference.Email));

        ClientDto updated = (await portal.UpdateProfileAsync(Profile(client, ClientNotificationPreference.None)))!;

        Assert.Equal(ClientNotificationPreference.None, updated.NotificationPreference);
        Assert.NotNull(updated.ConsentRevokedAt);
    }

    [Fact]
    public async Task Canal_e_mail_sans_adresse_e_mail_est_refuse()
    {
        var (portal, client) = await NewPortalClientAsync("0711000003", null);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            portal.UpdateProfileAsync(Profile(client, ClientNotificationPreference.Email)));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("adresse e-mail", ex.Message);
        ClientDto after = (await portal.GetProfileAsync())!;
        Assert.Equal(ClientNotificationPreference.None, after.NotificationPreference);
    }

    [Fact]
    public async Task Canal_WhatsApp_sans_numero_est_refuse()
    {
        var (portal, client) = await NewPortalClientAsync("0711000004", null);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            portal.UpdateProfileAsync(new UpdateProfileRequest(
                client.FirstName, client.LastName, client.Phone, "0711000004", client.Email, client.Address,
                ClientNotificationPreference.WhatsApp) with { WhatsApp = " " }));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }
}
