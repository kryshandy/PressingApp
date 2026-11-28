namespace Pressing.Core.Security;

/// <summary>
/// Chiffre les secrets stockés en base (secret TOTP). L'API fournit une implémentation fondée sur ASP.NET Core
/// Data Protection : une copie de la base ne suffit pas à lire les secrets, il faut aussi le trousseau de clés.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plain);

    /// <summary>Texte en clair, ou null si la valeur est illisible (trousseau absent ou altéré).</summary>
    string? Unprotect(string protectedValue);
}
