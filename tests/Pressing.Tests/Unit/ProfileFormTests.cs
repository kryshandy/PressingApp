extern alias WebApp;

using System.ComponentModel.DataAnnotations;
using Pressing.Core.Entities;
using ProfileForm = WebApp::Pressing.Web.Models.ProfileForm;

namespace Pressing.Tests.Unit;

/// <summary>Formulaire de profil du site : un canal de rappel exige un consentement explicite et la coordonnée correspondante.</summary>
public class ProfileFormTests
{
    private static ProfileForm Form(ClientNotificationPreference preference, bool consent, string? email = "awa@example.com") => new()
    {
        FirstName = "Awa", LastName = "Koné", Phone = "0701020304", WhatsApp = "0701020304",
        Email = email, NotificationPreference = preference, Consent = consent
    };

    private static List<ValidationResult> Validate(ProfileForm form)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Aucun_rappel_ne_demande_pas_de_consentement() =>
        Assert.Empty(Validate(Form(ClientNotificationPreference.None, consent: false)));

    [Theory]
    [InlineData(ClientNotificationPreference.WhatsApp)]
    [InlineData(ClientNotificationPreference.Email)]
    public void Un_canal_sans_consentement_est_refuse(ClientNotificationPreference preference)
    {
        var errors = Validate(Form(preference, consent: false));
        var error = Assert.Single(errors);
        Assert.Contains("consentement", error.ErrorMessage);
        Assert.Contains("Consent", error.MemberNames);
    }

    [Theory]
    [InlineData(ClientNotificationPreference.WhatsApp)]
    [InlineData(ClientNotificationPreference.Email)]
    public void Un_canal_avec_consentement_est_accepte(ClientNotificationPreference preference) =>
        Assert.Empty(Validate(Form(preference, consent: true)));

    [Fact]
    public void Canal_e_mail_sans_adresse_est_refuse()
    {
        var errors = Validate(Form(ClientNotificationPreference.Email, consent: true, email: null));
        Assert.Contains(errors, e => e.MemberNames.Contains("Email"));
    }

    [Fact]
    public void Canal_inconnu_est_refuse()
    {
        var errors = Validate(Form((ClientNotificationPreference)99, consent: true));
        Assert.Contains(errors, e => e.MemberNames.Contains("NotificationPreference"));
    }
}
