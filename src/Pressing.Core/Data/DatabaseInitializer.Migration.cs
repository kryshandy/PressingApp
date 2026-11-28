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
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({0}, {1});", new object[] { baseline, version }, ct);

            // Bases passées par la version du 27/11 (colonnes de consentement et table ReminderLogs créées en SQL
            // brut, sans migration) : RappelsEtConsentement y est déjà appliquée, on la marque comme telle.
            if (await HasLegacyReminderSchemaAsync(ct))
            {
                string reminders = _db.Database.GetMigrations().First(m => m.EndsWith("_RappelsEtConsentement", StringComparison.Ordinal));
                await _db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({0}, {1});", new object[] { reminders, version }, ct);
            }

            if ((await _db.Database.GetPendingMigrationsAsync(ct)).Any())
            {
                await BackupAsync("avant-migration", ct);
            }
        }
        else if (tables > 0 && (await _db.Database.GetPendingMigrationsAsync(ct)).Any())
        {
            await BackupAsync("avant-migration", ct);
        }

        await _db.Database.MigrateAsync(ct);
    }

    private async Task<bool> HasLegacyReminderSchemaAsync(CancellationToken ct)
    {
        int reminderTable = (await _db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name='ReminderLogs'")
            .ToListAsync(ct)).First();
        int consentColumn = (await _db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM pragma_table_info('Clients') WHERE name='NotificationPreference'")
            .ToListAsync(ct)).First();
        return reminderTable > 0 && consentColumn > 0;
    }

    private async Task BackupAsync(string motif, CancellationToken ct)
    {
        // Même dossier que les sauvegardes manuelles : PRESSING_BACKUP_PATH, sinon « Backups » à côté de la base.
        string? dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_options.Path));
        if (string.IsNullOrWhiteSpace(dir)) return;

        string backups = string.IsNullOrWhiteSpace(_options.BackupPath)
            ? System.IO.Path.Combine(dir, "Backups")
            : _options.BackupPath;
        Directory.CreateDirectory(backups);
        string target = System.IO.Path.Combine(backups, $"pressing-{motif}-{DateTime.Now:yyyyMMdd-HHmmss}.db");

        await _db.Database.ExecuteSqlRawAsync("VACUUM INTO {0};", new object[] { target }, ct);
    }
}
