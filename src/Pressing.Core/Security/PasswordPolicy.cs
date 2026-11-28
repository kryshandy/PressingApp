namespace Pressing.Core.Security;

/// <summary>
/// Règle des mots de passe du personnel : 8 caractères minimum, avec au moins une lettre et un chiffre.
/// Appliquée à la création et à chaque changement ; jamais à la connexion (les comptes existants restent utilisables).
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    public const string Rule = "Le mot de passe doit contenir au moins 8 caractères, dont une lettre et un chiffre.";

    public static bool IsValid(string? password) =>
        !string.IsNullOrEmpty(password) &&
        password.Length >= MinLength &&
        password.Any(char.IsLetter) &&
        password.Any(char.IsDigit);

    /// <summary>Lève InvalidOperationException (HTTP 400, message en français) si le mot de passe ne respecte pas la règle.</summary>
    public static void EnsureValid(string? password)
    {
        if (!IsValid(password))
        {
            throw new InvalidOperationException(Rule);
        }
    }
}
