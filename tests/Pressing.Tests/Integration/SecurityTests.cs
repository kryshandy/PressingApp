using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Options;
using Pressing.Tests.Support;

namespace Pressing.Tests.Integration;

/// <summary>Santé, authentification et contrôle d'accès (401 / 403) de l'API.</summary>
[Collection("Api")]
public class SecurityTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public SecurityTests(ApiFactory factory) => _factory = factory;

    private HttpClient WithToken(string token)
    {
        HttpClient http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    [Fact]
    public async Task Sante_repond_ok_avec_e_mails_simules()
    {
        PressingApiClient api = _factory.NewApiClient();
        HealthInfoDto? health = await api.GetHealthAsync();

        Assert.NotNull(health);
        Assert.Equal("ok", health!.Status);
        Assert.False(health.EmailConfigured);
        Assert.Equal("simulated", health.EmailProvider);
        Assert.False(string.IsNullOrWhiteSpace(health.ShopName));
    }

    [Fact]
    public void Base_utilisee_est_la_base_temporaire_et_jamais_la_base_reelle()
    {
        // Le conteneur d'injection de l'hôte de test doit pointer sur le fichier temporaire de la fabrique.
        _ = _factory.CreateClient();
        var options = _factory.Services.GetRequiredService<PressingDatabaseOptions>();

        Assert.Equal(Path.GetFullPath(_factory.DbPath), Path.GetFullPath(options.Path));
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(options.Path), StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(_factory.DbPath));

        string realDb = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pressing", "pressing.db");
        Assert.NotEqual(Path.GetFullPath(realDb), Path.GetFullPath(options.Path), StringComparer.OrdinalIgnoreCase);

        // PRESSING_DB_PATH est bien celui que lit ResolveOptions.
        Assert.Equal(Path.GetFullPath(_factory.DbPath), Path.GetFullPath(PressingDatabase.ResolveOptions().Path));
    }

    [Fact]
    public async Task Connexion_personnel_valide_retourne_un_jeton_et_le_role()
    {
        PressingApiClient api = _factory.NewApiClient();
        AuthResponse? auth = await api.LoginStaffAsync("caisse", ApiFactory.CashierPassword);

        Assert.NotNull(auth);
        Assert.False(string.IsNullOrEmpty(auth!.Token));
        Assert.Equal(UserRole.Cashier, auth.Staff!.Role);
        Assert.Null(auth.Client);
        Assert.True(api.IsAuthenticated);

        MeDto? me = await api.GetMeAsync();
        Assert.Equal("staff", me!.Type);
        Assert.Equal("caisse", me.Staff!.Username);
    }

    [Fact]
    public async Task Connexion_avec_mauvais_mot_de_passe_donne_401()
    {
        PressingApiClient api = _factory.NewApiClient();
        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => api.LoginStaffAsync("caisse", "mauvais-mdp"));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.False(api.IsAuthenticated);
    }

    [Fact]
    public async Task Connexion_avec_utilisateur_inconnu_donne_401()
    {
        HttpClient http = _factory.CreateClient();
        HttpResponseMessage response = await http.PostAsJsonAsync(
            "/api/auth/staff/login", new { username = "fantome", password = "x" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/orders")]
    [InlineData("/api/clients")]
    [InlineData("/api/payments")]
    [InlineData("/api/dashboard")]
    [InlineData("/api/settings")]
    [InlineData("/api/portal/dashboard")]
    public async Task Route_protegee_sans_jeton_donne_401(string path)
    {
        HttpResponseMessage response = await _factory.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Jeton_invalide_donne_401()
    {
        HttpResponseMessage response = await WithToken("pas.un.jeton").GetAsync("/api/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Jeton_client_sur_route_personnel_donne_403()
    {
        PressingApiClient client = await _factory.PortalClientAsync("+225 07 11 22 33 44");
        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => client.SearchOrdersAsync());
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);

        ex = await Assert.ThrowsAsync<ApiException>(() => client.GetDashboardAsync());
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task Jeton_personnel_sur_portail_client_donne_403()
    {
        PressingApiClient cashier = await _factory.CashierAsync();
        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => cashier.GetPortalDashboardAsync());
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task Caissier_sur_routes_manager_ou_admin_donne_403()
    {
        PressingApiClient cashier = await _factory.CashierAsync();

        // ManagerOrAdmin
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<ApiException>(() => cashier.GetReportAsync())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<ApiException>(() => cashier.GetSettingsAsync())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<ApiException>(() => cashier.GetEmailsAsync())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Assert.ThrowsAsync<ApiException>(() => cashier.CancelOrderAsync(1, "test"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Assert.ThrowsAsync<ApiException>(() => cashier.RefundAsync(1, new RefundRequest(100, PaymentMethod.Cash, null)))).StatusCode);

        // AdminOnly
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<ApiException>(() => cashier.GetStaffAsync())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<ApiException>(() => cashier.GetAuditAsync())).StatusCode);
    }

    [Fact]
    public async Task Manager_accede_aux_rapports_mais_pas_a_l_administration()
    {
        PressingApiClient manager = await _factory.ManagerAsync();

        Assert.NotNull(await manager.GetReportAsync());
        Assert.NotNull(await manager.GetSettingsAsync());

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => manager.GetStaffAsync());
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task Admin_accede_a_la_liste_du_personnel()
    {
        PressingApiClient admin = await _factory.AdminAsync();
        List<StaffUserDto>? staff = await admin.GetStaffAsync();

        Assert.NotNull(staff);
        Assert.Contains(staff!, s => s.Username == "admin");
        Assert.Contains(staff!, s => s.Username == "manager");
        Assert.Contains(staff!, s => s.Username == "caisse");
    }

    [Fact]
    public async Task Connexion_client_valide_et_refusee_si_mauvais_mot_de_passe()
    {
        PressingApiClient api = _factory.NewApiClient();
        AuthResponse? auth = await api.LoginClientAsync("awa.kone@example.com", ApiFactory.ClientPassword);
        Assert.Equal("Awa", auth!.Client!.FirstName);
        Assert.Null(auth.Staff);

        PressingApiClient bad = _factory.NewApiClient();
        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => bad.LoginClientAsync("awa.kone@example.com", "nope"));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }
}
