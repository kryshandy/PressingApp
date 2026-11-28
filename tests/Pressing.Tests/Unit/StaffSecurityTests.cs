using System.Text;
using Microsoft.EntityFrameworkCore;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Security;
using Pressing.Core.Services;
using Pressing.Tests.Support;

namespace Pressing.Tests.Unit;

/// <summary>TOTP (vecteurs de la RFC 6238), politique de mot de passe et mots de passe à usage unique.</summary>
public class TotpTests
{
    private static readonly byte[] Sha1Key = Encoding.ASCII.GetBytes("12345678901234567890");
    private static readonly byte[] Sha256Key = Encoding.ASCII.GetBytes("12345678901234567890123456789012");
    private static readonly byte[] Sha512Key = Encoding.ASCII.GetBytes("1234567890123456789012345678901234567890123456789012345678901234");

    // RFC 6238, annexe B : (heure Unix, SHA-1, SHA-256, SHA-512), codes à 8 chiffres.
    public static TheoryData<long, string, string, string> Rfc6238Vectors => new()
    {
        { 59, "94287082", "46119246", "90693936" },
        { 1111111109, "07081804", "68084774", "25091201" },
        { 1111111111, "14050471", "67062674", "99943326" },
        { 1234567890, "89005924", "91819424", "93441116" },
        { 2000000000, "69279037", "90698825", "38618901" },
        { 20000000000, "65353130", "77737706", "47863826" }
    };

    [Theory]
    [MemberData(nameof(Rfc6238Vectors))]
    public void Vecteurs_de_la_RFC_6238(long unixTime, string sha1, string sha256, string sha512)
    {
        long step = unixTime / 30;
        Assert.Equal(sha1, Totp.ComputeForStep(Sha1Key, step, 8, "SHA1"));
        Assert.Equal(sha256, Totp.ComputeForStep(Sha256Key, step, 8, "SHA256"));
        Assert.Equal(sha512, Totp.ComputeForStep(Sha512Key, step, 8, "SHA512"));
    }

    [Fact]
    public void Code_a_6_chiffres_est_la_fin_du_code_a_8_chiffres()
    {
        string secret = Base32.Encode(Sha1Key);
        Assert.Equal("287082", Totp.Compute(secret, DateTimeOffset.FromUnixTimeSeconds(59)));
        Assert.Equal("081804", Totp.Compute(secret, DateTimeOffset.FromUnixTimeSeconds(1111111109)));
    }

    [Fact]
    public void Base32_aller_retour_et_valeur_connue()
    {
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Base32.Encode(Sha1Key));
        Assert.Equal(Sha1Key, Base32.Decode("gezd gnbv-GY3TQOJQGEZDGNBVGY3TQOJQ"));
        byte[] random = Enumerable.Range(0, 20).Select(i => (byte)(i * 13 + 7)).ToArray();
        Assert.Equal(random, Base32.Decode(Base32.Encode(random)));
        Assert.Throws<FormatException>(() => Base32.Decode("ABC1"));
    }

    [Fact]
    public void Fenetre_de_plus_ou_moins_un_pas()
    {
        string secret = Base32.Encode(Sha1Key);
        var now = DateTimeOffset.FromUnixTimeSeconds(1111111111);
        string current = Totp.Compute(secret, now);

        Assert.Equal(Totp.StepOf(now), Totp.Verify(secret, current, now));
        Assert.NotNull(Totp.Verify(secret, Totp.Compute(secret, now.AddSeconds(-30)), now));
        Assert.NotNull(Totp.Verify(secret, Totp.Compute(secret, now.AddSeconds(30)), now));
        Assert.Null(Totp.Verify(secret, Totp.Compute(secret, now.AddSeconds(-90)), now));
        Assert.Null(Totp.Verify(secret, Totp.Compute(secret, now.AddSeconds(90)), now));
        Assert.NotNull(Totp.Verify(secret, current[..3] + " " + current[3..], now));
        Assert.Null(Totp.Verify(secret, "12345", now));
        Assert.Null(Totp.Verify(secret, "abcdef", now));
        Assert.Null(Totp.Verify(secret, null, now));
    }

    [Fact]
    public void Uri_otpauth_compatible_Google_et_Microsoft_Authenticator()
    {
        string uri = Totp.BuildUri("Pressing Éclat", "awa", "JBSWY3DPEHPK3PXP");
        Assert.StartsWith("otpauth://totp/Pressing%20%C3%89clat:awa?secret=JBSWY3DPEHPK3PXP&issuer=Pressing%20%C3%89clat", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
    }

    [Fact]
    public void Secret_genere_fait_160_bits()
    {
        string secret = Totp.GenerateSecret();
        Assert.Equal(20, Base32.Decode(secret).Length);
        Assert.NotEqual(secret, Totp.GenerateSecret());
    }

    [Theory]
    [InlineData("Abcdefg1", true)]
    [InlineData("motdepasse2026", true)]
    [InlineData("Admin@2026!", true)]
    [InlineData("Abc12", false)]
    [InlineData("abcdefgh", false)]
    [InlineData("12345678", false)]
    [InlineData("", false)]
    public void Politique_de_mot_de_passe(string password, bool valid)
    {
        Assert.Equal(valid, PasswordPolicy.IsValid(password));
        if (!valid)
        {
            var ex = Assert.Throws<InvalidOperationException>(() => PasswordPolicy.EnsureValid(password));
            Assert.Contains("8 caractères", ex.Message);
            Assert.Contains("une lettre et un chiffre", ex.Message);
        }
    }
}

