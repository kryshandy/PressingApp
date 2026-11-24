using System.Security.Cryptography;
using Pressing.Core.Data;

namespace Pressing.Api.Infrastructure;

public static class JwtKeyProvider
{
    public static string GetOrCreate(string databasePath)
    {
        string? environmentKey = Environment.GetEnvironmentVariable("PRESSING_JWT_KEY");
        if (!string.IsNullOrWhiteSpace(environmentKey) && environmentKey.Length >= 32)
        {
            return environmentKey;
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "jwt.key");

        if (File.Exists(path))
        {
            string existing = File.ReadAllText(path).Trim();
            if (existing.Length >= 32)
            {
                return existing;
            }
        }

        string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        File.WriteAllText(path, key);
        return key;
    }
}
