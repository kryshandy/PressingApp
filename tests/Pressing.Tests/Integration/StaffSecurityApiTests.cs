using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Core.Security;
using Pressing.Tests.Support;

namespace Pressing.Tests.Integration;

/// <summary>Verrouillage, double authentification (Manager) et droits d'administration, de bout en bout par HTTP.</summary>
[Collection("Api")]
public class StaffSecurityApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    private static int _ip;

    public StaffSecurityApiTests(ApiFactory factory) => _factory = factory;

    /// <summary>Client anonyme avec sa propre adresse (X-Forwarded-For) : la limitation de débit de connexion est par IP.</summary>
    private PressingApiClient Fresh() =>
        new(_factory.CreateClient()) { ForwardedFor = "10.20.0." + (Interlocked.Increment(ref _ip) % 250 + 1) };

    [Fact]
    public async Task Cinq_echecs_verrouillent_423_puis_l_administrateur_deverrouille()
    {
        PressingApiClient admin = await _factory.AdminAsync();
        StaffUserDto member = (await admin.CreateStaffAsync(
            new CreateStaffRequest("Compte Verrou", "verrou", null, UserRole.Cashier, "Verrou2026")))!;

        for (int i = 0; i < 4; i++)
        {
            var wrong = await Assert.ThrowsAsync<ApiException>(() => Fresh().LoginStaffAsync("verrou", "mauvais-1"));
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.Equal("Identifiant ou mot de passe incorrect.", wrong.Message);
        }

        var locked = await Assert.ThrowsAsync<ApiException>(() => Fresh().LoginStaffAsync("verrou", "mauvais-1"));
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
        Assert.Matches(@"déverrouillé à \d{2}:\d{2}", locked.Message);

        // Bon mot de passe pendant le verrouillage : refusé aussi.
        var stillLocked = await Assert.ThrowsAsync<ApiException>(() => Fresh().LoginStaffAsync("verrou", "Verrou2026"));
        Assert.Equal(HttpStatusCode.Locked, stillLocked.StatusCode);

        Assert.True((await admin.GetStaffAsync())!.Single(s => s.Id == member.Id).IsLocked);
        await admin.UnlockStaffAsync(member.Id);
        Assert.NotNull((await Fresh().LoginStaffAsync("verrou", "Verrou2026"))!.Staff);

        var audit = (await admin.GetAuditAsync(1, 20, "verrou"))!;
        Assert.Contains(audit.Items, a => a.Action == "unlock");
        Assert.Contains((await admin.GetAuditAsync(1, 50, "verrouillé"))!.Items, a => a.Action == "lockout");
    }

    [Fact]
    public async Task Deverrouillage_et_reinitialisation_2fa_sont_reserves_a_l_administrateur()
    {
        PressingApiClient manager = await _factory.ManagerAsync();
        PressingApiClient cashier = await _factory.CashierAsync();
        PressingApiClient anonymous = Fresh();

        foreach (PressingApiClient who in new[] { manager, cashier })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<ApiException>(() => who.UnlockStaffAsync(1))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<ApiException>(() => who.ResetStaffTwoFactorAsync(1))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<ApiException>(() =>
                who.UpdateSettingsAsync(new Dictionary<string, string> { [SettingsKeys.RequireManagerTwoFactor] = "false" }))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await Assert.ThrowsAsync<ApiException>(() => anonymous.UnlockStaffAsync(1))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Assert.ThrowsAsync<ApiException>(() => anonymous.GetTwoFactorStatusAsync())).StatusCode);
    }

    [Fact]
    public async Task Politique_de_mot_de_passe_a_la_creation_d_un_compte()
    {
        PressingApiClient admin = await _factory.AdminAsync();
        var weak = await Assert.ThrowsAsync<ApiException>(() => admin.CreateStaffAsync(
            new CreateStaffRequest("Mdp Faible", "mdpfaible", null, UserRole.Cashier, "abcdefgh")));
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("au moins 8 caractères", weak.Message);
        Assert.NotNull(await admin.CreateStaffAsync(new CreateStaffRequest("Mdp Fort", "mdpfort", null, UserRole.Cashier, "abcdefg1")));
    }

    [Fact]
    public async Task Manager_double_authentification_de_bout_en_bout_et_jeton_intermediaire_refuse_partout_ailleurs()
    {
        PressingApiClient admin = await _factory.AdminAsync();
        StaffUserDto created = (await admin.CreateStaffAsync(
            new CreateStaffRequest("Resp Deux Facteurs", "resp2fa", null, UserRole.Manager, "Resp2026ok")))!;

        // Premier passage : pas de 2FA, la connexion aboutit et demande l'activation.
        PressingApiClient manager = Fresh();
        AuthResponse first = (await manager.LoginStaffAsync("resp2fa", "Resp2026ok"))!;
        Assert.False(first.MfaRequired);
        Assert.True(first.TwoFactorSetupRequired);

        TwoFactorSetupDto setup = (await manager.BeginTwoFactorSetupAsync())!;
        var wrong = await Assert.ThrowsAsync<ApiException>(() => manager.EnableTwoFactorAsync("000000"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        TwoFactorEnabledDto enabled = (await manager.EnableTwoFactorAsync(Totp.Compute(setup.Secret, DateTimeOffset.UtcNow)))!;
        Assert.Equal(8, enabled.RecoveryCodes.Count);
        Assert.True((await manager.GetTwoFactorStatusAsync())!.Enabled);

        // Second passage : jeton intermédiaire.
        PressingApiClient second = Fresh();
        AuthResponse pending = (await second.LoginStaffAsync("resp2fa", "Resp2026ok"))!;
        Assert.True(pending.MfaRequired);
        Assert.Null(pending.Staff);

        // Le jeton « mfa-pending » ne donne accès à rien.
        foreach (Func<Task> call in new Func<Task>[]
                 {
                     () => second.GetDashboardAsync(),
                     () => second.GetStaffAsync(),
                     () => second.GetTwoFactorStatusAsync(),
                     () => second.ChangeStaffPasswordAsync("Resp2026ok", "Autre2026ok")
                 })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Assert.ThrowsAsync<ApiException>(call)).StatusCode);
        }

        // Sans jeton intermédiaire ou avec un mauvais code : 401.
        var noToken = await Assert.ThrowsAsync<ApiException>(() => Fresh().LoginStaffMfaAsync("123456"));
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        var badCode = await Assert.ThrowsAsync<ApiException>(() => second.LoginStaffMfaAsync("000000"));
        Assert.Equal(HttpStatusCode.Unauthorized, badCode.StatusCode);

        AuthResponse done = (await second.LoginStaffMfaAsync(Totp.Compute(setup.Secret, DateTimeOffset.UtcNow)))!;
        Assert.Equal(UserRole.Manager, done.Staff!.Role);
        Assert.NotNull(await second.GetDashboardAsync());

        // Un vrai jeton de session ne sert pas au second temps.
        var reuse = await Assert.ThrowsAsync<ApiException>(() => second.LoginStaffMfaAsync("123456"));
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        // L'administrateur réinitialise : le manager doit réactiver (connexion directe, activation exigée).
        await admin.ResetStaffTwoFactorAsync(created.Id);
        AuthResponse afterReset = (await Fresh().LoginStaffAsync("resp2fa", "Resp2026ok"))!;
        Assert.False(afterReset.MfaRequired);
        Assert.True(afterReset.TwoFactorSetupRequired);
        Assert.Contains((await admin.GetAuditAsync(1, 30, "2fa"))!.Items, a => a.Action == "2fa-reset");
    }

    [Fact]
    public async Task Admin_et_caissier_n_ont_pas_de_double_authentification()
    {
        PressingApiClient admin = await _factory.AdminAsync();
        PressingApiClient cashier = await _factory.CashierAsync();

        foreach (PressingApiClient who in new[] { admin, cashier })
        {
            Assert.False((await who.GetTwoFactorStatusAsync())!.Available);
            var ex = await Assert.ThrowsAsync<ApiException>(() => who.BeginTwoFactorSetupAsync());
            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }
    }

    [Fact]
    public async Task Le_jeton_intermediaire_est_aussi_refuse_sur_une_route_anonyme_protegee_par_le_jeton()
    {
        // Contrôle HTTP brut : mfa-pending dans l'en-tête Authorization n'authentifie jamais.
        PressingApiClient admin = await _factory.AdminAsync();
        await admin.CreateStaffAsync(new CreateStaffRequest("Resp Brut", "respbrut", null, UserRole.Manager, "Brut2026ok"));
        PressingApiClient m = Fresh();
        await m.LoginStaffAsync("respbrut", "Brut2026ok");
        TwoFactorSetupDto setup = (await m.BeginTwoFactorSetupAsync())!;
        await m.EnableTwoFactorAsync(Totp.Compute(setup.Secret, DateTimeOffset.UtcNow));

        string pending = (await Fresh().LoginStaffAsync("respbrut", "Brut2026ok"))!.Token;
        using HttpClient http = _factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-Forwarded-For", "10.20.1.1");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pending);
        foreach (string path in new[] { "/api/auth/me", "/api/orders", "/api/dashboard", "/api/staff", "/api/reminders" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(path)).StatusCode);
        }

        using HttpResponseMessage mfa = await http.PostAsJsonAsync("/api/auth/staff/login/mfa", new StaffMfaRequest("000000"));
        Assert.Equal(HttpStatusCode.Unauthorized, mfa.StatusCode);
    }

    [Fact]
    public async Task L_administrateur_desactive_l_exigence_et_le_manager_se_connecte_sans_second_facteur()
    {
        PressingApiClient admin = await _factory.AdminAsync();
        await admin.CreateStaffAsync(new CreateStaffRequest("Resp Reglage", "respreglage", null, UserRole.Manager, "Regl2026ok"));
        PressingApiClient m = Fresh();
        await m.LoginStaffAsync("respreglage", "Regl2026ok");
        TwoFactorSetupDto setup = (await m.BeginTwoFactorSetupAsync())!;
        await m.EnableTwoFactorAsync(Totp.Compute(setup.Secret, DateTimeOffset.UtcNow));
        Assert.True((await Fresh().LoginStaffAsync("respreglage", "Regl2026ok"))!.MfaRequired);

        try
        {
            await admin.UpdateSettingsAsync(new Dictionary<string, string> { [SettingsKeys.RequireManagerTwoFactor] = "false" });
            AuthResponse direct = (await Fresh().LoginStaffAsync("respreglage", "Regl2026ok"))!;
            Assert.False(direct.MfaRequired);
            Assert.NotNull(direct.Staff);
            Assert.Contains((await admin.GetAuditAsync(1, 30, "managers"))!.Items, a => a.Action == "2fa-unrequire");
        }
        finally
        {
            await admin.UpdateSettingsAsync(new Dictionary<string, string> { [SettingsKeys.RequireManagerTwoFactor] = "true" });
        }

        Assert.True((await Fresh().LoginStaffAsync("respreglage", "Regl2026ok"))!.MfaRequired);
    }
}