/// <summary>Verrouillage, double authentification et codes de secours, au niveau des services (horloge maîtrisée).</summary>
public sealed class StaffSecurityTests : IDisposable
{
    private const string Password = "Secret2026!";

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class ReversibleProtector : ISecretProtector
    {
        public string Protect(string plain) => "prot:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
        public string? Unprotect(string protectedValue) =>
            protectedValue.StartsWith("prot:", StringComparison.Ordinal)
                ? Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue[5..]))
                : null;
    }

    private readonly TestDb _t = new();
    private readonly FakeClock _clock = new();
    private readonly StaffAuthService _auth;
    private readonly TwoFactorService _twoFactor;
    private readonly StaffService _staff;
    private int _n;

    public StaffSecurityTests()
    {
        var tokens = new TokenService(new TokenOptions { Key = "tests-only-jwt-key-0123456789-abcdefghijklmnopqrstuvwxyz" });
        var audit = _t.Get<AuditService>();
        _twoFactor = new TwoFactorService(_t.Db, new ReversibleProtector(), audit, _clock, _t.Settings);
        _auth = new StaffAuthService(_t.Db, tokens, audit, _twoFactor, _clock);
        _staff = new StaffService(_t.Db, audit);
        Tokens = tokens;
    }

    private TokenService Tokens { get; }

    private async Task<StaffUser> AddStaffAsync(UserRole role)
    {
        int n = ++_n;
        var user = new StaffUser
        {
            FullName = "Membre " + n,
            Username = "membre" + n,
            Role = role,
            PasswordHash = PasswordHasher.Hash(Password)
        };
        _t.Db.StaffUsers.Add(user);
        await _t.Db.SaveChangesAsync();
        return user;
    }

    private Task<AuthResponse> LoginAsync(StaffUser user, string password = Password) =>
        _auth.LoginAsync(new StaffLoginRequest(user.Username, password));

    private async Task FailAsync(StaffUser user, int times)
    {
        for (int i = 0; i < times; i++)
        {
            await Assert.ThrowsAnyAsync<Exception>(() => LoginAsync(user, "mauvais1"));
        }
    }

    private async Task<(StaffUser User, string Secret, IReadOnlyList<string> Recovery)> ManagerWithTwoFactorAsync()
    {
        StaffUser manager = await AddStaffAsync(UserRole.Manager);
        TwoFactorSetupDto setup = await _twoFactor.BeginSetupAsync(manager.Id);
        TwoFactorEnabledDto enabled = await _twoFactor.EnableAsync(manager.Id, Totp.Compute(setup.Secret, _clock.Now));
        return (manager, setup.Secret, enabled.RecoveryCodes);
    }

    // ---------------------------------------------------------------- verrouillage

    [Fact]
    public async Task Quatre_echecs_ne_verrouillent_pas_le_cinquieme_oui_avec_l_heure_de_deverrouillage()
    {
        StaffUser user = await AddStaffAsync(UserRole.Cashier);
        await FailAsync(user, 4);
        var locked = await Assert.ThrowsAsync<StaffAccountLockedException>(() => LoginAsync(user, "dernier-1"));
        Assert.Equal(new DateTime(2026, 12, 1, 10, 15, 0), locked.Until);
        Assert.Contains("10:15", locked.Message);
        Assert.Contains("verrouillé", locked.Message);

        // Même avec le bon mot de passe, tant que le verrouillage dure.
        var again = await Assert.ThrowsAsync<StaffAccountLockedException>(() => LoginAsync(user));
        Assert.Contains("10:15", again.Message);
    }

    [Fact]
    public async Task Le_verrouillage_expire_apres_15_minutes_et_un_succes_remet_le_compteur_a_zero()
    {
        StaffUser user = await AddStaffAsync(UserRole.Cashier);
        await FailAsync(user, 5);

        _clock.Now = _clock.Now.AddMinutes(14);
        await Assert.ThrowsAsync<StaffAccountLockedException>(() => LoginAsync(user));

        _clock.Now = _clock.Now.AddMinutes(2);
        AuthResponse ok = await LoginAsync(user);
        Assert.NotNull(ok.Staff);

        StaffUser saved = await _t.Db.StaffUsers.AsNoTracking().SingleAsync(s => s.Id == user.Id);
        Assert.Equal(0, saved.FailedLoginCount);
        Assert.Null(saved.LockedUntil);
    }

    [Fact]
    public async Task Un_succes_interrompt_la_serie_d_echecs()
    {
        StaffUser user = await AddStaffAsync(UserRole.Cashier);
        await FailAsync(user, 4);
        await LoginAsync(user);
        await FailAsync(user, 4); // 4 nouveaux échecs seulement : pas de verrouillage
        Assert.NotNull((await LoginAsync(user)).Staff);
    }

    [Fact]
    public async Task Apres_expiration_le_compteur_repart_de_zero()
    {
        StaffUser user = await AddStaffAsync(UserRole.Cashier);
        await FailAsync(user, 5);
        _clock.Now = _clock.Now.AddMinutes(16);

        await FailAsync(user, 4); // 4 échecs après expiration : toujours pas verrouillé
        Assert.NotNull((await LoginAsync(user)).Staff);
    }

    [Fact]
    public async Task Identifiant_inconnu_et_mauvais_mot_de_passe_donnent_le_meme_message()
    {
        StaffUser user = await AddStaffAsync(UserRole.Cashier);
        var wrong = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => LoginAsync(user, "mauvais1"));
        var unknown = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _auth.LoginAsync(new StaffLoginRequest("personne-ici", "mauvais1")));
        Assert.Equal(wrong.Message, unknown.Message);

        // Un identifiant inconnu ne peut jamais « se verrouiller ».
        for (int i = 0; i < 8; i++)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => _auth.LoginAsync(new StaffLoginRequest("personne-ici", "mauvais1")));
        }
    }

    [Fact]
    public async Task L_administrateur_deverrouille_et_les_deux_actions_sont_auditees()
    {
        StaffUser user = await AddStaffAsync(UserRole.Cashier);
        await FailAsync(user, 5);
        await Assert.ThrowsAsync<StaffAccountLockedException>(() => LoginAsync(user));

        var admin = new CurrentUser { StaffId = 1, Name = "Admin", Role = "Admin" };
        MessageResponse result = await _staff.UnlockAsync(user.Id, admin);
        Assert.Contains("déverrouillé", result.Message);

        Assert.NotNull((await LoginAsync(user)).Staff);
        List<string> actions = await _t.Db.AuditLogs.Select(a => a.Action).ToListAsync();
        Assert.Contains("lockout", actions);
        Assert.Contains("unlock", actions);
    }

    [Fact]
    public async Task Les_echecs_du_second_facteur_comptent_aussi_pour_le_verrouillage()
    {
        var (manager, _, _) = await ManagerWithTwoFactorAsync();
        AuthResponse step1 = await LoginAsync(manager);
        for (int i = 0; i < 4; i++)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _auth.LoginMfaAsync(step1.Token, "000000"));
        }

        await Assert.ThrowsAsync<StaffAccountLockedException>(() => _auth.LoginMfaAsync(step1.Token, "000000"));
        await Assert.ThrowsAsync<StaffAccountLockedException>(() => LoginAsync(manager));
    }

    // ---------------------------------------------------------------- double authentification

    [Fact]
    public async Task Connexion_en_deux_temps_avec_jeton_intermediaire()
    {
        var (manager, secret, _) = await ManagerWithTwoFactorAsync();

        AuthResponse first = await LoginAsync(manager);
        Assert.True(first.MfaRequired);
        Assert.Null(first.Staff);
        var principal = Tokens.Validate(first.Token)!;
        Assert.Equal("mfa-pending", principal.FindFirst("token_type")!.Value);
        Assert.Null(principal.FindFirst(System.Security.Claims.ClaimTypes.Role));
        Assert.True(first.ExpiresAt <= DateTime.UtcNow.AddMinutes(5).AddSeconds(5));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _auth.LoginMfaAsync(first.Token, "123456"));

        _clock.Now = _clock.Now.AddSeconds(30); // pas suivant : accepté dans la fenêtre, et non rejoué
        AuthResponse second = await _auth.LoginMfaAsync(first.Token, Totp.Compute(secret, _clock.Now));
        Assert.False(second.MfaRequired);
        Assert.Equal(manager.Id, second.Staff!.Id);
        Assert.Equal("staff", Tokens.Validate(second.Token)!.FindFirst("token_type")!.Value);
    }

    [Fact]
    public async Task Un_code_deja_utilise_est_refuse_au_rejeu()
    {
        var (manager, secret, _) = await ManagerWithTwoFactorAsync();
        string code = Totp.Compute(secret, _clock.Now);

        AuthResponse a = await LoginAsync(manager);
        await _auth.LoginMfaAsync(a.Token, code);

        AuthResponse b = await LoginAsync(manager);
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _auth.LoginMfaAsync(b.Token, code));
        Assert.Contains("incorrect ou déjà utilisé", ex.Message);
    }

    [Fact]
    public async Task Jeton_non_intermediaire_ou_expire_est_refuse_au_second_temps()
    {
        var (manager, secret, _) = await ManagerWithTwoFactorAsync();
        string code = Totp.Compute(secret, _clock.Now);

        // Un vrai jeton de session ne sert pas de jeton intermédiaire.
        var (staffToken, _) = Tokens.CreateStaffToken(manager);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _auth.LoginMfaAsync(staffToken, code));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _auth.LoginMfaAsync(null, code));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _auth.LoginMfaAsync("n'importe-quoi", code));
    }

    [Fact]
    public async Task Codes_de_secours_huit_a_usage_unique_et_haches()
    {
        var (manager, _, recovery) = await ManagerWithTwoFactorAsync();
        Assert.Equal(8, recovery.Count);
        Assert.Equal(8, recovery.Distinct().Count());
        Assert.All(recovery, c => Assert.Matches("^[A-Z2-9]{5}-[A-Z2-9]{5}$", c));

        StaffUser stored = await _t.Db.StaffUsers.AsNoTracking().SingleAsync(s => s.Id == manager.Id);
        Assert.All(recovery, c => Assert.DoesNotContain(c.Replace("-", string.Empty), stored.RecoveryCodeHashes!));

        AuthResponse first = await LoginAsync(manager);
        Assert.NotNull((await _auth.LoginMfaAsync(first.Token, recovery[0].ToLowerInvariant())).Staff);

        AuthResponse second = await LoginAsync(manager);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _auth.LoginMfaAsync(second.Token, recovery[0]));
        Assert.NotNull((await _auth.LoginMfaAsync(second.Token, recovery[1])).Staff);

        Assert.Equal(6, (await _twoFactor.GetStatusAsync(manager.Id)).RecoveryCodesLeft);
    }

    [Fact]
    public async Task Le_secret_est_chiffre_en_base()
    {
        StaffUser manager = await AddStaffAsync(UserRole.Manager);
        TwoFactorSetupDto setup = await _twoFactor.BeginSetupAsync(manager.Id);
        StaffUser stored = await _t.Db.StaffUsers.AsNoTracking().SingleAsync(s => s.Id == manager.Id);

        Assert.NotNull(stored.TwoFactorSecret);
        Assert.DoesNotContain(setup.Secret, stored.TwoFactorSecret);
        Assert.False(stored.TwoFactorEnabled); // pas active tant qu'un code n'a pas été vérifié
        Assert.Contains("otpauth://totp/Pressing%20%C3%89clat:" + manager.Username, setup.OtpAuthUri);
        Assert.Contains("secret=" + setup.Secret, setup.OtpAuthUri);
    }

    [Fact]
    public async Task Activation_refusee_avec_un_mauvais_code()
    {
        StaffUser manager = await AddStaffAsync(UserRole.Manager);
        await _twoFactor.BeginSetupAsync(manager.Id);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _twoFactor.EnableAsync(manager.Id, "000000"));
        Assert.Contains("Code incorrect", ex.Message);
        Assert.False((await _t.Db.StaffUsers.AsNoTracking().SingleAsync(s => s.Id == manager.Id)).TwoFactorEnabled);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Cashier)]
    public async Task Seul_le_manager_dispose_de_la_double_authentification(UserRole role)
    {
        StaffUser user = await AddStaffAsync(role);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _twoFactor.BeginSetupAsync(user.Id));
        Assert.Contains("Manager", ex.Message);

        TwoFactorStatusDto status = await _twoFactor.GetStatusAsync(user.Id);
        Assert.False(status.Available);
        Assert.False(status.Required);

        AuthResponse login = await LoginAsync(user);
        Assert.False(login.MfaRequired);
        Assert.False(login.TwoFactorSetupRequired);
    }

    [Fact]
    public async Task Manager_sans_2fa_est_conduit_a_l_activer_sauf_si_l_exigence_est_desactivee()
    {
        StaffUser manager = await AddStaffAsync(UserRole.Manager);
        AuthResponse required = await LoginAsync(manager);
        Assert.True(required.TwoFactorSetupRequired);
        Assert.NotNull(required.Staff);

        await _t.SetSettingAsync(SettingsKeys.RequireManagerTwoFactor, "false");
        AuthResponse free = await LoginAsync(manager);
        Assert.False(free.TwoFactorSetupRequired);
        Assert.False(free.MfaRequired);
    }

    [Fact]
    public async Task Exigence_desactivee_un_manager_avec_2fa_se_connecte_avec_le_seul_mot_de_passe()
    {
        var (manager, _, _) = await ManagerWithTwoFactorAsync();
        Assert.True((await LoginAsync(manager)).MfaRequired);

        await _t.SetSettingAsync(SettingsKeys.RequireManagerTwoFactor, "false");
        AuthResponse direct = await LoginAsync(manager);
        Assert.False(direct.MfaRequired);
        Assert.NotNull(direct.Staff);

        await _t.SetSettingAsync(SettingsKeys.RequireManagerTwoFactor, "true");
        Assert.True((await LoginAsync(manager)).MfaRequired);
    }

    [Fact]
    public async Task Reinitialisation_par_l_administrateur_efface_secret_et_codes_et_est_auditee()
    {
        var (manager, _, _) = await ManagerWithTwoFactorAsync();
        var admin = new CurrentUser { StaffId = 1, Name = "Admin", Role = "Admin" };

        await _twoFactor.ResetAsync(manager.Id, admin);

        StaffUser stored = await _t.Db.StaffUsers.AsNoTracking().SingleAsync(s => s.Id == manager.Id);
        Assert.False(stored.TwoFactorEnabled);
        Assert.Null(stored.TwoFactorSecret);
        Assert.Null(stored.RecoveryCodeHashes);
        Assert.Contains("2fa-reset", await _t.Db.AuditLogs.Select(a => a.Action).ToListAsync());

        // Il devra la réactiver à la prochaine connexion (réglage global actif).
        Assert.True((await LoginAsync(manager)).TwoFactorSetupRequired);
    }

    [Fact]
    public async Task Le_manager_ne_peut_pas_desactiver_lui_meme_tant_que_c_est_exige()
    {
        var (manager, secret, _) = await ManagerWithTwoFactorAsync();
        var request = new DisableTwoFactorRequest(Password, Totp.Compute(secret, _clock.Now));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _twoFactor.DisableAsync(manager.Id, request));
        Assert.Contains("administrateur", ex.Message);

        await _t.SetSettingAsync(SettingsKeys.RequireManagerTwoFactor, "false");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _twoFactor.DisableAsync(manager.Id, request with { Password = "mauvais-1" }));
        await _twoFactor.DisableAsync(manager.Id, request);
        Assert.False((await _t.Db.StaffUsers.AsNoTracking().SingleAsync(s => s.Id == manager.Id)).TwoFactorEnabled);
    }

    // ---------------------------------------------------------------- mots de passe

    [Fact]
    public async Task Politique_appliquee_a_la_creation_au_changement_et_a_la_reinitialisation()
    {
        var admin = new CurrentUser { StaffId = 1, Name = "Admin", Role = "Admin" };
        var weak = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _staff.CreateAsync(new CreateStaffRequest("Test Faible", "faible", null, UserRole.Cashier, "abcdefgh"), admin));
        Assert.Equal(PasswordPolicy.Rule, weak.Message);

        StaffUserDto created = await _staff.CreateAsync(
            new CreateStaffRequest("Test Fort", "fort", null, UserRole.Cashier, "Motdepasse1"), admin);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _staff.SetPasswordAsync(created.Id, "court1", admin));
        await _staff.SetPasswordAsync(created.Id, "Nouveau2026", admin);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _auth.ChangePasswordAsync(created.Id, new ChangeStaffPasswordRequest("Nouveau2026", "sansChiffre")));
        await _auth.ChangePasswordAsync(created.Id, new ChangeStaffPasswordRequest("Nouveau2026", "Encore2026"));

        // La politique ne s'applique pas à la connexion : un ancien mot de passe de 6 caractères reste utilisable.
        StaffUser legacy = await AddStaffAsync(UserRole.Cashier);
        legacy.PasswordHash = PasswordHasher.Hash("abc123");
        await _t.Db.SaveChangesAsync();
        Assert.NotNull((await LoginAsync(legacy, "abc123")).Staff);
    }

    public void Dispose() => _t.Dispose();
}
