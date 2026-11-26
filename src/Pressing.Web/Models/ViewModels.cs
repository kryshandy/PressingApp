using System.ComponentModel.DataAnnotations;
using Pressing.Client;
using Pressing.Core.Dtos;

namespace Pressing.Web.Models;

public record ErrorViewModel(int Code, string? Message);

// ---------- Public ----------

public record HomeViewModel(PublicInfoDto Info);

public record TarifsViewModel(PublicInfoDto Info);

public class SuiviViewModel
{
    public const string RequiredMessage = "Veuillez indiquer un numéro de suivi et un numéro de téléphone.";
    public const string PartialMessage = "Renseignez à la fois le numéro de ticket et le téléphone utilisé lors du dépôt.";

    public string Ticket { get; set; } = "";
    public string Phone { get; set; } = "";
    public bool Searched { get; set; }
    public TrackingDto? Result { get; set; }
    public string? Error { get; set; }
}

public class ContactForm
{
    [Required(ErrorMessage = "Merci d'indiquer votre nom.")]
    [StringLength(120, ErrorMessage = "Le nom est trop long (120 caractères maximum).")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Merci d'indiquer votre adresse e-mail.")]
    [EmailAddress(ErrorMessage = "Cette adresse e-mail ne semble pas valide.")]
    [StringLength(160, ErrorMessage = "L'adresse e-mail est trop longue.")]
    public string Email { get; set; } = "";

    [StringLength(30, ErrorMessage = "Le numéro de téléphone est trop long.")]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "Votre message est vide.")]
    [StringLength(2000, MinimumLength = 10, ErrorMessage = "Votre message doit contenir entre 10 et 2 000 caractères.")]
    public string Message { get; set; } = "";

    /// <summary>Champ piège (invisible) : un robot le remplit, une personne jamais.</summary>
    public string? Website { get; set; }
}

public class ContactViewModel
{
    public ContactForm Form { get; set; } = new();
    public Pressing.Core.Options.ShopInfo Shop { get; set; } = Infrastructure.ShopDirectory.Fallback;
}

// ---------- Compte ----------

public class LoginForm
{
    [Required(ErrorMessage = "Indiquez votre téléphone ou votre adresse e-mail.")]
    public string Identifier { get; set; } = "";

    [Required(ErrorMessage = "Indiquez votre mot de passe.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public bool Remember { get; set; } = true;
    public string? ReturnUrl { get; set; }
}

public class ForgotForm
{
    [Required(ErrorMessage = "Indiquez votre téléphone ou votre adresse e-mail.")]
    public string Identifier { get; set; } = "";
}

public class ResetForm
{
    [Required(ErrorMessage = "Le lien de réinitialisation est incomplet.")]
    public string Token { get; set; } = "";

    [Required(ErrorMessage = "Choisissez un nouveau mot de passe.")]
    [MinLength(6, ErrorMessage = "Le mot de passe doit contenir au moins 6 caractères.")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Confirmez le nouveau mot de passe.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Les deux mots de passe ne correspondent pas.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = "";
}

public class ChangePasswordForm
{
    [Required(ErrorMessage = "Indiquez votre mot de passe actuel.")]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = "";

    [Required(ErrorMessage = "Choisissez un nouveau mot de passe.")]
    [MinLength(6, ErrorMessage = "Le mot de passe doit contenir au moins 6 caractères.")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Confirmez le nouveau mot de passe.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Les deux mots de passe ne correspondent pas.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = "";
}

public class ProfileForm
{
    [Required(ErrorMessage = "Le prénom est obligatoire.")]
    [StringLength(80, ErrorMessage = "Le prénom est trop long.")]
    public string FirstName { get; set; } = "";

    [Required(ErrorMessage = "Le nom est obligatoire.")]
    [StringLength(80, ErrorMessage = "Le nom est trop long.")]
    public string LastName { get; set; } = "";

    [Required(ErrorMessage = "Le téléphone est obligatoire.")]
    [StringLength(30, ErrorMessage = "Le numéro de téléphone est trop long.")]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = "Le numéro WhatsApp est obligatoire.")]
    [StringLength(30, ErrorMessage = "Le numéro WhatsApp est trop long.")]
    public string WhatsApp { get; set; } = "";

    [EmailAddress(ErrorMessage = "Cette adresse e-mail ne semble pas valide.")]
    [StringLength(160, ErrorMessage = "L'adresse e-mail est trop longue.")]
    public string? Email { get; set; }

    [StringLength(250, ErrorMessage = "L'adresse est trop longue.")]
    public string? Address { get; set; }
}

public record DashboardViewModel(PortalDashboardDto Data);

public record OrdersViewModel(
    string Filter,
    IReadOnlyList<OrderSummaryDto> Items,
    int CountAll,
    int CountActive,
    int CountDone);

public record OrderDetailViewModel(OrderDto Order);

public record LoyaltyViewModel(ClientDto Client, IReadOnlyList<LoyaltyTransactionDto> Transactions);
