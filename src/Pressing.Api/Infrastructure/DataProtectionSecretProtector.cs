using Microsoft.AspNetCore.DataProtection;
using Pressing.Core.Security;
using Pressing.Core.Services;

namespace Pressing.Api.Infrastructure;

/// <summary>
/// Chiffre les secrets TOTP avec ASP.NET Core Data Protection (AES-256-CBC + HMAC-SHA256). Le trousseau de clés est
/// un dossier « dataprotection-keys » à côté de la base (comme jwt.key) : une sauvegarde de la base seule ne permet
/// pas de lire les secrets. À sauvegarder avec la base ; s'il est perdu, un administrateur réinitialise la 2FA du membre.
/// </summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Pressing.StaffTotpSecret.v1");
    }

    public string Protect(string plain) => _protector.Protect(plain);

    public string? Unprotect(string protectedValue)
    {
        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}

public static class StaffSecurityServiceCollectionExtensions
{
    /// <summary>Double authentification du personnel : Data Protection (clés à côté de la base) et service TOTP.</summary>
    public static IServiceCollection AddStaffSecurity(this IServiceCollection services, string databasePath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? Directory.GetCurrentDirectory();
        string keys = Path.Combine(directory, "dataprotection-keys");
        Directory.CreateDirectory(keys);

        services.AddDataProtection()
            .SetApplicationName("PressingEclat")
            .PersistKeysToFileSystem(new DirectoryInfo(keys));
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<TwoFactorService>();
        return services;
    }
}
