using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Pressing.Core.Data;

public partial class DatabaseInitializer
{
    /// <summary>
    /// Applique les migrations. Une base ancienne (créée par EnsureCreated, sans historique)
    /// est sauvegardée puis « adoptée » : Baseline est marquée comme déjà appliquée.
    /// Toute migration en attente est précédée d'une sauvegarde.
    /// </summary>
    private async Task MigrateOrAdoptAsync(CancellationToken ct)
    {
        int tables = (await _db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")
            .ToListAsync(ct)).First();
        int history = (await _db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'")
            .ToListAsync(ct)).First();

        if (tables > 0 && history == 0)
        {
            await BackupAsync("avant-adoption", ct);

            string baseline = _db.Database.GetMigrations().First();
            string version = typeof(DbContext).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?.Split('+')[0] ?? "10.0.0";

            await _db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL);", ct);
            await _db.Database.ExecuteSqlRawAsync(
                $"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('{baseline}', '{version}');", ct);
        }
        else if (tables > 0 && (await _db.Database.GetPendingMigrationsAsync(ct)).Any())
        {
            await BackupAsync("avant-migration", ct);
        }

        await _db.Database.MigrateAsync(ct);
    }

    private async Task BackupAsync(string motif, CancellationToken ct)
    {
        string? dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_options.Path));
        if (string.IsNullOrWhiteSpace(dir)) return;

        string backups = System.IO.Path.Combine(dir, "Backups");
        Directory.CreateDirectory(backups);
        string target = System.IO.Path.Combine(backups, $"pressing-{motif}-{DateTime.Now:yyyyMMdd-HHmmss}.db");

        await _db.Database.ExecuteSqlRawAsync($"VACUUM INTO '{target.Replace("'", "''")}';", ct);
    }
}
